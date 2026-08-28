using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Reads a cabinet's rows out and puts missing ones back, driven entirely by the EF model
/// (<c>clinic-data-archive-and-restore</c>). The only part of the archive that knows about EF Core.
///
/// <para><b>Why nothing here goes through a domain constructor.</b> Every primary key in this product is a GUID
/// minted inside the constructor and a good half of the timestamps are stamped there from
/// <c>DateTime.UtcNow</c> — <c>PatientFile.UploadedAt</c>, <c>Invoice.CreatedAt</c>, and so on. Building entities
/// the ordinary way would give every restored row a <i>new identity and today's date</i>, which is the exact
/// opposite of a restore and would break the one property the whole feature rests on: that a row still present is
/// recognisable as the same row. So an instance is created uninitialised and the model's own properties are
/// written directly, which is also what lets the restore be keyed on the original ids (AC-3).</para>
///
/// <para><b>Why a property bag rather than a DTO per entity.</b> Thirty-odd tables would be thirty-odd DTOs, each
/// a second definition of what a row is, and every column added later would need remembering in two places — the
/// <c>fixes-dont-propagate</c> shape, with the symptom being a column that silently stops being archived. The bag
/// is read off <c>IEntityType.GetProperties()</c>, so a new column travels on the day it is written.</para>
///
/// <para>⚠️ <b>Store-generated is not the same question as « has a database default », and reading it as one cost
/// the feature its two worst defects.</b> EF marks <i>every</i> property configured with <c>HasDefaultValue(…)</c>
/// as <see cref="ValueGenerated.OnAdd"/>, and so is a single <c>Guid</c> key on a configuration that does not say
/// <c>ValueGeneratedNever()</c>. Excluding <c>OnAdd</c> wholesale therefore dropped <c>Payment.IsVoided</c> (a
/// voided payment restored as live money), <c>Patient.IsArchived</c>, the clinic's billing settings, every devis's
/// act ordering — and, for the three configurations that declare no key generation, the <b>primary key</b>, which
/// made every ordonnance, certificat and antécédent médical silently unrestorable. What must genuinely not be
/// written back is the concurrency token and anything the database computes; a column with a default is a column
/// whose value we hold, and <see cref="StageInsert"/> insists on writing it.</para>
///
/// <para>⚠️ <b>The concurrency token is deliberately not archived.</b> <c>Entity&lt;T&gt;.Version</c> is mapped
/// onto PostgreSQL's <c>xmin</c> system column, so it is store-generated on every write and has no meaning
/// outside the row's own transaction history — writing one back would be asserting a transaction id from another
/// database.</para>
///
/// <para>⚠️ <b>The restore carries state across tables, exactly as the export does.</b> Which ids belong to this
/// cabinet is what a child row's parent is checked against, so it is accumulated table by table in the order the
/// plan walks them. The service is scoped and one restore runs per scope.</para>
/// </summary>
public class ClinicArchiveStore : IClinicArchiveStore
{
    /// <summary>
    /// How many parent ids go into one <c>IN</c>. PostgreSQL takes far more, but a clinic with several thousand
    /// invoices would otherwise produce a single query with a parameter per invoice — chunking keeps every
    /// statement an ordinary size at the cost of a few more round trips on an operation that runs by hand.
    /// </summary>
    private const int ParentIdChunk = 1000;

    private readonly ApplicationDbContext _context;
    private readonly ClinicArchivePlan _plan;

    /// <summary>
    /// Per table, the ids this cabinet legitimately holds — live rows plus the ones this restore inserted. A
    /// <c>Child</c> row is admissible only when its parent is in here, which is what stops a hand-edited archive
    /// hanging a payment off <i>another practice's</i> invoice: those twelve tables carry no <c>ClinicId</c> of
    /// their own, so the foreign key in the file is otherwise their only clinic identity.
    /// </summary>
    private readonly Dictionary<string, HashSet<Guid>> _cabinetIds = new(StringComparer.Ordinal);

    /// <summary>Per table, the ids skipped because the live row differs — their children are skipped too (see the class remarks on aggregates).</summary>
    private readonly Dictionary<string, HashSet<Guid>> _conflictingIds = new(StringComparer.Ordinal);

    /// <summary>The entries this restore staged, so <see cref="ForgetRestoredRows"/> releases those and nothing else.</summary>
    private readonly List<EntityEntry> _staged = new();

    public ClinicArchiveStore(ApplicationDbContext context)
    {
        _context = context;
        _plan = ClinicArchiveScope.Resolve(context.Model);
    }

    /// <summary>
    /// ⚠️ <b>The whole export is ONE <c>RepeatableRead</c> snapshot, and without it an archive can be internally
    /// inconsistent.</b> This walks ~35 tables in sequence, and PostgreSQL's default <c>ReadCommitted</c> gives
    /// every statement its <i>own</i> snapshot — so a cabinet working while the archive is built can have a
    /// patient created between the <c>Patients</c> read and the <c>Appointments</c> read, producing a visit whose
    /// patient the file does not contain, or a payment captured against an invoice snapshot older than it.
    /// <see cref="ClinicArchiveScope"/>'s FK-ordered apply sequence is what makes a restore survive that ordering,
    /// and it assumes the rows were captured <i>together</i>; nothing had been making that true.
    ///
    /// <para>It was survivable while an archive was something an administrator clicked deliberately, out of hours.
    /// <c>clinic-archive-auto-copy</c> takes one on a schedule, unattended, which means mid-consultation — exactly
    /// when writes land between two table reads.</para>
    ///
    /// <para><c>RepeatableRead</c> rather than <c>Serializable</c>: this transaction only ever reads, so the one
    /// guarantee needed is a stable snapshot, and the stricter level would add serialization-failure retries to a
    /// read that has nothing to serialize against.</para>
    /// </summary>
    public async Task<ClinicArchiveExport> ExportAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        var tables = new List<ClinicArchiveTableData>();
        var storageKeys = new List<string>();
        var idsByTable = new Dictionary<string, HashSet<Guid>>(StringComparer.Ordinal);

        await using var snapshot = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);

        foreach (var table in _plan.Tables)
        {
            var parentIds = table.Scope == ClinicArchiveTableScope.Child
                ? idsByTable.GetValueOrDefault(table.ParentTable!) ?? new HashSet<Guid>()
                : null;

            var rows = await ReadRowsAsync(table, clinicId, parentIds, cancellationToken);

            idsByTable[table.Name] = CollectIds(table, rows);
            storageKeys.AddRange(StorageKeysOf(table.Name, rows));

            tables.Add(new ClinicArchiveTableData(
                table.Name,
                JsonSerializer.Serialize(rows, ClinicArchiveFormat.Json),
                rows.Count));
        }

        return new ClinicArchiveExport(
            tables,
            storageKeys.Distinct(StringComparer.Ordinal).ToList(),
            _plan.Warnings);
    }

    public bool CanRestore(string table) => _plan.Tables.Any(t => t.Name == table);

    /// <summary>
    /// Releases the entries this restore staged and committed — never the whole change tracker.
    ///
    /// <para><c>ChangeTracker.Clear()</c> is what <see cref="IUnitOfWork.StopTracking"/> exists to avoid: it also
    /// drops whatever else the request is holding, including the caller's own <c>User</c> and anything a handler
    /// staged before calling in. Nothing is lost by it today, and « today » is not a property worth resting on for
    /// an operation whose failure mode is a silently discarded insert reported as a success.</para>
    /// </summary>
    public void ForgetRestoredRows()
    {
        foreach (var entry in _staged)
        {
            entry.State = EntityState.Detached;
        }

        _staged.Clear();
    }

    public async Task<ClinicArchiveTableOutcome> RestoreTableAsync(
        string table,
        Guid clinicId,
        string json,
        CancellationToken cancellationToken = default)
    {
        var plan = _plan.Tables.FirstOrDefault(t => t.Name == table);
        if (plan is null)
        {
            return ClinicArchiveTableOutcome.Empty;
        }

        var warnings = new List<string>();
        var key = SingleGuidKey(plan.EntityType);

        if (key is null)
        {
            // Named rather than counted as nothing: a table this restore cannot key is a table the practice does
            // not get back, and « 0 partout » reads identically to « il n'y en avait pas ».
            warnings.Add($"« {table} » n'a pas d'identifiant simple et n'a pas pu être restaurée.");
            return ClinicArchiveTableOutcome.Empty with { Warnings = warnings };
        }

        var rows = JsonSerializer.Deserialize<List<JsonObject>>(json, ClinicArchiveFormat.Json)
                   ?? new List<JsonObject>();

        // The cabinet's own live rows, selected by the SAME predicate the export uses — so « déjà présent » can
        // only ever mean « already present in THIS cabinet », and the read carries no cross-clinic reach to be
        // turned into a field-value oracle.
        var live = await ReadEntitiesAsync(plan, clinicId, ParentIdsFor(plan), cancellationToken);
        var liveById = IndexByKey(key, live);
        var cabinetIds = liveById.Keys.ToHashSet();
        var uniqueKeys = UniqueIndexKeys(plan, live);

        var byId = new Dictionary<Guid, JsonObject>();
        var unreadable = 0;

        foreach (var row in rows)
        {
            if (TryReadGuid(row, key.Name, out var id))
            {
                byId[id] = row;
            }
            else
            {
                unreadable++;
            }
        }

        if (unreadable > 0)
        {
            warnings.Add($"{unreadable} enregistrement(s) de « {table} » n'ont pas d'identifiant lisible et ont été ignorés.");
        }

        // One narrow probe across every cabinet, and it answers ONE bit: does this identifier already exist
        // somewhere? It has to — a primary key taken by another practice's row is a duplicate-key crash rather
        // than an insert — but it must never distinguish « identical » from « different », which is what turned
        // the old row-level comparison into a confirm-or-deny oracle over arbitrary columns.
        var takenElsewhere = await ReadIdsTakenAnywhereAsync(
            plan, key, byId.Keys.Where(id => !liveById.ContainsKey(id)), cancellationToken);

        var parentIds = plan.Scope == ClinicArchiveTableScope.Child
            ? _cabinetIds.GetValueOrDefault(plan.ParentTable!) ?? new HashSet<Guid>()
            : new HashSet<Guid>();

        var conflictingParents = plan.Scope == ClinicArchiveTableScope.Child
            ? _conflictingIds.GetValueOrDefault(plan.ParentTable!) ?? new HashSet<Guid>()
            : new HashSet<Guid>();

        var restored = 0;
        var alreadyPresent = 0;
        var conflicts = 0;
        var conflicting = new HashSet<Guid>();
        var storageKeys = new List<string>();
        var refusedForeignParent = 0;
        var refusedConflictingParent = 0;
        var refusedTakenId = 0;

        foreach (var (id, row) in byId)
        {
            // AC-1 — the cabinet's own record is matched on its primary key, so exactly one row is legitimate.
            // Without this a hand-edited `data/Clinic.json` inserts as many new practices as it lists: `Clinic`
            // has no `ClinicId` for StageInsert to re-stamp, its identity being the very column being trusted.
            if (plan.Scope == ClinicArchiveTableScope.Self && id != clinicId)
            {
                conflicts++;
                refusedForeignParent++;
                continue;
            }

            if (plan.Scope == ClinicArchiveTableScope.Child)
            {
                var parent = ParentOf(row, plan);

                // Cross-tenant insertion, and the only guard these twelve tables have: `Payment`, `InvoiceLine`,
                // `TreatmentPlanItem`, `DentalRecordAct` and the rest carry no `ClinicId` of their own, so the
                // foreign key in the file IS their clinic identity — an FK pointing at another practice's invoice
                // is valid, passes every query filter (which do not apply to inserts) and commits.
                if (parent is null || !parentIds.Contains(parent.Value))
                {
                    conflicts++;
                    refusedForeignParent++;
                    continue;
                }

                if (conflictingParents.Contains(parent.Value))
                {
                    // Its parent exists and DIFFERS from the archive, so re-inserting the child would hang the
                    // archive's lines off the live row — and the parent's stored totals are denormalised, so the
                    // note d'honoraires would permanently disagree with the sum of its own lines.
                    conflicts++;
                    conflicting.Add(id);
                    refusedConflictingParent++;
                    continue;
                }
            }

            if (liveById.TryGetValue(id, out var liveRow))
            {
                if (RowsMatch(plan.EntityType, row, liveRow))
                {
                    alreadyPresent++;
                }
                else
                {
                    conflicts++;
                    conflicting.Add(id);
                }

                continue;
            }

            if (takenElsewhere.Contains(id))
            {
                conflicts++;
                refusedTakenId++;
                continue;
            }

            var collision = CollidingIndex(uniqueKeys, row, clinicId);
            if (collision is not null)
            {
                // AC-3's « sans gap ni collision ». The scenario is the feature's own: rows are lost, the practice
                // keeps working, `IssueInvoiceCommand` re-mints the freed number off MAX+1, and putting the archive
                // back violates (ClinicId, Number). Named, never inserted — the alternative is an unhandled
                // DbUpdateException taking the whole restore down with no report at all.
                conflicts++;
                warnings.Add(
                    $"« {table} » : un enregistrement existe déjà sous un autre identifiant ({collision}). "
                    + "Il n'a pas été remis en place.");
                continue;
            }

            var entry = StageInsert(plan.EntityType, row, clinicId);
            _staged.Add(entry);

            cabinetIds.Add(id);
            RegisterUniqueKeys(uniqueKeys, row, clinicId);
            storageKeys.AddRange(StorageKeysOf(plan.Name, row));
            restored++;
        }

        _cabinetIds[table] = cabinetIds;
        _conflictingIds[table] = conflicting;

        AddRefusalWarning(warnings, table, refusedForeignParent,
            "n'appartiennent pas à ce cabinet et n'ont pas été remis en place");
        AddRefusalWarning(warnings, table, refusedConflictingParent,
            "dépendent d'un enregistrement modifié depuis l'archive et ont été ignorés");
        AddRefusalWarning(warnings, table, refusedTakenId,
            "portent un identifiant déjà utilisé ailleurs et n'ont pas été remis en place");

        return new ClinicArchiveTableOutcome(restored, alreadyPresent, conflicts, storageKeys, warnings);
    }

    private static void AddRefusalWarning(List<string> warnings, string table, int count, string reason)
    {
        if (count > 0)
        {
            warnings.Add($"« {table} » : {count} enregistrement(s) {reason}.");
        }
    }

    // ── Export ────────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<List<Dictionary<string, object?>>> ReadRowsAsync(
        ClinicArchiveTablePlan plan, Guid clinicId, HashSet<Guid>? parentIds, CancellationToken cancellationToken)
    {
        var entities = await ReadEntitiesAsync(plan, clinicId, parentIds, cancellationToken);
        var redacted = ClinicArchiveScope.Redacted.GetValueOrDefault(plan.Name);

        return entities.Select(entity => ReadRow(plan.EntityType, entity, redacted)).ToList();
    }

    private Task<List<object>> ReadEntitiesAsync(
        ClinicArchiveTablePlan plan, Guid clinicId, HashSet<Guid>? parentIds, CancellationToken cancellationToken)
    {
        var typed = typeof(ClinicArchiveStore)
            .GetMethod(nameof(ReadEntitiesTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(plan.EntityType.ClrType);

        return (Task<List<object>>)typed
            .Invoke(this, new object?[] { plan, clinicId, parentIds, cancellationToken })!;
    }

    private async Task<List<object>> ReadEntitiesTypedAsync<TEntity>(
        ClinicArchiveTablePlan plan, Guid clinicId, HashSet<Guid>? parentIds, CancellationToken cancellationToken)
        where TEntity : class
    {
        var entities = new List<object>();

        // Every branch states its clinic predicate EXPLICITLY rather than leaning on the ambient query filter:
        // the filter is a backstop, and this is the read whose miss would put another cabinet's records in a file
        // the practice keeps on a laptop (AC-1). `Clinic` and `User` carry no filter at all, so `Self` would be
        // unscoped without it.
        switch (plan.Scope)
        {
            case ClinicArchiveTableScope.Self:
            {
                var key = SingleGuidKey(plan.EntityType)!.Name;

                entities.AddRange(await _context.Set<TEntity>()
                    .AsNoTracking()
                    .Where(e => EF.Property<Guid>(e, key) == clinicId)
                    .ToListAsync(cancellationToken));
                break;
            }

            case ClinicArchiveTableScope.Direct:
                entities.AddRange(await _context.Set<TEntity>()
                    .AsNoTracking()
                    .Where(e => EF.Property<Guid>(e, ClinicArchiveScope.ClinicIdProperty) == clinicId)
                    .ToListAsync(cancellationToken));
                break;

            default:
            {
                var foreignKey = plan.ForeignKeyProperty!;

                foreach (var chunk in Chunk(parentIds ?? new HashSet<Guid>(), ParentIdChunk))
                {
                    entities.AddRange(await _context.Set<TEntity>()
                        .AsNoTracking()
                        .Where(e => chunk.Contains(EF.Property<Guid>(e, foreignKey)))
                        .ToListAsync(cancellationToken));
                }

                break;
            }
        }

        return entities;
    }

    private static Dictionary<string, object?> ReadRow(
        IEntityType entityType, object entity, IReadOnlySet<string>? redacted)
    {
        var row = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in ArchivedProperties(entityType))
        {
            row[property.Name] = redacted?.Contains(property.Name) == true
                ? null
                : ReadValue(property, entity);
        }

        foreach (var navigation in OwnedNavigations(entityType))
        {
            var owned = navigation.PropertyInfo?.GetValue(entity);

            row[navigation.Name] = owned is null
                ? null
                : ReadRow(navigation.TargetEntityType, owned, redacted: null);
        }

        return row;
    }

    private static object? ReadValue(IProperty property, object entity) =>
        property.PropertyInfo is { } info
            ? info.GetValue(entity)
            : property.FieldInfo?.GetValue(entity);

    private static HashSet<Guid> CollectIds(ClinicArchiveTablePlan plan, List<Dictionary<string, object?>> rows)
    {
        var key = SingleGuidKey(plan.EntityType);
        if (key is null)
        {
            return new HashSet<Guid>();
        }

        return rows
            .Select(row => row.GetValueOrDefault(key.Name))
            .OfType<Guid>()
            .ToHashSet();
    }

    // ── Storage keys ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The blob keys a table's rows point at — <b>one reader</b>, over both shapes a row is held in.
    ///
    /// <para>It was written twice, once per shape, each with its own null handling and only one of them
    /// de-duplicating; the export and the restore therefore had two answers to « which string is a storage key ».
    /// Both entry points fold into this, which is also what lets the restore hand back the keys of the rows it
    /// actually inserted rather than re-parsing the file and returning every key it names.</para>
    /// </summary>
    private static IEnumerable<string> StorageKeysOf<TRow>(
        string table, IEnumerable<TRow> rows, Func<TRow, string, string?> read)
    {
        if (!ClinicArchiveScope.BlobProperties.TryGetValue(table, out var property))
        {
            return Array.Empty<string>();
        }

        return rows
            .Select(row => read(row, property))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> StorageKeysOf(string table, List<Dictionary<string, object?>> rows) =>
        StorageKeysOf(table, rows, (row, name) => row.GetValueOrDefault(name) as string);

    private static IEnumerable<string> StorageKeysOf(string table, JsonObject row) =>
        StorageKeysOf(table, new[] { row },
            (json, name) => json.TryGetPropertyValue(name, out var value) ? value?.GetValue<string>() : null);

    // ── Restore ───────────────────────────────────────────────────────────────────────────────────────────────

    private HashSet<Guid>? ParentIdsFor(ClinicArchiveTablePlan plan) =>
        plan.Scope == ClinicArchiveTableScope.Child
            ? _cabinetIds.GetValueOrDefault(plan.ParentTable!) ?? new HashSet<Guid>()
            : null;

    private static Dictionary<Guid, object> IndexByKey(IProperty key, List<object> entities)
    {
        var found = new Dictionary<Guid, object>();

        foreach (var entity in entities)
        {
            if (ReadValue(key, entity) is Guid id)
            {
                found[id] = entity;
            }
        }

        return found;
    }

    private static Guid? ParentOf(JsonObject row, ClinicArchiveTablePlan plan) =>
        TryReadGuid(row, plan.ForeignKeyProperty!, out var parent) ? parent : null;

    private Task<HashSet<Guid>> ReadIdsTakenAnywhereAsync(
        ClinicArchiveTablePlan plan, IProperty key, IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var typed = typeof(ClinicArchiveStore)
            .GetMethod(nameof(ReadIdsTakenAnywhereTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(plan.EntityType.ClrType);

        return (Task<HashSet<Guid>>)typed
            .Invoke(this, new object?[] { key.Name, ids.ToHashSet(), cancellationToken })!;
    }

    private async Task<HashSet<Guid>> ReadIdsTakenAnywhereTypedAsync<TEntity>(
        string keyName, HashSet<Guid> ids, CancellationToken cancellationToken)
        where TEntity : class
    {
        var taken = new HashSet<Guid>();

        foreach (var chunk in Chunk(ids, ParentIdChunk))
        {
            // IgnoreQueryFilters, and it is required rather than lax: on the console path the cabinet is being
            // re-created with no clinic in scope, and a primary key already held by ANOTHER practice's row must
            // be refused rather than met as a duplicate-key crash half way through the restore. It projects the
            // id alone, so nothing about the other cabinet's row can be read off the answer.
            taken.UnionWith(await _context.Set<TEntity>()
                .AsNoTracking()
                .IgnoreQueryFilters()
                .Where(e => chunk.Contains(EF.Property<Guid>(e, keyName)))
                .Select(e => EF.Property<Guid>(e, keyName))
                .ToListAsync(cancellationToken));
        }

        return taken;
    }

    /// <summary>
    /// Whether the archived row and the live one say the same thing, over the archived scalar columns only.
    ///
    /// <para>Compared on their <b>serialized</b> form rather than value by value, so a <c>DateTime</c> and a
    /// <c>decimal</c> are compared exactly as they were written — which is the only comparison that cannot report
    /// a difference the archive could not have recorded, and so raise a phantom conflict on a row nobody
    /// touched.</para>
    ///
    /// <para>⚠️ <b>The live row is read through the same redaction the archive was written with</b>, and that is
    /// not symmetry for its own sake: the cabinet's Google refresh token is written as <c>null</c>, so comparing
    /// it against the live value reported the <c>Clinic</c> row as a conflict on every restore of every
    /// Google-connected practice — a disagreement the owner could never resolve, because the archive is
    /// structurally incapable of carrying that value.</para>
    /// </summary>
    private static bool RowsMatch(IEntityType entityType, JsonObject archived, object live)
    {
        var redacted = ClinicArchiveScope.Redacted.GetValueOrDefault(entityType.ClrType.Name);
        var liveRow = ReadRow(entityType, live, redacted);
        var liveJson = JsonSerializer.SerializeToNode(liveRow, ClinicArchiveFormat.Json) as JsonObject;

        if (liveJson is null)
        {
            return false;
        }

        foreach (var (name, archivedValue) in archived)
        {
            if (!liveJson.TryGetPropertyValue(name, out var liveValue))
            {
                continue;
            }

            if (!JsonNode.DeepEquals(archivedValue, liveValue))
            {
                return false;
            }
        }

        return true;
    }

    // ── Unique indexes ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The unique indexes a restored row could collide on, keyed by index, holding the live rows' values.
    ///
    /// <para>Derived from the model rather than from a list of the indexes anyone remembered — the document-number
    /// sequences are the case AC-3 names, but <c>ProcedureTypes(ClinicId, Name)</c> and
    /// <c>DentalRecordTeeth(DentalRecordId, ToothNumber)</c> fail exactly the same way. The primary key is
    /// excluded: presence by id is the restore's own question and is answered above.</para>
    /// </summary>
    private static Dictionary<IIndex, HashSet<string>> UniqueIndexKeys(
        ClinicArchiveTablePlan plan, List<object> live)
    {
        var indexes = plan.EntityType.GetIndexes().Where(i => i.IsUnique).ToList();
        var keys = indexes.ToDictionary(i => i, _ => new HashSet<string>(StringComparer.Ordinal));

        if (indexes.Count == 0)
        {
            return keys;
        }

        foreach (var entity in live)
        {
            foreach (var index in indexes)
            {
                var composed = ComposeIndexKey(index, property => ReadValue(property, entity));
                if (composed is not null)
                {
                    keys[index].Add(composed);
                }
            }
        }

        return keys;
    }

    private static string? CollidingIndex(
        Dictionary<IIndex, HashSet<string>> keys, JsonObject row, Guid clinicId)
    {
        foreach (var (index, seen) in keys)
        {
            var composed = ComposeIndexKey(index, property => ArchivedValue(row, property, clinicId));

            if (composed is not null && seen.Contains(composed))
            {
                return string.Join(", ", index.Properties.Select(p => p.Name));
            }
        }

        return null;
    }

    private static void RegisterUniqueKeys(
        Dictionary<IIndex, HashSet<string>> keys, JsonObject row, Guid clinicId)
    {
        foreach (var (index, seen) in keys)
        {
            var composed = ComposeIndexKey(index, property => ArchivedValue(row, property, clinicId));
            if (composed is not null)
            {
                seen.Add(composed);
            }
        }
    }

    /// <summary>
    /// One index's values as a comparable string, or null when any of them is null — PostgreSQL's unique indexes
    /// do not constrain nulls, so a row with one is not a collision candidate at all.
    /// </summary>
    private static string? ComposeIndexKey(IIndex index, Func<IProperty, object?> read)
    {
        var parts = new List<string>(index.Properties.Count);

        foreach (var property in index.Properties)
        {
            var value = read(property);
            if (value is null)
            {
                return null;
            }

            parts.Add(JsonSerializer.Serialize(value, ClinicArchiveFormat.Json));
        }

        // A separator no serialized value can contain, so « AB »+« C » and « A »+« BC » stay distinct keys.
        return string.Join('', parts);
    }

    /// <summary>An archived row's value for one property, with the clinic re-stamped exactly as the insert will.</summary>
    private static object? ArchivedValue(JsonObject row, IProperty property, Guid clinicId)
    {
        if (property.Name == ClinicArchiveScope.ClinicIdProperty && property.ClrType == typeof(Guid))
        {
            return clinicId;
        }

        return row.TryGetPropertyValue(property.Name, out var value)
            ? Convert(value, property.ClrType)
            : null;
    }

    // ── Materialisation ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Materialises one archived row straight onto the model's properties and stages it as an insert.
    ///
    /// <para>The instance is created <b>uninitialised</b>: every entity here has a private parameterless
    /// constructor for EF, but the ones that do not would still have to be reachable, and no constructor may run
    /// anyway — see the class remarks on ids and timestamps.</para>
    ///
    /// <para>The clinic id is re-stamped from <paramref name="clinicId"/> rather than trusted from the file. On
    /// the cabinet path the two are already equal (the manifest was checked), and on the console path the cabinet
    /// is created <i>at</i> the archive's own id — so this is belt and braces at the one place a mismatch would be
    /// unrecoverable rather than refused. ⚠️ It fires only on a <c>Guid ClinicId</c> property, which twelve
    /// archived tables do not have; their clinic identity is their parent's, checked by the caller before a row
    /// reaches here.</para>
    ///
    /// <para>⚠️ <b>Writing the value is not enough on its own, and the other half is in the model.</b> EF leaves a
    /// store-generated column out of the INSERT when the value equals the property's sentinel, so an archived
    /// <c>false</c> on a column defaulting to <c>true</c> reached the database as <c>true</c> — a deactivated acte
    /// restored active, a clinic's VAT switched back on. <c>ApplicationDbContext</c> aligns every sentinel with
    /// the default the column carries, which makes « supplied the same value the database would » distinct from
    /// « supplied nothing » for every write in the product, not only for this one.</para>
    /// </summary>
    private EntityEntry StageInsert(IEntityType entityType, JsonObject row, Guid clinicId)
    {
        var instance = Materialize(entityType.ClrType);
        var entry = _context.Entry(instance);

        foreach (var property in ArchivedProperties(entityType))
        {
            if (property.Name == ClinicArchiveScope.ClinicIdProperty && property.ClrType == typeof(Guid))
            {
                entry.Property(property.Name).CurrentValue = clinicId;
                continue;
            }

            if (row.TryGetPropertyValue(property.Name, out var value))
            {
                entry.Property(property.Name).CurrentValue = Convert(value, property.ClrType);
            }
        }

        foreach (var navigation in OwnedNavigations(entityType))
        {
            if (!row.TryGetPropertyValue(navigation.Name, out var value) || value is not JsonObject ownedRow)
            {
                continue;
            }

            var owned = Materialize(navigation.TargetEntityType.ClrType);

            foreach (var property in ArchivedProperties(navigation.TargetEntityType))
            {
                if (ownedRow.TryGetPropertyValue(property.Name, out var ownedValue))
                {
                    SetDirect(property, owned, Convert(ownedValue, property.ClrType));
                }
            }

            navigation.PropertyInfo?.SetValue(instance, owned);
        }

        entry.State = EntityState.Added;

        return entry;
    }

    /// <summary>
    /// Builds an empty instance to write a restored row onto.
    ///
    /// <para><b>The private parameterless constructor first, and that is load-bearing rather than tidy.</b> Every
    /// entity here has one for EF, and it runs the <b>field initialisers</b> — including the
    /// <c>private readonly List&lt;T&gt; _lines = new()</c> backing every collection navigation. An instance built
    /// with <see cref="RuntimeHelpers.GetUninitializedObject"/> leaves those null, and EF's own fix-up walks them
    /// when the entry is marked <c>Added</c>: the failure is a null reference from inside the change tracker, on a
    /// restore, with the row's data nowhere in the message.</para>
    ///
    /// <para>The uninitialised fall-back covers a type with no accessible parameterless constructor at all — the
    /// value objects, which have no collections to leave null.</para>
    ///
    /// <para>⚠️ Neither path runs a <i>domain</i> constructor, which is the point: those mint a fresh id and stamp
    /// <c>DateTime.UtcNow</c>. See the class remarks.</para>
    /// </summary>
    private static object Materialize(Type clrType)
    {
        try
        {
            return Activator.CreateInstance(clrType, nonPublic: true)
                   ?? RuntimeHelpers.GetUninitializedObject(clrType);
        }
        catch (MissingMethodException)
        {
            return RuntimeHelpers.GetUninitializedObject(clrType);
        }
    }

    private static void SetDirect(IProperty property, object target, object? value)
    {
        if (property.PropertyInfo is { } info && info.CanWrite)
        {
            info.SetValue(target, value);
            return;
        }

        property.FieldInfo?.SetValue(target, value);
    }

    // ── Shared model helpers ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The scalar columns an archive carries: everything mapped, minus the concurrency token, minus anything the
    /// <b>database itself</b> computes or maintains.
    ///
    /// <para>⚠️ <c>OnAdd</c> is kept, and that is the correction rather than an oversight — see the class remarks.
    /// EF marks a column with a <c>HasDefaultValue</c> and a <c>Guid</c> key with no <c>ValueGeneratedNever()</c>
    /// as <c>OnAdd</c>, so excluding it dropped money flags, billing settings and three tables' primary keys.
    /// <c>OnAddOrUpdate</c> and <c>OnUpdate</c> are the genuinely store-owned ones, and a computed column is the
    /// database's own expression: writing either back would assert something about another database.</para>
    /// </summary>
    private static IEnumerable<IProperty> ArchivedProperties(IEntityType entityType) =>
        entityType.GetProperties().Where(p =>
            p.Name != nameof(Domain.Common.Entity<int>.Version)
            && p.ValueGenerated is ValueGenerated.Never or ValueGenerated.OnAdd
            && p.GetComputedColumnSql() is null
            && !p.IsShadowProperty());

    /// <summary>
    /// The value objects table-split into the same row (<c>Patient.Email</c>, both <c>PhoneNumber</c>s). They have
    /// no row of their own, so they travel nested inside their owner's rather than as a table.
    /// </summary>
    private static IEnumerable<INavigation> OwnedNavigations(IEntityType entityType) =>
        entityType.GetNavigations().Where(n => n.ForeignKey.IsOwnership && !n.IsCollection);

    /// <summary>The single GUID primary key, or null for the composite and non-GUID keys this restore skips.</summary>
    private static IProperty? SingleGuidKey(IEntityType entityType)
    {
        var key = entityType.FindPrimaryKey();

        return key is { Properties.Count: 1 } && key.Properties[0].ClrType == typeof(Guid)
            ? key.Properties[0]
            : null;
    }

    private static bool TryReadGuid(JsonObject row, string name, out Guid value)
    {
        value = Guid.Empty;

        return row.TryGetPropertyValue(name, out var node)
               && node is not null
               && Guid.TryParse(node.GetValue<string>(), out value);
    }

    private static object? Convert(JsonNode? value, Type target) =>
        value is null ? null : JsonSerializer.Deserialize(value.ToJsonString(), target, ClinicArchiveFormat.Json);

    private static IEnumerable<List<Guid>> Chunk(IEnumerable<Guid> ids, int size)
    {
        var batch = new List<Guid>(size);

        foreach (var id in ids)
        {
            batch.Add(id);

            if (batch.Count == size)
            {
                yield return batch;
                batch = new List<Guid>(size);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }
}
