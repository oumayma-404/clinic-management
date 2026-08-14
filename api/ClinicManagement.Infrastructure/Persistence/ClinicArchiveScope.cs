using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// <b>Which tables an archive carries, in which order, and what is deliberately left out of it</b>
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para>The set is <b>derived from the EF model</b> and not listed: every non-owned entity type with a path to a
/// clinic is archived unless it is named in <see cref="Excluded"/>. That direction matters — a table added next
/// year is archived on the day it is written, whereas an inclusion list is only ever as complete as the last
/// person who remembered it, and the symptom of forgetting is a restore that quietly puts back less than the
/// practice had.</para>
///
/// <para><b>How a table is scoped to one cabinet.</b> Two shapes, and no third: it carries a <c>ClinicId</c> of its
/// own, or it is a child reached through a foreign key to a table already resolved. A table with neither is
/// reported rather than silently dropped.</para>
///
/// <para><b>⚠️ Scoping and ordering are two questions, and conflating them cost a total-loss restore.</b> Which
/// rows belong to a cabinet is decided by <i>required</i> foreign keys — an optional one is a reference, not
/// ownership. What order the tables must be <i>applied</i> in is decided by <b>every</b> foreign key, required or
/// not: <c>Appointment.PatientId</c> is nullable and still enforced when set. The directly-owned tables used to be
/// appended in the model's own enumeration order with no regard for the keys between them, so on a full restore —
/// the case the feature exists for — <c>DentalRecord</c> reached the database before <c>Patient</c> and the
/// operation died part-way. Both are now settled by one fixpoint walk over every planned table.</para>
/// </summary>
public static class ClinicArchiveScope
{
    /// <summary>The column a directly-owned table is selected on.</summary>
    public const string ClinicIdProperty = "ClinicId";

    /// <summary>
    /// What an archive never contains, each for a stated reason. Every entry here is a decision; nothing is
    /// excluded merely because it was awkward.
    ///
    /// <para><b>The vendor's money.</b> <see cref="ClinicSubscription"/> and <see cref="SubscriptionPeriod"/> are
    /// what the cabinet owes <i>us</i> (<c>clinic-subscription</c> FR-2), and including them would let a practice
    /// restore its own entitlement from a file it controls — turning « read-only until you pay » into a zip.</para>
    ///
    /// <para><b>Machine state in flight.</b> The three outboxes (<see cref="Notification"/>,
    /// <see cref="PushDelivery"/>, <see cref="DocumentEmail"/>), the in-app feed
    /// (<see cref="StaffNotification"/>, <see cref="NotificationRead"/>), the push registry
    /// (<see cref="DeviceRegistration"/>) and the backup ledger (<see cref="BackupRun"/>) are transient. Putting a
    /// due outbox row back would send SMS reminders about visits that happened months ago — the restore's most
    /// visible possible failure, and one aimed at patients rather than at staff.</para>
    ///
    /// <para><b>Credentials.</b> <see cref="User"/> holds password hashes, which do not travel in a file on a
    /// laptop; the console path re-provisions the admin instead. <see cref="ClinicReminderSettings"/> holds
    /// per-clinic secrets encrypted under a Data Protection key ring that is <i>not</i> in the archive, so they
    /// would restore as undecryptable and each channel would silently read « non configuré » — worse than absent,
    /// because absent is visible.</para>
    ///
    /// <para><b>The ledger of who did what.</b> <see cref="AuditEntry"/> is excluded because re-inserting it would
    /// let a restore write history, and because AC-9 has this operation <i>appear</i> in that ledger rather than
    /// rewrite it.</para>
    ///
    /// <para><b>The vendor's own tables</b> — the console's accounts, its access journal and the portfolio counters
    /// — belong to no cabinet and are measurements <i>of</i> one. <see cref="ClinicSignup"/> carries no clinic at
    /// all, by construction.</para>
    ///
    /// <para><b>Personal interface state, not clinic work.</b> <see cref="UserDashboardPreference"/> is one
    /// person's card layout, keyed on an account this archive deliberately does not carry — so restoring it would
    /// put a stranger's dashboard arrangement under whoever the new administrator turns out to be.</para>
    /// </summary>
    public static readonly IReadOnlySet<string> Excluded = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(ClinicSubscription),
        nameof(SubscriptionPeriod),
        nameof(Notification),
        nameof(PushDelivery),
        nameof(DocumentEmail),
        nameof(StaffNotification),
        nameof(NotificationRead),
        nameof(DeviceRegistration),
        nameof(BackupRun),
        // ⚠️ Beside BackupRun, but for a sharper reason than transience: a ClinicRecoveryPoint names a STORAGE KEY.
        // Restoring these rows would hand a practice a list of recovery points whose objects retention may have
        // pruned months ago — offering a recovery that cannot be performed, which is worse than offering none. They
        // are also measurements of the deployment rather than records of the practice, exactly like the backup ledger.
        nameof(ClinicRecoveryPoint),
        nameof(AuditEntry),
        nameof(User),
        nameof(UserDashboardPreference),
        nameof(ClinicReminderSettings),
        nameof(PlatformAccount),
        nameof(PlatformRecoveryCode),
        nameof(PlatformAccessEntry),
        nameof(ClinicActivityDay),
        nameof(ClinicActivitySnapshot),
        nameof(ClinicSignup),
        // The Data Protection key ring, where DataProtection:PersistToDatabase puts it. Deployment-wide key
        // material and the single most dangerous thing that could travel in a cabinet's zip: the archive is
        // deliberately UNENCRYPTED and kept on a practice's laptop, and these rows decrypt every administrator's
        // second factor and every clinic's reminder credentials — including other cabinets'. It has no path to a
        // clinic either, so without this entry it would be *reported* as unarchivable on every export.
        "DataProtectionKey",
    };

    /// <summary>
    /// Values written as <c>null</c> into the archive, because they are secrets rather than records.
    ///
    /// <para>The cabinet's Google connection is an OAuth refresh token and the calendar it is bound to: a
    /// long-lived credential for a third-party account, which has no business in a file the practice keeps on a
    /// laptop or hands to us for a restore. Reconnecting Google is two clicks on « Paramètres »; a leaked refresh
    /// token is not undoable from this application at all.</para>
    ///
    /// <para><see cref="Clinic.Code"/> is here on the same criterion and not because it looks like one: it is the
    /// six characters <c>POST /api/auth/register</c> accepts to attach a new account to a practice, so on the one
    /// profile where that door is open it is a credential rather than a record — and that is precisely the profile
    /// whose archive is a portable file carried between machines on a USB stick. A restored cabinet mints a fresh
    /// one; nothing reads it back.</para>
    ///
    /// <para>Nulled rather than the row being excluded: everything else on <see cref="Clinic"/> — the name, the
    /// address, the billing settings, the working hours, the recall interval — is exactly what a restored cabinet
    /// must come back with.</para>
    ///
    /// <para>⚠️ <b>Completeness is asserted, not remembered.</b> Inclusion is derived and this list is hand-written,
    /// so the direction is inverted against the safe default: a credential-bearing column added next year would
    /// travel into a deliberately unencrypted zip with no compile error and no failing test.
    /// <c>ClinicArchiveScopeTests</c> therefore reflects over every <i>planned</i> entity for a secret-shaped
    /// property name and fails unless the entity is <see cref="Excluded"/> or the property is named here.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Redacted =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [nameof(Clinic)] = new HashSet<string>(StringComparer.Ordinal)
            {
                nameof(Clinic.GoogleRefreshToken),
                // Ciphertext, and redacted all the same: the archive deliberately does not carry the key ring, so
                // it would restore as an undecryptable token that reads as « connecté » and syncs nothing.
                nameof(Clinic.GoogleRefreshTokenProtected),
                nameof(Clinic.GoogleCalendarId),
                nameof(Clinic.Code),
            },
        };

    /// <summary>
    /// The properties holding a <b>storage key</b>, so the packager knows which blobs a table's rows point at.
    ///
    /// <para>Declared rather than derived — « this string is a storage key » is not something the model can say,
    /// and guessing from a name would sweep up <see cref="Clinic.LogoUrl"/>'s neighbours and miss
    /// <see cref="Doctor.CachetStorageKey"/>'s. <see cref="MedicalDocument"/> is deliberately absent: its rendered
    /// PDF is stored <i>as a <see cref="PatientFile"/></i> (<c>MedicalDocument.FileId</c>), so its bytes already
    /// travel with that row rather than needing a second mechanism.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> BlobProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(PatientFile)] = nameof(PatientFile.StorageKey),
            [nameof(Doctor)] = nameof(Doctor.CachetStorageKey),
            [nameof(Clinic)] = nameof(Clinic.LogoUrl),
        };

    /// <summary>
    /// Resolves the archive plan from <paramref name="model"/>: every archivable table with how it is scoped to a
    /// cabinet, parents first, plus a French warning per table that has no path to one.
    /// </summary>
    public static ClinicArchivePlan Resolve(IModel model)
    {
        var candidates = model.GetEntityTypes()
            .Where(e => !e.IsOwned() && !e.HasSharedClrType && e.ClrType != typeof(object))
            .Where(e => !Excluded.Contains(e.ClrType.Name))
            .GroupBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var ordered = new List<ClinicArchiveTablePlan>();
        var resolved = new Dictionary<string, IEntityType>(StringComparer.Ordinal);
        var warnings = new List<string>();

        // Ordered by name before the walk, so a pass that can admit several tables at once admits them in a
        // stable order — the manifest lists the tables and two runs of the same model must produce the same file.
        var pending = candidates.OrderBy(e => e.ClrType.Name, StringComparer.Ordinal).ToList();
        var planned = pending.Select(e => e.ClrType.Name).ToHashSet(StringComparer.Ordinal);

        // The cabinet's own record goes first, and it is its own shape: Clinic has no ClinicId — it IS the
        // clinic — so neither of the two rules below can place it. Archiving it is what carries the practice's
        // name, address, billing settings, working hours and recall interval, and it is what lets the console
        // path re-create a cabinet that no longer exists rather than hand its owner a blank one.
        var clinic = pending.FirstOrDefault(e => e.ClrType == typeof(Clinic));
        if (clinic is not null)
        {
            ordered.Add(new ClinicArchiveTablePlan(clinic, ClinicArchiveTableScope.Self, null, null));
            resolved.Add(clinic.ClrType.Name, clinic);
            pending.Remove(clinic);
        }

        // One fixpoint over every remaining table, admitting one when it is both *scopable* and *insertable*:
        // its rows can be found for a cabinet, and every table it points at is already in the plan.
        AdmitToFixpoint(pending, ordered, resolved, planned, requireInsertableOrder: true);

        // A cycle between two archived tables cannot satisfy the ordering term, and refusing to archive them
        // would put back less than the practice had — so they are admitted anyway and the file says so. The
        // restore's per-row parent check is what keeps this honest rather than merely optimistic.
        if (pending.Count > 0)
        {
            var before = pending.Count;
            AdmitToFixpoint(pending, ordered, resolved, planned, requireInsertableOrder: false);

            if (pending.Count < before)
            {
                warnings.Add(
                    "Certaines tables se référencent mutuellement : leur ordre de restauration ne peut pas être "
                    + "garanti et certains enregistrements pourraient être ignorés.");
            }
        }

        warnings.AddRange(pending
            .Select(e => $"« {e.ClrType.Name} » n'a pas pu être rattachée au cabinet et n'est pas incluse dans l'archive."));

        return new ClinicArchivePlan(ordered, warnings);
    }

    /// <summary>
    /// Admits every table it can, repeatedly, until a pass admits nothing. <paramref name="requireInsertableOrder"/>
    /// off drops the « every table it points at is already planned » term, which is the cycle-breaking second pass.
    /// </summary>
    private static void AdmitToFixpoint(
        List<IEntityType> pending,
        List<ClinicArchiveTablePlan> ordered,
        Dictionary<string, IEntityType> resolved,
        IReadOnlySet<string> planned,
        bool requireInsertableOrder)
    {
        bool admittedSomething;

        do
        {
            admittedSomething = false;

            foreach (var entity in pending.ToList())
            {
                // Direct wins over Child where both apply: a table stating its own clinic needs no parent, and
                // selecting it through one would scope it by whichever relation happened to be walked first.
                var link = HasOwnClinicId(entity) ? null : FindResolvedParentLink(entity, resolved);
                var scope = HasOwnClinicId(entity) ? ClinicArchiveTableScope.Direct
                    : link is null ? (ClinicArchiveTableScope?)null
                    : ClinicArchiveTableScope.Child;

                if (scope is null)
                {
                    continue;
                }

                if (requireInsertableOrder && !ReferencesOnlyPlannedTables(entity, planned, resolved))
                {
                    continue;
                }

                ordered.Add(new ClinicArchiveTablePlan(
                    entity, scope.Value, link?.ForeignKeyProperty, link?.ParentTable));
                resolved.Add(entity.ClrType.Name, entity);
                pending.Remove(entity);
                admittedSomething = true;
            }
        }
        while (admittedSomething);
    }

    /// <summary>
    /// Whether every archived table <paramref name="entity"/> holds a foreign key into is already in the plan —
    /// the ordering term, and the one place <b>optional</b> keys count.
    ///
    /// <para>A nullable FK is not ownership, so it must not scope a table; but it is still a real constraint the
    /// database enforces whenever the column is set, so it absolutely governs the order the inserts run in. A
    /// key into a table this archive does not carry imposes nothing — the row it points at was never ours to
    /// restore.</para>
    /// </summary>
    private static bool ReferencesOnlyPlannedTables(
        IEntityType entity,
        IReadOnlySet<string> planned,
        IReadOnlyDictionary<string, IEntityType> resolved)
    {
        foreach (var fk in entity.GetForeignKeys())
        {
            var parentName = fk.PrincipalEntityType.ClrType.Name;

            if (parentName == entity.ClrType.Name || !planned.Contains(parentName))
            {
                continue;
            }

            if (!resolved.ContainsKey(parentName))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasOwnClinicId(IEntityType entity) =>
        entity.FindProperty(ClinicIdProperty) is { ClrType: var t } && t == typeof(Guid);

    /// <summary>
    /// The single-column foreign key by which <paramref name="entity"/> hangs off a table already in the plan.
    ///
    /// <para><b>Required keys only.</b> An optional FK is a reference, not ownership — a
    /// <c>TreatmentPlanItemId</c> on an appointment does not make the appointment part of that plan — and
    /// following one would scope a table by whichever parent it happened to mention. A composite key is skipped
    /// for the same reason it has no single id to match on.</para>
    /// </summary>
    private static (string ForeignKeyProperty, string ParentTable)? FindResolvedParentLink(
        IEntityType entity,
        IReadOnlyDictionary<string, IEntityType> resolved)
    {
        foreach (var fk in entity.GetForeignKeys())
        {
            if (fk.Properties.Count != 1 || !fk.IsRequired)
            {
                continue;
            }

            var property = fk.Properties[0];
            if (property.ClrType != typeof(Guid))
            {
                continue;
            }

            var parentName = fk.PrincipalEntityType.ClrType.Name;
            if (resolved.ContainsKey(parentName) && parentName != entity.ClrType.Name)
            {
                return (property.Name, parentName);
            }
        }

        return null;
    }
}

/// <summary>The resolved archive plan: the tables to walk in order, and what could not be placed.</summary>
public sealed record ClinicArchivePlan(
    IReadOnlyList<ClinicArchiveTablePlan> Tables,
    IReadOnlyList<string> Warnings);

/// <summary>The three — and only three — ways a table's rows are found for one cabinet.</summary>
public enum ClinicArchiveTableScope
{
    /// <summary>The cabinet's own record: matched on its primary key. <see cref="Clinic"/> alone.</summary>
    Self,

    /// <summary>Matched on the table's own <c>ClinicId</c> column.</summary>
    Direct,

    /// <summary>Matched through a required foreign key into the ids already collected for the parent table.</summary>
    Child,
}

/// <summary>One table, and how its rows are found for a cabinet.</summary>
public sealed record ClinicArchiveTablePlan(
    IEntityType EntityType,
    ClinicArchiveTableScope Scope,
    string? ForeignKeyProperty,
    string? ParentTable)
{
    public string Name => EntityType.ClrType.Name;
}
