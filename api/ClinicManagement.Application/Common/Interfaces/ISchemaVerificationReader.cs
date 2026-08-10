using ClinicManagement.Domain.Services;

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
    AuditLedgerFacts AuditLedger,
    IReadOnlyList<ClinicSubscriptionLedgerFact>? SubscriptionLedgers);

/// <summary>
/// One cabinet's stored entitlement date beside the ledger it is supposed to be a fold of
/// (<c>clinic-subscription</c> FR-9).
///
/// <para><b>⚠️ Why this is rows and not a count, unlike every other data-migration check here.</b> The comparison
/// is « does the stored <c>EndsOn</c> equal <c>SubscriptionLedger.Fold(entries)</c>? », and answering it in SQL
/// means re-expressing the fold's exclusive-cursor arithmetic as a recursive CTE — a second copy of exactly the
/// arithmetic R-6 exists to prevent, in a language where no compiler checks it against the first. So the reader
/// projects and <c>SchemaVerificationService</c> calls the <b>real</b> fold. The ledger is a handful of rows per
/// cabinet on a read-only operator verb, and the check stays unit-testable against a mocked reader like the rest.</para>
/// </summary>
public sealed record ClinicSubscriptionLedgerFact(
    Guid ClinicId,
    DateTime? StoredEndsOn,
    IReadOnlyList<SubscriptionLedgerEntry> Entries);

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
    /// Payment rows carrying a <b>banked stamp</b> on a method that is not <c>Cheque</c> (Group B), across both
    /// ledgers — <see cref="PaymentsWithChequeDetailsOnNonCheque"/>'s sibling, and here for the same reason.
    /// <para>
    /// The three columns per ledger, their widths and their nullability are diffed against the catalog for free by
    /// reading the EF model, so none of that is repeated in the service. What the model cannot express is the
    /// invariant: « only a cheque can be taken to the bank », enforced once in <c>ChequeBankedStamp.For</c> and
    /// deliberately not duplicated as a CHECK constraint, whose failure would be a 500 instead of the French
    /// refusal. A non-zero count means a write path reached the columns without passing the guard — which would
    /// make « chèques à encaisser » filter espèces in and out of a list of cheques. Null before the columns exist.
    /// </para>
    /// </summary>
    int? PaymentsWithBankedStampOnNonCheque,
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
    int? RowsAttributableFromAppointmentButUnattributed,
    /// <summary>
    /// Queued OS pushes whose <c>ClinicId</c> disagrees with the clinic of the device they are addressed to
    /// (<c>mobile-native-shells</c> Part 6).
    /// <para>
    /// The only part of that migration worth a hand-written line, and the reason is the same shape as the cheque
    /// invariant above: the two tables, their eight indexes and their three foreign keys are diffed against the
    /// catalog for free by reading the EF model, but « a push belongs to the clinic of the device it goes to » is a
    /// relationship <b>between</b> two independent FKs that no constraint expresses. A non-zero count is a
    /// cross-clinic delivery waiting to happen: the dispatcher compares the two and fails the row, but a
    /// disagreement means some write path produced one, and on a lock screen there is no request-time check left to
    /// catch it. Null before the tables exist.
    /// </para>
    /// </summary>
    int? PushDeliveriesWithMismatchedClinic,
    /// <summary>
    /// Pending clinic signups that can no longer become anything (<c>clinic-self-signup</c>): a row still
    /// unconsumed whose address <b>already has an account</b>, or a consumed row kept well past its retention.
    /// <para>
    /// The table's shape — its two unique indexes and its columns — is diffed against the catalog for free by
    /// reading the EF model, so the only line worth writing here is the one the model cannot state: this table
    /// has <b>no owner and no foreign key</b> (a signup exists precisely because its clinic does not), so nothing
    /// in the schema cascades it away and nothing but the opportunistic purge on the signup path ever deletes a
    /// row. A deployment that stops receiving signups therefore stops trimming, which is the failure mode
    /// choosing « no background job » accepts — and this is what makes it visible.
    /// </para>
    /// <para>
    /// ⚠️ The first half is a genuine invariant rather than housekeeping: a live token for an address that is now
    /// an account would provision a second clinic for somebody who already has one. Verification refuses it and
    /// spends the row, but a non-zero count means such a link is sitting in an inbox unspent. Null before the
    /// table exists.
    /// </para>
    /// </summary>
    int? ClinicSignupOrphans,
    /// <summary>
    /// Rows across the <b>seven clinical children of <c>Patients</c></b> whose denormalised <c>ClinicId</c> does
    /// not equal their patient's — the one thing that migration's model changes cannot state.
    /// <para>
    /// Seven columns and seven indexes are diffed against the catalog for free by reading the EF model. What no
    /// model construct can express is « this column always equals the patient's », and that equality is the whole
    /// basis of the global query filters added with it. The two ways it can break point in opposite directions
    /// and this figure catches both. A <b>backfill that covered nothing</b> leaves rows at
    /// <c>Guid.Empty</c>, and because the filter compares the column to the scoped clinic the symptom is not an
    /// error but an <i>empty patient record</i> — a fiche of ten years' standing that no longer exists as far as
    /// any screen can tell. A <b>write path that names the wrong clinic</b> is the mirror image: the row is
    /// visible, to the wrong practice.
    /// </para>
    /// <para>
    /// ⚠️ Deliberately not a CHECK or a composite foreign key. A composite FK would state the rule, but it makes
    /// every one of these tables carry the patient's clinic in its own key shape and turns a violation into a 500
    /// at insert rather than a line in this report — and the constructors already take the clinic from the
    /// patient they just tenant-checked, so a violation means a *new* write path exists that did not. That is
    /// something to be told about, not something to crash on. Null before the columns exist.
    /// </para>
    /// </summary>
    int? ClinicalChildrenWithWrongClinic,
    /// <summary>
    /// Cabinets with <b>no entitlement row at all</b> (<c>clinic-subscription</c> AC-6.4, FR-13). Must be 0.
    /// <para>
    /// A <b>derived count over every cabinet</b>, deliberately — never a count qualified by which door created it,
    /// and never a list of known doors. FR-13's whole point is that a *third* construction door added later is
    /// caught, and a check that enumerates today's two would pass forever while the new one leaked. It is a flat
    /// count and not « …on a deployment that enforces subscriptions » for the same reason: where enforcement is
    /// off the entitlement is still created, open-ended, so 0 is the right answer in all three topologies and this
    /// figure needs to know nothing about the deployment it is run on. Null before the table exists.
    /// </para>
    /// </summary>
    int? ClinicsWithoutEntitlement,
    /// <summary>
    /// <c>Grandfathered</c> ledger entries — reported as <b>Info with its count</b>, not asserted (AC-6.2/AC-6.4).
    /// <para>
    /// ⚠️ It is deliberately not compared against the clinic count. AC-6.4's « equals the number of cabinets that
    /// existed » is established by FR-9's prescribed before/after run and <b>diff</b>, because the moment the first
    /// new cabinet arrives the two figures legitimately differ for ever — a check asserting equality would go red
    /// on the deployment's first signup and be deleted as noisy. Null before the table exists.
    /// </para>
    /// </summary>
    int? GrandfatheredEntitlementEntries);
