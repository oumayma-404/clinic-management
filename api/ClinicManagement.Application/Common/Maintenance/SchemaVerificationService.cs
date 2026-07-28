using ClinicManagement.Application.Common.Interfaces;

namespace ClinicManagement.Application.Common.Maintenance;

/// <summary>How much attention a schema finding needs.</summary>
public enum SchemaVerificationSeverity
{
    /// <summary>A recorded fact, for the before/after diff. Not a problem.</summary>
    Info,

    /// <summary>The database does not match the model, or a shape requirement is violated.</summary>
    Drift
}

/// <summary>One line of the schema report.</summary>
public sealed record SchemaVerificationFinding(
    string Scope,
    string Check,
    string Detail,
    SchemaVerificationSeverity Severity);

/// <summary>The full schema-verification result.</summary>
public sealed record SchemaVerificationReport(IReadOnlyList<SchemaVerificationFinding> Findings)
{
    /// <summary>True when at least one check failed. Drives the console verb's exit code.</summary>
    public bool HasDrift => Findings.Any(f => f.Severity == SchemaVerificationSeverity.Drift);

    public int DriftCount => Findings.Count(f => f.Severity == SchemaVerificationSeverity.Drift);
}

/// <summary>
/// Asserts that the schema the EF model describes is <b>actually in the database</b>, and that each data
/// migration in this feature finished its job. Read-only.
///
/// <para><b>Why this exists.</b> Nothing in the test project touches a database — the whole suite is Moq-based —
/// so a migration is the one class of change unit tests structurally cannot verify. An index can be missing, an
/// exclusion constraint can be non-partial, a backfill can cover zero rows, a model change can have no applied
/// migration at all, and every test still passes. This is the gate for that class of change: run it before a
/// migration batch, keep the output, run it after, and diff.</para>
///
/// <para><b>Model-driven, not a hand-maintained list.</b> The expected indexes, foreign keys and decimal
/// precisions come from the EF model itself, so a schema object added in a configuration file is verified for
/// free. A hardcoded expectation list is precisely the failure this feature's plan flags three times over
/// (R-9/R-13/R-14: a "contract" test that silently never fails on a new area) and it would rot the same way.
/// Only the things the model cannot express are named here: the <c>btree_gist</c> extension, the exclusion
/// constraint's partiality, the two rate columns, and the data-migration row counts.</para>
///
/// <para>Deliberately <b>not</b> DI-registered — like <see cref="AdminPasswordRecoveryService"/> and
/// <see cref="MoneyReconciliationService"/> it is driven only by the API's <c>verify-schema</c> console verb, so
/// there is no HTTP-reachable path to a cross-clinic catalog read. It never mutates anything.</para>
/// </summary>
public class SchemaVerificationService
{
    /// <summary>
    /// The two rate columns that legitimately keep their own precision. They are rates, not money: a convention
    /// that silently widened a VAT rate would be worse than the drift it fixes (AC-P4.38). Listed here because
    /// "this column is deliberately different" is a decision, not something the model can state.
    /// </summary>
    private static readonly (string Table, string Column)[] RatePrecisionExceptions =
    {
        ("Clinics", "VatRate"),
        ("Invoices", "VatRate"),
    };

    private const int MoneyPrecision = 18;
    private const int MoneyScale = 3;

    /// <summary>Extensions the schema depends on, with what breaks without each.</summary>
    private static readonly (string Name, string Reason)[] RequiredExtensions =
    {
        ("btree_gist", "the appointment exclusion constraint mixes = (uuid) with && (range) in one GiST index"),
    };

    private readonly ISchemaVerificationReader _reader;

    public SchemaVerificationService(ISchemaVerificationReader reader)
    {
        _reader = reader;
    }

    public async Task<SchemaVerificationReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var facts = await _reader.ReadAsync(cancellationToken);
        var findings = new List<SchemaVerificationFinding>();

        VerifyExtensions(facts, findings);
        VerifyBookingConstraint(facts, findings);
        VerifyIndexes(facts, findings);
        VerifyForeignKeys(facts, findings);
        VerifyDecimalPrecision(facts, findings);
        VerifyDataMigrations(facts, findings);

        return new SchemaVerificationReport(findings);
    }

    // ------------------------------------------------------------------ extensions

    private static void VerifyExtensions(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        foreach (var (name, reason) in RequiredExtensions)
        {
            var present = facts.InstalledExtensions.Contains(name, StringComparer.OrdinalIgnoreCase);
            findings.Add(new SchemaVerificationFinding(
                "Extensions",
                name,
                present ? "installed" : $"MISSING — {reason}",
                present ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    // ------------------------------------------------------------------ the booking constraint

    private static void VerifyBookingConstraint(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        // 'x' is PostgreSQL's contype for an EXCLUDE constraint. EF cannot express one, so it is named here.
        var constraint = facts.Constraints.FirstOrDefault(c =>
            string.Equals(c.Table, "Appointments", StringComparison.OrdinalIgnoreCase) && c.Kind == 'x');

        if (constraint == null)
        {
            findings.Add(new SchemaVerificationFinding(
                "Booking integrity",
                "exclusion-constraint",
                "MISSING — two staff can still book one practitioner into the same slot",
                SchemaVerificationSeverity.Drift));
            return;
        }

        // A NON-partial constraint is worse than none: it makes a cancelled slot permanently unbookable, and
        // rebooking a cancelled slot is the most common scheduling action there is (AC-P1.16).
        var isPartial = constraint.Definition.Contains("WHERE", StringComparison.OrdinalIgnoreCase);
        findings.Add(new SchemaVerificationFinding(
            "Booking integrity",
            "exclusion-constraint",
            isPartial
                ? $"{constraint.Name} — partial (cancelled / no-show slots stay rebookable)"
                : $"{constraint.Name} — NOT PARTIAL; a cancelled slot is now permanently unbookable",
            isPartial ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
    }

    // ------------------------------------------------------------------ model vs database

    private static void VerifyIndexes(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        // Matched on table + ordered columns, not on name: EF's generated name and a hand-written migration's
        // name legitimately differ, and it is the covered columns that determine whether a query is served.
        var actual = facts.Database.Indexes
            .Select(i => i.Signature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in facts.Model.Indexes.OrderBy(i => i.Signature, StringComparer.OrdinalIgnoreCase))
        {
            var found = actual.Contains(expected.Signature);
            findings.Add(new SchemaVerificationFinding(
                "Indexes",
                expected.Signature,
                found
                    ? (expected.IsUnique ? "present (unique)" : "present")
                    : "MISSING in the database — the model declares it, so a migration was never applied",
                found ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    private static void VerifyForeignKeys(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        var actual = facts.Database.ForeignKeys
            .Select(fk => fk.Signature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expected in facts.Model.ForeignKeys.OrderBy(fk => fk.Signature, StringComparer.OrdinalIgnoreCase))
        {
            var found = actual.Contains(expected.Signature);
            findings.Add(new SchemaVerificationFinding(
                "Foreign keys",
                expected.Signature,
                found ? "present" : "MISSING in the database — the model declares it, so a migration was never applied",
                found ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    // ------------------------------------------------------------------ decimal precision

    private static void VerifyDecimalPrecision(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        // Checked against the DATABASE, because the drift this exists to catch is a model change whose
        // migration was never applied — comparing the model to itself would report success either way.
        foreach (var column in facts.Database.DecimalColumns.OrderBy(c => c.Signature, StringComparer.OrdinalIgnoreCase))
        {
            var isRate = IsRateException(column.Table, column.Column);

            if (isRate)
            {
                // A rate WIDENED to money precision is drift in the other direction: the convention swallowed
                // an annotation that was there for a reason.
                var keptItsOwn = column.Scale != MoneyScale;
                findings.Add(new SchemaVerificationFinding(
                    "Decimal precision",
                    column.Signature,
                    keptItsOwn
                        ? $"{column.Rendered} — rate column, deliberately not money precision"
                        : $"{column.Rendered} — WIDENED to money precision; this is a rate and must keep its own",
                    keptItsOwn ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
                continue;
            }

            var correct = column.Precision == MoneyPrecision && column.Scale == MoneyScale;
            findings.Add(new SchemaVerificationFinding(
                "Decimal precision",
                column.Signature,
                correct
                    ? column.Rendered
                    : $"{column.Rendered} — expected ({MoneyPrecision},{MoneyScale}); a narrower scale truncates the millime",
                correct ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }

        // The model side too: a later contributor re-adding an explicit HasColumnType would match the database
        // once its own migration applied, so the mapped store type is asserted independently. This is the check
        // that makes the precision fix durable rather than a one-off.
        foreach (var mapped in facts.MappedDecimals
            .Where(m => !IsRateException(m.Table, m.Column))
            .OrderBy(m => $"{m.Entity}.{m.Property}", StringComparer.OrdinalIgnoreCase))
        {
            var expected = $"numeric({MoneyPrecision},{MoneyScale})";
            var normalized = mapped.StoreType.Replace("decimal", "numeric", StringComparison.OrdinalIgnoreCase);
            var correct = normalized.Equals(expected, StringComparison.OrdinalIgnoreCase);

            findings.Add(new SchemaVerificationFinding(
                "Decimal precision (EF model)",
                $"{mapped.Entity}.{mapped.Property}",
                correct ? mapped.StoreType : $"{mapped.StoreType} — expected {expected}",
                correct ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    private static bool IsRateException(string table, string column) =>
        RatePrecisionExceptions.Any(e =>
            string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Column, column, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------ data migrations

    private static void VerifyDataMigrations(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        var counts = facts.DataMigrations;

        // A schema object can be present while its BACKFILL covered nothing — that is what these measure.
        // A null count means the thing it measures does not exist yet; saying so beats reporting 0, which would
        // claim a backfill succeeded when it has not run at all.
        Add("type-prefix-removed", counts.AppointmentsWithTypePrefixRemaining,
            n => n == 0
                ? "0 appointment note(s) still start with a 'Type: ' prefix"
                : $"{n} appointment note(s) still carry a 'Type: ' prefix",
            n => n == 0);

        // Reported as a fact, not repaired: pre-existing overlaps are exactly what the constraint's pre-flight
        // refuses to destroy, and resolving them belongs to a human with the clinic's context.
        Add("overlapping-appointment-pairs", counts.OverlappingAppointmentPairs,
            n => n == 0
                ? "0 overlapping pair(s) under the constraint's own predicate"
                : $"{n} overlapping pair(s) — the constraint cannot be installed until these are resolved",
            n => n == 0);

        if (counts.StockItemsWithLegacyExpiry is null || counts.StockItemsWithLegacyExpiryLackingBatch is null)
        {
            findings.Add(NotApplicable("stock-batch-backfill", "per-batch stock does not exist yet"));
        }
        else
        {
            var uncovered = counts.StockItemsWithLegacyExpiryLackingBatch.Value;
            findings.Add(new SchemaVerificationFinding(
                "Data migrations",
                "stock-batch-backfill",
                uncovered == 0
                    ? $"{counts.StockItemsWithLegacyExpiry} item(s) had a legacy expiry; all have an opening batch"
                    : $"{uncovered} of {counts.StockItemsWithLegacyExpiry} item(s) with a legacy expiry have NO opening batch — their date was dropped",
                uncovered == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }

        if (counts.PatientsMissingNormalizedName is null)
        {
            findings.Add(NotApplicable("normalized-name-populated", "the normalized-name column does not exist yet"));
        }
        else
        {
            var missing = counts.PatientsMissingNormalizedName.Value;
            findings.Add(new SchemaVerificationFinding(
                "Data migrations",
                "normalized-name-populated",
                missing == 0
                    ? $"{counts.PatientsTotal} patient(s), all with a normalized name"
                    : $"{missing} of {counts.PatientsTotal} patient(s) have none — duplicate detection will miss them",
                missing == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }

        void Add(string check, int? count, Func<int, string> detail, Func<int, bool> ok)
        {
            if (count is null)
            {
                findings.Add(NotApplicable(check, "what it measures does not exist yet"));
                return;
            }

            findings.Add(new SchemaVerificationFinding(
                "Data migrations",
                check,
                detail(count.Value),
                ok(count.Value) ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    /// <summary>
    /// A check that cannot run yet. Info, not Drift, on purpose: a part that has not been implemented is not a
    /// regression, and making <c>verify-schema</c> exit non-zero for unbuilt work would train the operator to
    /// ignore its exit code — which is the one thing a gate must not do.
    /// </summary>
    private static SchemaVerificationFinding NotApplicable(string check, string why) =>
        new("Data migrations", check, $"not applicable — {why}", SchemaVerificationSeverity.Info);
}
