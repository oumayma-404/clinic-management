using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ClinicManagement.Infrastructure.Persistence;

/// <summary>
/// Empties one cabinet out of the database, table by table, in the order the foreign keys allow
/// (<c>clinic-account-removal</c>).
///
/// <para><b>⚠️ The plan is DERIVED from the EF model, never written out.</b> A hand-kept list of sixty DELETE
/// statements is this repository's dominant defect shape with the highest possible stake: the table somebody
/// forgets to add is the table whose patient rows survive a deletion the vendor was told had succeeded. So the
/// set comes from <c>Model.GetEntityTypes()</c>, the predicate from each type's own <c>ClinicId</c> column or
/// from its foreign-key path to one, and the order from <c>GetReferencingForeignKeys()</c> — which means a table
/// added next month is covered the day it is configured, with nobody remembering this file exists.
/// <c>ClinicPurgePlanTests</c> holds the classification in both directions.</para>
///
/// <para><b>⚠️ Raw SQL rather than EF deletes, and the difference is not performance.</b> Loading a cabinet's
/// aggregates to remove them would go through the tenant query filter (which the console does not satisfy), pull
/// every patient record into memory, and depend on each navigation being configured to cascade — three ways to
/// delete less than was asked for. A <c>DELETE … WHERE "ClinicId" = @clinicId</c> per table is exact and says
/// exactly what it did in the log.</para>
///
/// <para><b>⚠️ It verifies itself and throws.</b> After the pass, every covered table is counted again; one
/// surviving row aborts the caller's transaction. A half-emptied cabinet is the worst available outcome — it reads
/// as deleted, nobody can sign into it, and it still holds clinical rows — so it must not be reachable even
/// through a bug in this file.</para>
///
/// <para>⚠️ <b>Blobs are not its business.</b> <c>IFileStorage.DeleteByClinicAsync</c> sweeps the object store
/// after the rows are committed: an object store cannot be rolled back, so removing bytes inside a transaction
/// that might abort is how a cabinet keeps its rows and loses its radiographs.</para>
/// </summary>
public class ClinicPurge : IClinicPurge
{
    /// <summary>
    /// Rows that <b>survive</b> a cabinet's deletion. Each entry is a decision, not an omission — and the guard in
    /// <c>ClinicPurgePlanTests</c> fails if this stops matching the model exactly: a new table that belongs to
    /// nobody fails, and so does an exemption for a table that has since grown a clinic of its own.
    /// </summary>
    public static readonly Dictionary<string, string> SurvivesByDesign = new(StringComparer.Ordinal)
    {
        // The reason `PlatformAccessEntryConfiguration` gives for having no foreign key to `Clinics`: this is a
        // record of what the VENDOR did, and "who deleted the practice that is no longer here?" is exactly the
        // row an audit of this console would be looking for. It carries `ClinicName` and `AccountEmail`
        // denormalised so such a row can still name both parties.
        [nameof(PlatformAccessEntry)] =
            "the vendor's own ledger: a deleted cabinet's rows are the ones an audit needs most",

        // A console account and its recovery codes belong to the vendor, not to any cabinet.
        [nameof(PlatformAccount)] = "a console account, which no cabinet owns",
        [nameof(PlatformRecoveryCode)] = "belongs to a console account, not to a cabinet",

        // The Data Protection key ring. It is the INSTALL's, and every cabinet's encrypted reminder secrets are
        // protected with it, so deleting one practice must not take the keys that decrypt the others'.
        ["DataProtectionKey"] = "the install's Data Protection key ring, shared by every cabinet",
    };

    /// <summary>
    /// The column that identifies a cabinet's rows in the two tables no clinic id can reach — a pending signup and
    /// a password-reset request. Both hold the practice's name, an administrator's address and a hash of their
    /// password; neither carries a <c>ClinicId</c> (there is no clinic yet when a signup exists) nor a foreign key
    /// to <c>Users</c>, so no path through the model finds them.
    ///
    /// <para><b>Derived, not listed.</b> The rule is "clinic-less, not exempted, and carrying an <c>Email</c>
    /// column", so a third table of that shape is swept the day it is configured — which is exactly the kind of
    /// table somebody adds without thinking about deletion. The vendor's own tables are filtered out first, which
    /// is what keeps <c>PlatformAccount</c>'s address out of it.</para>
    /// </summary>
    private const string AddressColumn = "Email";

    private readonly ApplicationDbContext _context;
    private readonly ILogger<ClinicPurge> _logger;

    public ClinicPurge(ApplicationDbContext context, ILogger<ClinicPurge> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ClinicPurgeCensus> CountAsync(
        Guid clinicId, IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default)
    {
        var normalised = Normalise(addresses);
        var steps = Applicable(BuildPlan(_context.Model), normalised);
        var tallies = new List<ClinicPurgeTally>();

        foreach (var step in steps)
        {
            var rows = await CountAsync(step, clinicId, normalised, cancellationToken);
            if (rows > 0)
            {
                tallies.Add(new ClinicPurgeTally(step.Entity, step.Table, rows));
            }
        }

        return new ClinicPurgeCensus(tallies, await FileBytesAsync(clinicId, cancellationToken));
    }

    public async Task<ClinicPurgeCensus> PurgeAsync(
        Guid clinicId, IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default)
    {
        var normalised = Normalise(addresses);
        var steps = Applicable(BuildPlan(_context.Model), normalised);
        var tallies = new List<ClinicPurgeTally>();
        var fileBytes = await FileBytesAsync(clinicId, cancellationToken);

        foreach (var step in steps)
        {
            // Counted rather than taking the delete's own row count, because a cascade removes rows this step
            // never named: the figure the vendor is shown must be « what this table held », which is only true
            // before the pass reaches the tables below it.
            var rows = await CountAsync(step, clinicId, normalised, cancellationToken);

            if (rows == 0)
            {
                continue;
            }

            var parameters = Parameters(clinicId, normalised);

            var deleted = await _context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {step.Table} WHERE {step.Where}",
                parameters,
                cancellationToken);

            tallies.Add(new ClinicPurgeTally(step.Entity, step.Table, rows));

            _logger.LogInformation(
                "Cleared {Deleted} rows from {Table} for clinic {ClinicId} (counted {Counted})",
                deleted, step.Table, clinicId, rows);
        }

        await VerifyEmptyAsync(steps, clinicId, normalised, cancellationToken);

        return new ClinicPurgeCensus(tallies, fileBytes);
    }

    /// <summary>
    /// Counts every covered table again once the pass is over. It is the one check that can catch a plan which
    /// deleted less than it claimed, and the exception it throws is what turns that into a rollback.
    ///
    /// <para>⚠️ Honest about its reach: a table reached through a <b>subquery</b> on its parent answers zero as soon
    /// as the parent is gone, so this is a real assertion for every <c>ClinicId</c>-bearing table and a structural
    /// one for the children — which are deleted first, and whose foreign keys are what keep them from surviving
    /// their parents.</para>
    /// </summary>
    private async Task VerifyEmptyAsync(
        IReadOnlyList<PurgeStep> steps, Guid clinicId, string[] addresses, CancellationToken cancellationToken)
    {
        var survivors = new List<string>();

        foreach (var step in steps)
        {
            var remaining = await CountAsync(step, clinicId, addresses, cancellationToken);
            if (remaining > 0)
            {
                survivors.Add($"{step.Table} ({remaining})");
            }
        }

        if (survivors.Count > 0)
        {
            throw new InvalidOperationException(
                $"La suppression du cabinet {clinicId} a laissé des lignes derrière elle : "
                + string.Join(", ", survivors)
                + ". Rien n'a été supprimé.");
        }
    }

    private async Task<long> CountAsync(
        PurgeStep step, Guid clinicId, string[] addresses, CancellationToken cancellationToken)
    {
        // `AS "Value"` is not decoration: EF's scalar SqlQueryRaw reads a column of that name and nothing else.
        var sql = $"SELECT COUNT(*) AS \"Value\" FROM {step.Table} WHERE {step.Where}";

        return await _context.Database
            .SqlQueryRaw<long>(sql, Parameters(clinicId, addresses))
            .SingleAsync(cancellationToken);
    }

    /// <summary>
    /// The steps that can run at all. An address-keyed step with no addresses is <b>dropped</b> rather than run
    /// with an empty list: it would delete nothing, and counting it would have the preview state a table the
    /// deletion cannot touch.
    /// </summary>
    private static IReadOnlyList<PurgeStep> Applicable(IReadOnlyList<PurgeStep> plan, string[] addresses) =>
        addresses.Length > 0 ? plan : plan.Where(s => s.Key == PurgeKey.Clinic).ToList();

    /// <summary>
    /// The stored spelling of an address — <c>EmailNormalization</c>'s, which both tables write through. Comparing
    /// a caller's own spelling would match nothing and report a clean sweep.
    /// </summary>
    private static string[] Normalise(IReadOnlyCollection<string> addresses) =>
        addresses
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(EmailNormalization.Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// How many bytes of files the cabinet holds, as the rows claim. Read before the pass because the rows are what
    /// carry it, and stated in the preview beside the file count — « 6 fichiers » says nothing about whether the
    /// deletion is about to drop a scanner's whole output.
    ///
    /// <para>⚠️ <c>PatientFile.FileSize</c> is the row's figure and, for anything written before upload validation
    /// existed, that is the client's claim rather than the object store's. Close enough for a sentence a human reads
    /// before pressing a button; it is never used as a length on the wire.</para>
    /// </summary>
    private async Task<long> FileBytesAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        // Read off the model like everything else here: a renamed column would otherwise take the preview's size
        // figure down while the deletion itself carried on working, which is the quietest possible way for a
        // sentence a human reads before a destructive click to become wrong.
        var files = _context.Model.FindEntityType(typeof(PatientFile));
        var size = files?.FindProperty(nameof(PatientFile.FileSize));
        var clinicColumn = files?.FindProperty("ClinicId");

        if (files is null || size is null || clinicColumn is null)
        {
            return 0;
        }

        var sql = $"SELECT SUM({QuoteColumn(ColumnOf(size, files)!)}) AS \"Value\" FROM {Quote(files)} "
                  + $"WHERE {QuoteColumn(ColumnOf(clinicColumn, files)!)} = @clinicId";

        var bytes = await _context.Database
            .SqlQueryRaw<long?>(sql, Parameters(clinicId, Array.Empty<string>()))
            .SingleAsync(cancellationToken);

        return bytes ?? 0;
    }

    /// <summary>
    /// Both parameters, always, whatever the step names: Npgsql binds by name and ignores what a statement does
    /// not mention, so one parameter set for every step is one thing that cannot be mismatched.
    /// </summary>
    private static object[] Parameters(Guid clinicId, string[] addresses) => new object[]
    {
        new NpgsqlParameter("clinicId", clinicId),
        new NpgsqlParameter("emails", addresses),
    };

    /// <summary>What identifies a cabinet's rows in one table.</summary>
    public enum PurgeKey
    {
        /// <summary>The clinic's id, directly or through a foreign-key path to it.</summary>
        Clinic = 0,

        /// <summary>The addresses of its accounts — the two tables no clinic id can reach.</summary>
        Address = 1,
    }

    /// <summary>One table's delete, and the predicate that identifies the cabinet's rows in it.</summary>
    public sealed record PurgeStep(string Entity, string Table, string Where, PurgeKey Key);

    /// <summary>
    /// The whole plan, dependents before principals, with the cabinet's own row last.
    ///
    /// <para>Public rather than private so the guard test asserts the classification off the same model the
    /// runtime uses — a test that rebuilt this logic would be asserting its own copy — and so an operator can be
    /// shown exactly what a deletion will touch.</para>
    /// </summary>
    public static IReadOnlyList<PurgeStep> BuildPlan(IModel model)
    {
        var clinic = model.FindEntityType(typeof(Clinic))
            ?? throw new InvalidOperationException("The model has no Clinic entity type.");

        var candidates = model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .Where(e => e.GetTableName() is not null)
            .Where(e => !SurvivesByDesign.ContainsKey(e.ClrType.Name))
            .ToList();

        // Resolved first for every candidate, because the order pass below must know which tables are in the plan
        // before it can decide what has to precede what.
        var predicates = new Dictionary<IEntityType, string>();

        foreach (var entity in candidates)
        {
            var where = ResolvePredicate(entity, clinic, new HashSet<IEntityType>());
            if (where is not null)
            {
                predicates[entity] = where;
            }
        }

        var ordered = new List<IEntityType>();
        var settled = new HashSet<IEntityType>();
        var walking = new HashSet<IEntityType>();

        void Visit(IEntityType entity)
        {
            if (settled.Contains(entity) || !walking.Add(entity))
            {
                // Already placed, or a cycle — a cycle means one of its foreign keys is optional, and the row that
                // carries it is deleted by the other end's own step.
                return;
            }

            foreach (var referencing in entity.GetReferencingForeignKeys())
            {
                var dependent = referencing.DeclaringEntityType;

                if (dependent != entity && predicates.ContainsKey(dependent))
                {
                    Visit(dependent);
                }
            }

            walking.Remove(entity);
            settled.Add(entity);
            ordered.Add(entity);
        }

        // The clinic's own row is appended by hand at the end rather than being walked: everything else references
        // it, so it is last by construction — and saying so here means a missing reference cannot reorder it.
        foreach (var entity in predicates.Keys.Where(e => e != clinic))
        {
            Visit(entity);
        }

        // The address-keyed tables lead, and their position is free: they reference nothing in the clinic's graph
        // and nothing references them, so no foreign key constrains where they go.
        var steps = candidates
            .Where(e => !predicates.ContainsKey(e))
            .Where(e => e.FindProperty(AddressColumn) is not null)
            .OrderBy(e => e.ClrType.Name, StringComparer.Ordinal)
            .Select(e => new PurgeStep(
                e.ClrType.Name,
                Quote(e),
                $"{QuoteColumn(ColumnOf(e.FindProperty(AddressColumn)!, e)!)} = ANY(@emails)",
                PurgeKey.Address))
            .ToList();

        steps.AddRange(ordered
            .Where(e => e != clinic)
            .Select(e => new PurgeStep(e.ClrType.Name, Quote(e), predicates[e], PurgeKey.Clinic)));

        steps.Add(new PurgeStep(nameof(Clinic), Quote(clinic), predicates[clinic], PurgeKey.Clinic));

        return steps;
    }

    /// <summary>
    /// How this table's rows are identified as one cabinet's: its own <c>ClinicId</c>, the clinic's primary key when
    /// it <i>is</i> the clinic, or a subquery through the single-column foreign key that leads to one.
    ///
    /// <para>⚠️ <b>Returning null means « this table belongs to no cabinet »</b>, which is a classification and not a
    /// failure — the guard test is what refuses it, so a platform table has to be named in
    /// <see cref="SurvivesByDesign"/> with a reason rather than being skipped in silence here.</para>
    /// </summary>
    private static string? ResolvePredicate(IEntityType entity, IEntityType clinic, HashSet<IEntityType> seen)
    {
        if (entity == clinic)
        {
            var key = PrimaryKeyProperty(clinic);
            return $"{QuoteColumn(ColumnOf(key, clinic)!)} = @clinicId";
        }

        if (!seen.Add(entity))
        {
            return null;
        }

        var own = entity.FindProperty("ClinicId");
        if (own is not null && ColumnOf(own, entity) is { } column)
        {
            return $"{QuoteColumn(column)} = @clinicId";
        }

        foreach (var fk in entity.GetForeignKeys())
        {
            // Composite foreign keys are deliberately not followed: there is none on this model's clinic paths, and
            // a silently half-built `IN` clause is worse than the guard test's refusal.
            if (fk.Properties.Count != 1 || fk.PrincipalKey.Properties.Count != 1)
            {
                continue;
            }

            var principal = fk.PrincipalEntityType;
            if (principal == entity || SurvivesByDesign.ContainsKey(principal.ClrType.Name))
            {
                continue;
            }

            var principalWhere = ResolvePredicate(principal, clinic, seen);
            if (principalWhere is null)
            {
                continue;
            }

            var dependentColumn = ColumnOf(fk.Properties[0], entity);
            var principalColumn = ColumnOf(fk.PrincipalKey.Properties[0], principal);

            if (dependentColumn is null || principalColumn is null)
            {
                continue;
            }

            return $"{QuoteColumn(dependentColumn)} IN (SELECT {QuoteColumn(principalColumn)} "
                   + $"FROM {Quote(principal)} WHERE {principalWhere})";
        }

        return null;
    }

    private static IProperty PrimaryKeyProperty(IEntityType entity) =>
        entity.FindPrimaryKey()?.Properties[0]
        ?? throw new InvalidOperationException($"{entity.ClrType.Name} has no primary key.");

    private static string? ColumnOf(IProperty property, IEntityType entity)
    {
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        return property.GetColumnName(table);
    }

    private static string Quote(IEntityType entity)
    {
        var schema = entity.GetSchema();
        var table = $"\"{entity.GetTableName()}\"";
        return string.IsNullOrEmpty(schema) ? table : $"\"{schema}\".{table}";
    }

    private static string QuoteColumn(string column) => $"\"{column}\"";
}
