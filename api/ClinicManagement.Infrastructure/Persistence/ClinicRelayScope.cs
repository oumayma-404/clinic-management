using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Which tables a PC de secours holds, how each is scoped to the clinic, and the order to apply them in (D4).
/// Derived from the EF model like the archive: a table added next year is carried unless it is named in <see cref="Excluded"/>.
/// </summary>
public static class ClinicRelayScope
{
    /// <summary>What the archive leaves out and the relay carries, each with its reason (FR-1).</summary>
    public static readonly IReadOnlyDictionary<string, string> Added = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [nameof(User)] = "people sign in on the PC during a cut",
        [nameof(UserRecoveryCode)] = "a recovery code works on the PC, and one spent there stays spent",
        [nameof(UserDashboardPreference)] = "the same person's dashboard on the PC",
        [nameof(ClinicSubscription)] = "the PC enforces the mirrored entitlement (FR-4)",
        [nameof(SubscriptionPeriod)] = "the entitlement is a fold of its ledger",
        [nameof(Notification)] = "reminders booked during a cut are sent at the return (AC-5.4)",
        [nameof(StaffNotification)] = "the clinic's bell (FR-1)",
        [nameof(NotificationRead)] = "who read which bell row",
        [nameof(NotificationDismissal)] = "who cleared which bell row",
    };

    /// <summary>Never carried. The archive's exclusions minus <see cref="Added"/>, plus what only the cloud may hold.</summary>
    public static readonly IReadOnlySet<string> Excluded = ClinicArchiveScope.Excluded
        .Where(name => !Added.ContainsKey(name))
        .Concat(new[]
        {
            // The vendor's WhatsApp forfait: cloud-owned, and the PC sends no WhatsApp.
            nameof(MessagingAllowanceEntry),
            nameof(ClinicMessagingMonth),
            // An upload in progress on the cloud; the finished file arrives as its PatientFile row.
            nameof(FileUploadSession),
            // Each install signs its own sessions.
            nameof(SessionFamily),
            nameof(PasswordResetRequest),
        })
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Secrets that leave the cloud sealed for the PC's own key, and are re-protected under the PC's ring (D8).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> WrappedSecrets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [nameof(User)] = new[] { nameof(User.ProtectedTotpSecret) },
        };

    /// <summary>Written as null on the way out, on the archive's terms: the Google link and the join code stay in the cloud (FR-1).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Redacted = ClinicArchiveScope.Redacted;

    /// <summary>Columns that legitimately differ between the two sides and are left out of the hourly digest (D25).</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> PerSideColumns =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            // The TOTP secret is sealed per PC; the sign-in traces are each side's own (a sign-in on the PC is allowed
            // in standby) and are merged field by field at the return (D18) — neither is drift.
            [nameof(User)] = new[]
            {
                nameof(User.ProtectedTotpSecret), nameof(User.LastLoginAt), nameof(User.FailedLoginAttempts),
                nameof(User.LockoutEnd),
            },
            [nameof(Clinic)] = ClinicArchiveScope.Redacted[nameof(Clinic)].ToArray(),
        };

    private static readonly object Gate = new();
    private static IModel? _cachedModel;
    private static ClinicRelayPlan? _cachedPlan;

    /// <summary>The plan for <paramref name="model"/>, resolved once per model.</summary>
    public static ClinicRelayPlan For(IModel model)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_cachedModel, model) || _cachedPlan is null)
            {
                _cachedPlan = Resolve(model);
                _cachedModel = model;
            }

            return _cachedPlan;
        }
    }

    public static ClinicRelayPlan Resolve(IModel model)
    {
        var candidates = model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.ClrType != typeof(object) && e.GetTableName() is not null)
            .Where(e => !Excluded.Contains(e.ClrType.Name))
            .GroupBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .ToList();

        var planned = candidates.Select(e => e.ClrType.Name).ToHashSet(StringComparer.Ordinal);
        var scopes = new Dictionary<string, (ClinicRelayTableScope Scope, IForeignKey? Link)>(StringComparer.Ordinal);
        var pending = candidates.ToList();

        // Scoping: Clinic itself, then any table with its own ClinicId, then children through a required single-column FK.
        foreach (var entity in pending.ToList())
        {
            if (entity.ClrType == typeof(Clinic))
            {
                scopes[entity.ClrType.Name] = (ClinicRelayTableScope.Self, null);
            }
            else if (entity.FindProperty(ClinicArchiveScope.ClinicIdProperty) is { } p
                     && (p.ClrType == typeof(Guid) || p.ClrType == typeof(Guid?)))
            {
                scopes[entity.ClrType.Name] = (ClinicRelayTableScope.Direct, null);
            }
        }

        bool progressed;
        do
        {
            progressed = false;
            foreach (var entity in pending.Where(e => !scopes.ContainsKey(e.ClrType.Name)).ToList())
            {
                var link = entity.GetForeignKeys().FirstOrDefault(fk =>
                    fk.IsRequired
                    && fk.Properties.Count == 1
                    && fk.PrincipalEntityType.ClrType != entity.ClrType
                    && scopes.ContainsKey(fk.PrincipalEntityType.ClrType.Name));
                if (link is not null)
                {
                    scopes[entity.ClrType.Name] = (ClinicRelayTableScope.Child, link);
                    progressed = true;
                }
            }
        }
        while (progressed);

        var unplaced = pending.Where(e => !scopes.ContainsKey(e.ClrType.Name)).Select(e => e.ClrType.Name).ToList();
        var scoped = pending.Where(e => scopes.ContainsKey(e.ClrType.Name)).ToList();

        // Ordering: every FK into a planned table counts, optional ones included; a cycle is broken by deferring the
        // back-edge columns, which the applier writes in a second pass.
        var order = new List<IEntityType>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        var remaining = scoped.ToList();
        while (remaining.Count > 0)
        {
            bool Ready(IEntityType e, bool requiredOnly) => e.GetForeignKeys().All(fk =>
                (requiredOnly && !fk.IsRequired)
                || fk.PrincipalEntityType.ClrType == e.ClrType
                || !planned.Contains(fk.PrincipalEntityType.ClrType.Name)
                || placed.Contains(fk.PrincipalEntityType.ClrType.Name));

            var next = remaining.FirstOrDefault(e => Ready(e, requiredOnly: false))
                       ?? remaining.FirstOrDefault(e => Ready(e, requiredOnly: true))
                       ?? remaining[0];
            order.Add(next);
            placed.Add(next.ClrType.Name);
            remaining.Remove(next);
        }

        var position = order.Select((e, i) => (e.ClrType.Name, i)).ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);
        var tables = order.Select(entity => Describe(entity, scopes[entity.ClrType.Name], position, planned)).ToList();
        return new ClinicRelayPlan(tables, unplaced);
    }

    private static ClinicRelayTable Describe(
        IEntityType entity,
        (ClinicRelayTableScope Scope, IForeignKey? Link) scope,
        IReadOnlyDictionary<string, int> position,
        IReadOnlySet<string> planned)
    {
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        string Column(IProperty p) => p.GetColumnName(store) ?? p.Name;

        var key = entity.FindPrimaryKey()!.Properties.Select(Column).ToList();
        var mine = position[entity.ClrType.Name];

        var deferred = entity.GetForeignKeys()
            .Where(fk => planned.Contains(fk.PrincipalEntityType.ClrType.Name)
                         && position.TryGetValue(fk.PrincipalEntityType.ClrType.Name, out var theirs)
                         && theirs >= mine)
            .SelectMany(fk => fk.Properties)
            .Where(p => p.IsNullable && !key.Contains(Column(p)))
            .Select(Column)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var owned = entity.GetNavigations()
            .Where(n => n.IsCollection && n.TargetEntityType.IsOwned() && n.TargetEntityType.GetTableName() is not null)
            .Select(n =>
            {
                var target = n.TargetEntityType;
                var targetStore = StoreObjectIdentifier.Table(target.GetTableName()!, target.GetSchema());
                var ownership = target.FindOwnership()!;
                return new ClinicRelayOwnedTable(
                    n.Name,
                    target.GetTableName()!,
                    target.GetSchema(),
                    ownership.Properties.Select(p => p.GetColumnName(targetStore) ?? p.Name).ToList(),
                    ownership.PrincipalKey.Properties.Select(Column).ToList(),
                    target.FindPrimaryKey()!.Properties
                        .Where(p => p.ValueGenerated != ValueGenerated.Never && !ownership.Properties.Contains(p))
                        .Select(p => p.GetColumnName(targetStore) ?? p.Name)
                        .ToList());
            })
            .ToList();

        string? clinicColumn = scope.Scope switch
        {
            ClinicRelayTableScope.Direct => Column(entity.FindProperty(ClinicArchiveScope.ClinicIdProperty)!),
            ClinicRelayTableScope.Self => key[0],
            _ => null,
        };

        return new ClinicRelayTable(
            entity,
            entity.ClrType.Name,
            entity.GetTableName()!,
            entity.GetSchema(),
            scope.Scope,
            clinicColumn,
            scope.Link is null ? null : Column(scope.Link.Properties[0]),
            scope.Link?.PrincipalEntityType.ClrType.Name,
            scope.Link is null
                ? null
                : scope.Link.PrincipalKey.Properties[0].GetColumnName(StoreObjectIdentifier.Table(
                    scope.Link.PrincipalEntityType.GetTableName()!, scope.Link.PrincipalEntityType.GetSchema())),
            key,
            deferred,
            owned);
    }
}

public enum ClinicRelayTableScope
{
    /// <summary>The clinic's own row, matched on its key.</summary>
    Self,

    /// <summary>Matched on the table's own ClinicId column.</summary>
    Direct,

    /// <summary>Matched through a required foreign key into an already-scoped parent.</summary>
    Child,
}

/// <summary>An owned collection stored in its own table, carried as a whole list with its owner (D5).</summary>
public sealed record ClinicRelayOwnedTable(
    string Navigation,
    string TableName,
    string? Schema,
    IReadOnlyList<string> OwnerColumns,
    IReadOnlyList<string> OwnerKeyColumns,
    IReadOnlyList<string> GeneratedColumns)
{
    public string QualifiedName => ClinicRelaySql.Qualify(Schema, TableName);
}

public sealed record ClinicRelayTable(
    IEntityType EntityType,
    string Name,
    string TableName,
    string? Schema,
    ClinicRelayTableScope Scope,
    string? ClinicColumn,
    string? ParentColumn,
    string? ParentTable,
    string? ParentKeyColumn,
    IReadOnlyList<string> KeyColumns,
    IReadOnlyList<string> DeferredColumns,
    IReadOnlyList<ClinicRelayOwnedTable> OwnedCollections)
{
    public string QualifiedName => ClinicRelaySql.Qualify(Schema, TableName);
}

public sealed record ClinicRelayPlan(IReadOnlyList<ClinicRelayTable> Tables, IReadOnlyList<string> Unplaced)
{
    private readonly Dictionary<string, ClinicRelayTable> _byName =
        Tables.ToDictionary(t => t.Name, StringComparer.Ordinal);

    private readonly Dictionary<Type, ClinicRelayTable> _byClr =
        Tables.ToDictionary(t => t.EntityType.ClrType);

    public ClinicRelayTable? Find(string name) => _byName.GetValueOrDefault(name);

    public ClinicRelayTable? Find(Type clrType) => _byClr.GetValueOrDefault(clrType);
}

/// <summary>Identifier quoting for the relay's raw SQL; every name it receives comes from the EF model, never from a request.</summary>
public static class ClinicRelaySql
{
    public static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public static string Qualify(string? schema, string table) =>
        string.IsNullOrEmpty(schema) ? Quote(table) : Quote(schema) + "." + Quote(table);
}
