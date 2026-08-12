using ClinicManagement.Domain.Enums;
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
    IReadOnlyList<ClinicSubscriptionLedgerFact>? SubscriptionLedgers,
    /// <summary>
    /// Whether <c>ClinicSubscriptions.LatestCoverKind</c> exists yet. Separate from the facts above because a
    /// <b>null</b> stored kind is a real value — a cabinet whose every ledger entry has been cancelled — and
    /// « the column is not there » must not be reported as that.
    /// </summary>
    bool SubscriptionCoverKindColumnPresent,
    /// <summary>
    /// The WhatsApp reminder forfait's three checks, or <b>null</b> before its tables exist.
    /// </summary>
    MessagingAllowanceFacts? MessagingAllowances,
    /// <summary>
    /// The deployment's internal root certificate, or <b>null</b> where none is configured — which is the
    /// normal state on <c>SelfHostedLan</c> and on a developer machine, and is « not applicable » rather than
    /// « expired ». Reported here because this verb is the one thing already run before and after every schema
    /// change, so it is where a ten-year certificate's remaining life will actually be seen
    /// (<c>hosted-security-hardening</c> FR-2.6).
    /// </summary>
    InternalCertificateFact? InternalCertificate,
    /// <summary>
    /// How much of the deployment's stored ciphertext has moved onto the key ring's current generation
    /// (<c>hosted-security-hardening</c> FR-3.1), or <b>null</b> where the caller supplied no Data Protection
    /// provider — a fourth "side", beside the model, the catalog and the internal certificate, and null is
    /// « not applicable » rather than « zero left to do ».
    /// </summary>
    SecretProtectionFacts? SecretProtection = null,
    /// <summary>
    /// What walking each audit chain found (<c>hosted-security-hardening</c> FR-4.1), or <b>null</b> where the
    /// chain columns do not exist yet or the caller supplied no chain key — a fifth "side", and null is « not
    /// applicable » rather than « no breaks », which would be a clean bill of health nobody measured.
    /// </summary>
    AuditChainFacts? AuditChain = null);

/// <summary>
/// The audit chains as walked, one result per chain.
///
/// <para>⚠️ <b>The walk happens in the reader, not here.</b> Every other fact on <see cref="SchemaFacts"/> is a
/// count or a small projection; this one would be the ledger — every row a practice has ever written — carried
/// into Application so the service could re-derive it. The reader walks it streaming, per chain, and hands over
/// the verdicts. It still calls the <b>real</b> <c>AuditChain.Walk</c> and never re-expresses the arithmetic in
/// SQL, which is the property that matters (the <c>subscription-cover-kind-matches-ledger</c> precedent).</para>
/// </summary>
public sealed record AuditChainFacts(IReadOnlyList<AuditChainWalkResult> Chains);

/// <summary>
/// The figure that says the <c>reprotect-secrets</c> verb finished — and therefore the only thing that
/// authorises deleting the superseded plaintext key files (FR-3.1).
///
/// <para>⚠️ <b>Deleting a key file before its ciphertext has moved is R-2's data loss from the other
/// direction</b>: every second factor and every clinic's reminder credentials become unreadable at once, with no
/// way back. Hence a per-family count rather than a single total — « 3 remaining » says nothing about which
/// recovery an operator needs, and the six families recover four different ways.</para>
/// </summary>
/// <param name="KeyRingIsCertificateProtected">
/// Whether the ring encrypts what it writes. <b>False is drift on a deployment that requires it</b> — that is
/// FR-3.1's claim, and it is otherwise invisible: a cleartext ring works perfectly and says nothing.
/// </param>
/// <param name="ProtectingCertificateDaysRemaining">
/// Whole days until the protecting certificate expires; null when none is configured. Reported for FR-3.2's
/// rotation, on <see cref="InternalCertificateFact"/>'s precedent — this verb is the thing already run before and
/// after every schema change, so it is where a remaining life is actually seen.
/// </param>
public sealed record SecretProtectionFacts(
    bool KeyRingIsCertificateProtected,
    int? ProtectingCertificateDaysRemaining,
    IReadOnlyList<SecretFamilyFact> Families);

/// <summary>
/// One protected column family: how many rows hold ciphertext, and how many of those are not yet under the
/// ring's current generation.
/// </summary>
public sealed record SecretFamilyFact(string Name, int Rows, int NotUnderCurrentGeneration);

/// <summary>
/// One reading of the deployment's internal root certificate. Deliberately a neutral shape: the reading itself
/// is done in Infrastructure (which owns the file access and the X.509 parsing) and this project references
/// Domain alone, so it cannot name that type.
/// </summary>
/// <param name="DaysRemaining">
/// Whole days until <c>NotAfter</c>. Negative on an expired certificate, which is a more useful reading than
/// clamping to zero — « expired 40 days ago » and « expires today » are different operator situations.
/// </param>
public sealed record InternalCertificateFact(
    string Path,
    int DaysRemaining,
    bool Usable,
    string Detail);

/// <summary>
/// Everything the three <c>vendor-whatsapp-messaging-quota</c> checks read, in one shape.
/// </summary>
/// <param name="CurrentMonthKey">
/// The <b>Tunisian</b> month the daily pass is supposed to have provisioned by now, resolved once by the reader
/// rather than by each check — « aujourd'hui » read twice either side of Tunisian midnight gives two answers, and
/// the two figures below would then describe different months.
/// </param>
/// <param name="SellsVendorMessaging">
/// Whether this deployment does vendor-purchased messaging at all. It gates only
/// <c>messaging-month-covers-every-clinic</c>, whose writer — the daily pass — is registered nowhere else, so
/// without it that check would go permanently red on the two kinds the feature is absent from (EC-16).
/// </param>
public sealed record MessagingAllowanceFacts(
    string CurrentMonthKey,
    bool SellsVendorMessaging,
    IReadOnlyList<ClinicMessagingLedgerFact> Cabinets);

/// <summary>
/// One cabinet's whole allocation ledger beside the counting rows that are supposed to be a fold of it (FR-1a, FR-2).
///
/// <para><b>⚠️ Rows and not a count, for <c>subscription-end-date-matches-ledger</c>'s reason</b> (R-6): comparing a
/// stored <c>AllowanceMessages</c> against the fold *in SQL* means re-expressing « the last standing entry effective
/// on or before this month, plus that month's top-ups » as a window function — a second copy of the one arithmetic
/// this feature keeps single, in a language where no compiler checks it against the first. So the reader projects and
/// <c>SchemaVerificationService</c> calls the <b>real</b> <see cref="MessagingAllowanceLedger.Fold"/>.</para>
///
/// <para><b>Every cabinet appears, including one with no rows at all</b> — that is precisely what
/// <c>messaging-month-covers-every-clinic</c> counts, and a projection keyed off the ledger would make FR-3's failure
/// state the one state it cannot show.</para>
/// </summary>
public sealed record ClinicMessagingLedgerFact(
    Guid ClinicId,
    IReadOnlyList<MessagingAllowanceLedgerEntry> Entries,
    IReadOnlyList<StoredMessagingMonth> Months);

/// <summary>One <c>ClinicMessagingMonths</c> row, reduced to what the fold is compared against.</summary>
public sealed record StoredMessagingMonth(string MonthKey, int AllowanceMessages, int ConsumedMessages);

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
/// <param name="StoredLatestCoverKind">
/// The cabinet's denormalised <c>LatestCoverKind</c>, which <c>subscription-cover-kind-matches-ledger</c> re-derives
/// from <paramref name="Entries"/> through the <b>real</b> fold. Null both for « every entry cancelled » and — until
/// the column exists — for a database that has not run the migration; <c>SchemaFacts</c> carries a flag so the two
/// are told apart.
/// </param>
public sealed record ClinicSubscriptionLedgerFact(
    Guid ClinicId,
    DateTime? StoredEndsOn,
    SubscriptionPeriodKind? StoredLatestCoverKind,
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
    /// Console accounts marked as having enrolled a second factor while carrying <b>no secret</b>
    /// (<c>platform-console</c> AC-1.3a). Null before the table exists.
    /// <para>
    /// The table's shape is diffed against the catalog for free; what no constraint states is that
    /// <c>TotpEnrolledAt</c> and <c>ProtectedTotpSecret</c> are two halves of one fact. An account in the broken
    /// half is <b>unusable and says nothing about it</b>: sign-in demands a code, the enrolment path refuses
    /// because the account already counts as enrolled, and the only way back is the <c>platform-account
    /// --reset-totp</c> verb — which an operator has no reason to reach for, because every screen simply reports
    /// « code invalide ». It is the vendor locking itself out of its own console with no error anywhere.
    /// </para>
    /// </summary>
    int? PlatformAccountsEnrolledWithoutSecret,
    /// <summary>
    /// Cabinets with <b>no activity snapshot at all</b> (<c>platform-console</c> AC-2.4a, EC-15). Null before the
    /// table exists.
    /// <para>
    /// The counter job's per-cabinet loop swallows one cabinet's failure so the other ninety-nine still get
    /// measured — correct, and it means a cabinet can be skipped every night while the run logs clean. The
    /// portfolio renders such a cabinet as « jamais mesuré » rather than as zeros, so nothing on screen is a lie;
    /// but nothing on screen distinguishes « the pass has never run » from « this one cabinet has been failing
    /// since June », and this figure is where that distinction lives.
    /// </para>
    /// <para>
    /// ⚠️ A fresh deployment legitimately reports every cabinet here until the first nightly pass. That is not a
    /// false positive — it is the same statement the console itself makes — which is why it is reported as drift
    /// to be read rather than as a failure to be silenced.
    /// </para>
    /// </summary>
    int? ClinicsWithoutActivitySnapshot,
    /// <summary>
    /// Activity snapshots whose own figures contradict each other (<c>platform-console</c> AC-2.1). Null before
    /// the table exists.
    /// <para>
    /// Every figure on a snapshot is written by one <c>Restate</c> call over one window of one cabinet's audit
    /// rows, which makes several relations between them true by construction: seven days cannot hold more saves
    /// than thirty, thirty clinic-local days cannot contain thirty-one active ones, no active day exists without
    /// a save, and a cabinet that saved something has a <c>LastWriteAt</c>. A violation therefore means the row
    /// was written by something other than that one call — a half-applied refactor, a partial update, a second
    /// writer — and the visible symptom would be a portfolio sorted or filtered on a figure that is quietly wrong
    /// rather than an error.
    /// </para>
    /// <para>
    /// ⚠️ This replaces the plan's <c>clinic-activity-day-unique-per-clinic-day</c>, which the unique index on
    /// (cabinet, day) makes <b>unfalsifiable</b> — and that index is already diffed against the catalog for free.
    /// A check that cannot fail is worse than no check: it reports « ✓ » for ever about something it never looked
    /// at, which is the exact rot this verb exists to avoid.
    /// </para>
    /// </summary>
    int? IncoherentActivitySnapshots,
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
    int? GrandfatheredEntitlementEntries,
    /// <summary>
    /// Administrators who hold a <b>live session</b> while having no verified second factor, where the
    /// deployment requires one (<c>hosted-security-hardening</c> FR-1.1). Zero is the claim. Null before the
    /// tables exist.
    /// <para>
    /// ⚠️ <b>The plan's original name for this — « every admin has a factor or is unenrolled » — is a
    /// tautology</b>: every administrator satisfies one branch or the other, so it could never go red. That is
    /// exactly the unfalsifiability that got <c>clinic-activity-day-unique-per-clinic-day</c> replaced. What is
    /// falsifiable, and what actually matters, is an admin who is <i>still working</i> without one: the login
    /// ladder refuses a fresh sign-in, but a session minted before the requirement — or before that account was
    /// promoted — would go on working until the per-request check was added. A non-zero count means that check
    /// is not doing its job.
    /// </para>
    /// </summary>
    int? AdminsWithoutFactorHoldingLiveSession = null,
    /// <summary>
    /// Session families whose owning account no longer exists. Zero is the claim; the FK cascades, so a non-zero
    /// count means the cascade is not what the model says it is. Null before the table exists.
    /// </summary>
    int? SessionFamilyOrphans = null,
    /// <summary>
    /// The offset in seconds between the application's clock and PostgreSQL's, reported as <b>Info</b>.
    /// <para>
    /// ⚠️ <b>It cannot see the failure that matters, and says so in the check's own text.</b> The API and the
    /// database run in containers on one host reading one clock, so this comparison is ~0 by construction. The
    /// case that breaks TOTP — <i>the host</i> drifting from real time, which fails every code at once with the
    /// same French sentence as a wrong password — moves both sides together and is invisible here. The real
    /// control is NTP on the host, named beside this in <c>deploy/README.md</c>. It is reported anyway because
    /// the one thing it <i>can</i> catch is a container started with a different <c>TZ</c> or a deliberately
    /// skewed clock, and because a stated blind spot is worth more than a silent one.
    /// </para>
    /// </summary>
    double? AppToDatabaseClockOffsetSeconds = null,
    /// <summary>
    /// Clinics whose Google Calendar refresh token is <b>still stored in the clear</b>
    /// (<c>hosted-security-hardening</c> FR-3.4). Zero is the claim, and reaching zero is what authorises the
    /// later migration that drops the column. Null before the protected column exists.
    /// <para>
    /// A backfill is invisible to every other layer, and this one especially: the column exists, the API returns
    /// it, and a clinic whose token was never converted goes on syncing perfectly from the plaintext nobody
    /// encrypted. Nothing errors, nothing degrades, and the credential the feature exists to protect stays
    /// readable off a stolen disk — which is why the count, and not a green test, is what says it finished.
    /// </para>
    /// </summary>
    int? ClinicsWithPlaintextGoogleToken = null
);
