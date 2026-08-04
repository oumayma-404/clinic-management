namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Reads the schema facts the <c>verify-schema</c> report asserts: what the <b>EF model</b> says should exist,
/// what the <b>database</b> actually has, and the row counts that prove a data migration finished its job.
///
/// Implemented in Infrastructure because it needs both sides — PostgreSQL's catalog (<c>pg_extension</c>,
/// <c>pg_constraint</c>, <c>pg_indexes</c>, <c>information_schema.columns</c>) and EF's model metadata. The
/// <c>verify-schema</c> verb builds its container from <c>AddInfrastructure</c> only — never
/// <c>AddApplication</c> — so no <c>ICurrentClinicProvider</c> is registered, the global clinic query filters
/// stay inactive, and the backfill counts span every clinic without <c>IgnoreQueryFilters()</c>.
///
/// This seam exists for the same reason <see cref="IMoneyReconciliationReader"/> does: the assertions live in
/// Application so they are unit-testable against a mocked reader (the UnitTests project references Application,
/// and nothing in it touches a database).
/// </summary>
public interface ISchemaVerificationReader
{
    Task<SchemaFacts> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Everything the schema report needs, read in one pass.</summary>
public sealed record SchemaFacts(
    IReadOnlyList<string> InstalledExtensions,
    IReadOnlyList<TableConstraintFact> Constraints,
    SchemaSide Model,
    SchemaSide Database,
    IReadOnlyList<MappedDecimalFact> MappedDecimals,
    DataMigrationCounts DataMigrations,
    AuditLedgerFacts AuditLedger);

/// <summary>
/// The two things about <c>AuditEntries</c> the EF model cannot state, and whose violation is <b>silent</b> —
/// which is the only reason a check earns a line in this report.
///
/// <para>Its indexes and column types need nothing here: the model declares them, so the model-driven diff
/// covers them for free. These two do not survive that treatment.</para>
/// </summary>
/// <param name="TableExists">
/// False before the <c>AddAuditEntries</c> migration has been applied, which makes the rest « not applicable »
/// rather than drift.
/// </param>
/// <param name="ClinicIdIsNullable">
/// <c>AuditEntries.ClinicId</c> must stay nullable. A job or a console verb can mutate a row with no clinic
/// derivable from it, and the interceptor writes that row with a null. If a migration ever made the column
/// <c>NOT NULL</c>, the insert would throw <em>inside</em> the interceptor's own swallow-and-log — so the ledger
/// would simply stop recording every non-interactive mutation, with nothing on any screen to say so. Null when
/// the table does not exist yet.
/// </param>
public sealed record AuditLedgerFacts(bool TableExists, bool? ClinicIdIsNullable);

/// <summary>
/// One side of the comparison — what the model expects, or what the database has. Symmetrical on purpose: the
/// report diffs the two rather than checking either against a hand-maintained list, so a schema object added in
/// a configuration file is verified automatically. A hardcoded expectation list is the exact shape of bug this
/// feature's own plan flags three times (a "contract" test that never fails on a new area).
/// </summary>
public sealed record SchemaSide(
    IReadOnlyList<IndexFact> Indexes,
    IReadOnlyList<ForeignKeyFact> ForeignKeys,
    IReadOnlyList<DecimalColumnFact> DecimalColumns);

/// <summary>
/// One table constraint, with its kind and full definition. The definition is what lets the report assert an
/// exclusion constraint is <b>partial</b> — a non-partial one makes a cancelled slot permanently unbookable,
/// which is why AC-P1.16 required the predicate.
/// </summary>
public sealed record TableConstraintFact(string Table, string Name, char Kind, string Definition);

/// <summary>One index, identified by table + the ordered columns it covers (names differ between EF and PG).</summary>
public sealed record IndexFact(string Table, string Name, IReadOnlyList<string> Columns, bool IsUnique, string? Filter)
{
    /// <summary>Table + columns — the identity the two sides are matched on, since names can legitimately differ.</summary>
    public string Signature => $"{Table}({string.Join(", ", Columns)})";
}

/// <summary>One foreign key: which table's column(s) point at which table.</summary>
public sealed record ForeignKeyFact(string Table, IReadOnlyList<string> Columns, string ReferencedTable)
{
    public string Signature => $"{Table}({string.Join(", ", Columns)}) -> {ReferencedTable}";
}

/// <summary>A numeric column's precision/scale, on whichever side is being described.</summary>
public sealed record DecimalColumnFact(string Table, string Column, int? Precision, int? Scale)
{
    public string Signature => $"{Table}.{Column}";

    public string Rendered => $"({Precision?.ToString() ?? "?"},{Scale?.ToString() ?? "?"})";
}

/// <summary>A decimal property as the EF model maps it, with the store type it resolves to.</summary>
public sealed record MappedDecimalFact(string Entity, string Property, string Table, string Column, string StoreType);

/// <summary>
/// Row counts that prove each data migration in this feature actually finished — a schema change can be present
/// while its backfill silently covered nothing.
///
/// Each count is <b>nullable</b>: null means "the column or table this measures does not exist yet", which is
/// the honest answer before the part that introduces it has run. Reporting 0 there would claim a backfill
/// succeeded when it has not happened at all.
/// </summary>
/// <summary>
/// <b>The stock-batch check is deliberately two-phase.</b> Before the batch migration runs, the question is
/// "does every item with a legacy expiry have an opening batch?" - answerable from
/// <c>StockItems.ExpiryDate</c>. <b>After</b> it runs that column is dropped, so the original question becomes
/// permanently unanswerable and the durable invariant takes over: "does every item WITH STOCK have at least one
/// batch?" - which is what FEFO actually depends on. Reporting only the first would have made this check
/// unrunnable forever the moment its own migration applied.
/// </summary>
public sealed record DataMigrationCounts(
    int? AppointmentsWithTypePrefixRemaining,
    int? OverlappingAppointmentPairs,
    int? StockItemsWithLegacyExpiry,
    int? StockItemsWithLegacyExpiryLackingBatch,
    int? StockItemsWithStockLackingBatch,
    int? PatientsMissingNormalizedName,
    int? PatientsTotal,
    /// <summary>
    /// Appointments naming a lead act with **no** row in <c>AppointmentProcedures</c> — the durable invariant the
    /// multi-act backfill establishes. The three procedure scalars are a derived snapshot of the first act now, so
    /// a scalar with no row behind it is a visit the agenda paints with an act the edit dialog cannot see.
    /// </summary>
    int? AppointmentsWithActScalarLackingRow,
    /// <summary>
    /// Procedure types whose <c>Description</c> still holds a canonical clinical discipline — i.e. rows the
    /// `AddProcedureTypeCategory` backfill was supposed to move into <c>Category</c> and did not.
    /// <para>
    /// This is the one shape of drift the migration can leave behind that nothing else would notice: the column
    /// exists, the UI reads it, and an act whose category stayed in the description simply renders as unfiled —
    /// indistinguishable from an act the clinic genuinely never categorised. Null before the column exists.
    /// </para>
    /// </summary>
    int? ProcedureTypesWithCategoryStillInDescription,
    /// <summary>
    /// Clinics whose backup schedule was left at the scaffolder's zeros (L4a) — a non-positive retention count or
    /// staleness threshold.
    /// <para>
    /// The one shape of drift this migration can leave that nothing else would see: EF's differ emits
    /// <c>defaultValue: 0</c> for a new non-nullable <c>int</c>, and a retention of <b>0</b> is the single value
    /// the pruner's « never delete the last surviving backup » floor exists to survive. Every layer would report
    /// success while the practice's retention policy is « keep nothing ». Null before the columns exist.
    /// </para>
    /// </summary>
    int? ClinicsWithUnsetBackupSchedule,
    /// <summary>
    /// Payment rows carrying cheque details on a method that is <b>not</b> <c>Cheque</c> (L8), across both ledgers.
    /// <para>
    /// The one thing about these columns the EF model cannot express, and therefore the only part of this migration
    /// worth a hand-written line: the columns, their widths and their two partial indexes are all diffed against the
    /// catalog for free, but « a cheque number only ever appears on a cheque » is a <i>domain</i> invariant enforced
    /// in <c>ChequeDetails.For</c>. Deliberately not a CHECK constraint — that would be a second copy of the rule,
    /// and the copy that fired would surface as a 500 instead of the French refusal. So it is <b>verified</b> here
    /// instead: a non-zero count means some write path reached the columns without passing through the guard, which
    /// would put a cheque number on a cash payment and make « chèques à encaisser » list a row that is not a cheque.
    /// Null before the columns exist.
    /// </para>
    /// </summary>
    int? PaymentsWithChequeDetailsOnNonCheque,
    /// <summary>
    /// L9 — money and clinical rows whose linked visit names a practitioner while the row itself was left
    /// unattributed, i.e. rows the <c>AddPractitionerAttribution</c> backfill was supposed to reach and did not.
    /// <para>
    /// This is the only part of that migration worth a hand-written line. Three nullable columns, three indexes and
    /// four foreign keys are all diffed against the catalog for free by reading the EF model — but a <b>backfill</b>
    /// is invisible to every layer: the column exists, the API returns it, the filter works, and an invoice whose
    /// practitioner was knowable and simply not copied renders as « non attribué », indistinguishable from one that
    /// genuinely has none. A backfill covering zero rows on a practice with two dentists is the failure this line
    /// exists to see. Null before the columns exist.
    /// </para>
    /// <para>
    /// ⚠️ It counts <b>recoverable</b> misses only — rows whose appointment names a doctor. A row with no
    /// appointment, or one booked with no practitioner, is legitimately unattributed and is not drift.
    /// </para>
    /// </summary>
    int? RowsAttributableFromAppointmentButUnattributed);
