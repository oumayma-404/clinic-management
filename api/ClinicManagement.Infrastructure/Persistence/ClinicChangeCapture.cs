using System.Data.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Relay;
using ClinicManagement.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>The request's <c>Idempotency-Key</c>, recorded on every change it makes (D17). Null outside a request.</summary>
public interface IIdempotencyKeyAccessor
{
    string? Current { get; }
}

/// <summary>
/// Appends a <see cref="ClinicChange"/> per key a save touches, inside the save's own transaction (D1, D2): the
/// clinic's cursor row is advanced with <c>UPDATE … RETURNING</c>, whose row lock is what makes commit order seq order.
/// A clinic with no cursor row (no PC de secours) changes nothing.
///
/// <para><b>It is also the lease's net (D15)</b>, because it is the one place that sees every carried row a save
/// touches and the cabinet each belongs to: on the cloud, a cabinet whose PC may hold its saves accepts only
/// <see cref="RelayFence.AllowedOnFencedCloud"/>; on a PC de secours that does not hold them, only a sign-in's traces.
/// Anything else throws <see cref="ClinicFencedException"/> before a row is written — a job, a backfill or any path the
/// gate never sees.</para>
/// </summary>
public sealed class ClinicChangeCapture
{
    private readonly bool _enabled;
    private readonly bool _isRelay;
    private readonly ClinicChangeOrigin _origin;
    private readonly IIdempotencyKeyAccessor? _idempotency;
    private readonly IRelayLocalStatus? _relayLocal;

    public ClinicChangeCapture(
        DeploymentProfile profile, IIdempotencyKeyAccessor? idempotency = null, IRelayLocalStatus? relayLocal = null)
    {
        _enabled = profile.PublishesChangeFeed || profile.MirrorsCloudClinic;
        _isRelay = profile.MirrorsCloudClinic;
        _origin = profile.MirrorsCloudClinic ? ClinicChangeOrigin.Relay : ClinicChangeOrigin.Cloud;
        _idempotency = idempotency;
        _relayLocal = relayLocal;
    }

    /// <summary>
    /// The PC de secours's half of the net (D15), decided before any SQL: a PC that does not hold the cabinet's saves
    /// writes a sign-in's traces and nothing else. ⚠️ No local status means « not holding » — a PC that cannot tell
    /// must not write over the copy.
    /// </summary>
    public void EnsureThisSideMayWrite(DbContext context)
    {
        if (!_enabled || !_isRelay || _relayLocal?.IsHolding == true)
        {
            return;
        }

        var plan = ClinicRelayScope.For(context.Model);
        if (context.ChangeTracker.Entries().Any(e => IsCandidate(plan, e) && !RelayFence.IsSignInTrace(e)))
        {
            var (error, code) = RelayRefusals.ForPcNotHolding(_relayLocal?.IsRetired == true, _relayLocal?.IsHandingBack == true);
            throw new ClinicFencedException(error, code);
        }
    }

    /// <summary>Whether this save touches a relay-scoped row at all — the cheap test run before any SQL.</summary>
    public bool HasCandidates(DbContext context)
    {
        if (!_enabled)
        {
            return false;
        }

        var plan = ClinicRelayScope.For(context.Model);
        return context.ChangeTracker.Entries().Any(e => IsCandidate(plan, e));
    }

    /// <summary>Resolves each touched key's clinic and stages its change rows. Must run inside the save's transaction.</summary>
    public async Task AppendAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!_enabled)
        {
            return;
        }

        var transaction = context.Database.CurrentTransaction
                          ?? throw new InvalidOperationException("La capture des modifications exige une transaction.");

        var plan = ClinicRelayScope.For(context.Model);
        var tracked = new TrackedIndex(context.ChangeTracker.Entries().ToList());
        var entries = tracked.All.Where(e => IsCandidate(plan, e)).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var resolver = new ClinicResolver(context, plan, transaction, tracked);
        var touched = new Dictionary<(Guid Clinic, string Table, string Key), ClinicChangeOp>();
        var fenceable = new HashSet<Guid>();

        foreach (var entry in entries)
        {
            var (table, row) = OwnerRow(plan, entry, tracked);
            if (table is null || row is null)
            {
                continue;
            }

            var clinicId = await resolver.ClinicOfAsync(table, row, cancellationToken);
            if (clinicId is null)
            {
                continue;
            }

            if (!RelayFence.AllowedOnFencedCloud.ContainsKey(OwnerTableName(plan, entry, table)))
            {
                fenceable.Add(clinicId.Value);
            }

            var op = ReferenceEquals(row, entry) && entry.State == EntityState.Deleted
                ? ClinicChangeOp.Delete
                : ClinicChangeOp.Upsert;
            var key = KeyOf(table, row);
            var slot = (clinicId.Value, table.Name, key);
            touched[slot] = touched.TryGetValue(slot, out var previous) && previous == ClinicChangeOp.Delete
                ? ClinicChangeOp.Delete
                : op;
        }

        var now = DateTime.UtcNow;
        if (!_isRelay)
        {
            await EnsureCloudMayWriteAsync(context, fenceable, now, cancellationToken);
        }
        else if (_relayLocal?.IsHolding != true)
        {
            // A following PC writes only a sign-in's traces, which are each side's own and merged at the return: no log.
            return;
        }

        var idempotencyKey = _idempotency?.Current;

        foreach (var clinic in touched.Keys.Select(k => k.Clinic).Distinct().Order())
        {
            var changes = touched.Where(t => t.Key.Clinic == clinic)
                .OrderBy(t => t.Key.Table, StringComparer.Ordinal)
                .ThenBy(t => t.Key.Key, StringComparer.Ordinal)
                .ToList();

            var last = _isRelay
                ? await AdvanceOwnCursorAsync(context, transaction, clinic, changes.Count, cancellationToken)
                : await AdvanceCursorAsync(context, transaction, clinic, changes.Count, cancellationToken);
            if (last is null)
            {
                continue;
            }

            // D18: the PC's log of the cut is what the return sends. The return takes this same row lock before it
            // reads, after it stopped taking saves — so a save that got here first is waited for, and one that got here
            // after sees the return and is refused: nothing is saved that the return could miss.
            if (_isRelay && _relayLocal?.IsHolding != true)
            {
                var (error, code) = RelayRefusals.ForPcNotHolding(_relayLocal?.IsRetired == true, _relayLocal?.IsHandingBack == true);
                throw new ClinicFencedException(error, code);
            }

            var seq = last.Value - changes.Count;
            foreach (var change in changes)
            {
                context.Add(new ClinicChange(
                    clinic, ++seq, change.Key.Table, change.Key.Key, change.Value, _origin, idempotencyKey, now));
            }
        }
    }

    /// <summary>
    /// The cloud's half of the net (D15): a cabinet whose PC may hold its saves accepts only FR-11's tables. One read
    /// of the current relay per cabinet the save touches outside them — the gate's predicate, on this transaction.
    /// </summary>
    private static async Task EnsureCloudMayWriteAsync(
        DbContext context, IEnumerable<Guid> clinics, DateTime nowUtc, CancellationToken cancellationToken)
    {
        foreach (var clinic in clinics)
        {
            var relay = await ClinicRelayRepository
                .CurrentFor(context.Set<ClinicRelay>().IgnoreQueryFilters().AsNoTracking(), clinic)
                .FirstOrDefaultAsync(cancellationToken);
            if (RelayFence.CloudRefuses(relay, nowUtc))
            {
                var (error, code) = RelayRefusals.ForFencedCloud(relay!.PcHoldingSinceUtc, nowUtc, relay.IsReturning,
                    relay.IsRecoveringGap(nowUtc));
                throw new ClinicFencedException(error, code);
            }
        }
    }

    /// <summary>The entry's own table — an owned row is judged as its owner's table, a child row as its own.</summary>
    private static string OwnerTableName(ClinicRelayPlan plan, EntityEntry entry, ClinicRelayTable ownerTable)
    {
        var type = entry.Metadata;
        while (type.IsOwned())
        {
            type = type.FindOwnership()!.PrincipalEntityType;
        }

        return plan.Find(type.ClrType)?.Name ?? ownerTable.Name;
    }

    private static bool IsCandidate(ClinicRelayPlan plan, EntityEntry entry)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            return false;
        }

        var type = entry.Metadata;
        while (type.IsOwned())
        {
            type = type.FindOwnership()!.PrincipalEntityType;
        }

        return plan.Find(type.ClrType) is not null;
    }

    /// <summary>An owned row is a change to its owner's row (D5): the owner travels whole, its owned lists included.</summary>
    private static (ClinicRelayTable? Table, EntityEntry? Row) OwnerRow(
        ClinicRelayPlan plan, EntityEntry entry, TrackedIndex tracked)
    {
        var current = entry;
        while (current.Metadata.IsOwned())
        {
            var ownership = current.Metadata.FindOwnership()!;
            var owner = tracked.Find(ownership.PrincipalEntityType,
                ownership.Properties.Select(p => Value(current, p)).ToArray());
            if (owner is null)
            {
                return (null, null);
            }

            current = owner;
        }

        return (plan.Find(current.Metadata.ClrType), current);
    }

    internal static object? Value(EntityEntry entry, IProperty property) =>
        entry.State == EntityState.Deleted
            ? entry.Property(property.Name).OriginalValue
            : entry.Property(property.Name).CurrentValue;

    internal static string KeyOf(ClinicRelayTable table, EntityEntry entry) =>
        string.Join(ClinicChange.KeySeparator,
            table.EntityType.FindPrimaryKey()!.Properties.Select(p => FormatKey(Value(entry, p))));

    internal static string FormatKey(object? value) => value switch
    {
        null => string.Empty,
        Guid g => g.ToString("D"),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>The save's tracked entries, read once (each <c>Entries()</c> call re-runs change detection).</summary>
    private sealed class TrackedIndex
    {
        private readonly Dictionary<(IEntityType Type, string Key), EntityEntry> _byKey = new();

        public TrackedIndex(IReadOnlyList<EntityEntry> all)
        {
            All = all;
            foreach (var entry in all)
            {
                var key = entry.Metadata.FindPrimaryKey();
                if (key is not null)
                {
                    _byKey.TryAdd((entry.Metadata, Join(key.Properties.Select(p => Value(entry, p)))), entry);
                }
            }
        }

        public IReadOnlyList<EntityEntry> All { get; }

        public EntityEntry? Find(IEntityType type, IEnumerable<object?> key) =>
            _byKey.GetValueOrDefault((type, Join(key)));

        private static string Join(IEnumerable<object?> values) =>
            string.Join(ClinicChange.KeySeparator, values.Select(FormatKey));
    }

    private static async Task<long?> AdvanceCursorAsync(
        DbContext context, IDbContextTransaction transaction, Guid clinicId, int count, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "UPDATE \"ClinicChangeCursors\" SET \"LastSeq\" = \"LastSeq\" + @n WHERE \"ClinicId\" = @c RETURNING \"LastSeq\"";
        AddParameter(command, "n", (long)count);
        AddParameter(command, "c", clinicId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// D18, on a PC de secours holding the cut: its own log of the cut, whose cursor is created by the first save after
    /// the takeover and dropped once the cut is handed back.
    /// </summary>
    private static async Task<long?> AdvanceOwnCursorAsync(
        DbContext context, IDbContextTransaction transaction, Guid clinicId, int count, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText =
            "INSERT INTO \"ClinicChangeCursors\" (\"ClinicId\", \"LastSeq\") VALUES (@c, @n) "
            + "ON CONFLICT (\"ClinicId\") DO UPDATE SET \"LastSeq\" = \"ClinicChangeCursors\".\"LastSeq\" + @n RETURNING \"LastSeq\"";
        AddParameter(command, "n", (long)count);
        AddParameter(command, "c", clinicId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Finds a row's clinic: its own column, else its tracked parent, else one indexed lookup per level.</summary>
    private sealed class ClinicResolver
    {
        private readonly DbContext _context;
        private readonly ClinicRelayPlan _plan;
        private readonly IDbContextTransaction _transaction;
        private readonly TrackedIndex _tracked;
        private readonly Dictionary<(string Table, string Key), Guid?> _cache = new();

        public ClinicResolver(DbContext context, ClinicRelayPlan plan, IDbContextTransaction transaction, TrackedIndex tracked)
        {
            _context = context;
            _plan = plan;
            _transaction = transaction;
            _tracked = tracked;
        }

        public async Task<Guid?> ClinicOfAsync(ClinicRelayTable table, EntityEntry row, CancellationToken ct)
        {
            switch (table.Scope)
            {
                case ClinicRelayTableScope.Self:
                    return Value(row, table.EntityType.FindPrimaryKey()!.Properties[0]) as Guid?;
                case ClinicRelayTableScope.Direct:
                    return Value(row, table.EntityType.FindProperty(ClinicArchiveScope.ClinicIdProperty)!) as Guid?;
                default:
                    var fk = table.EntityType.GetForeignKeys().First(f =>
                        f.Properties.Count == 1 && f.PrincipalEntityType.ClrType.Name == table.ParentTable && f.IsRequired);
                    var parentKey = Value(row, fk.Properties[0]);
                    if (parentKey is null)
                    {
                        return null;
                    }

                    var parent = _plan.Find(table.ParentTable!)!;
                    var tracked = _tracked.Find(parent.EntityType, new[] { parentKey });
                    return tracked is not null
                        ? await ClinicOfAsync(parent, tracked, ct)
                        : await LookUpAsync(parent, parentKey, ct);
            }
        }

        private async Task<Guid?> LookUpAsync(ClinicRelayTable table, object key, CancellationToken ct)
        {
            var slot = (table.Name, FormatKey(key));
            if (_cache.TryGetValue(slot, out var cached))
            {
                return cached;
            }

            Guid? clinic;
            if (table.Scope == ClinicRelayTableScope.Self)
            {
                clinic = key as Guid?;
            }
            else
            {
                var column = table.Scope == ClinicRelayTableScope.Direct ? table.ClinicColumn! : table.ParentColumn!;
                var value = await ScalarAsync(
                    $"SELECT {ClinicRelaySql.Quote(column)} FROM {table.QualifiedName} WHERE {ClinicRelaySql.Quote(table.KeyColumns[0])} = @k",
                    key, ct);
                clinic = value is null
                    ? null
                    : table.Scope == ClinicRelayTableScope.Direct
                        ? value as Guid?
                        : await LookUpAsync(_plan.Find(table.ParentTable!)!, value, ct);
            }

            _cache[slot] = clinic;
            return clinic;
        }

        private async Task<object?> ScalarAsync(string sql, object key, CancellationToken ct)
        {
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.Transaction = _transaction.GetDbTransaction();
            command.CommandText = sql;
            AddParameter(command, "k", key);
            var result = await command.ExecuteScalarAsync(ct);
            return result is DBNull ? null : result;
        }
    }
}
