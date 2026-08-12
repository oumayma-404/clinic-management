using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Services;

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
        // Its absence has a nastier signature than a missing index: every paginated list's search predicate calls
        // unaccent(), so without the extension the searches do not degrade — they throw 42883 (function does not
        // exist), and only the moment someone types in a search box. A schema check is the only thing that catches
        // that before a user does, since no unit test touches a database.
        ("unaccent", "every paginated list's free-text search folds accents in SQL via unaccent()"),
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
        VerifyAuditLedger(facts, findings);
        VerifyDataMigrations(facts, findings);
        VerifySubscriptions(facts, findings);

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

    // ------------------------------------------------------------------ the audit ledger

    /// <summary>
    /// The audit ledger's two properties that the model cannot state and whose violation is <b>silent</b>.
    ///
    /// <para>Its indexes are deliberately <em>not</em> named here — <c>AuditEntryConfiguration</c> declares both,
    /// so the model-driven diff in <see cref="VerifyIndexes"/> already covers them, and repeating them would be
    /// the hand-maintained expectation list this whole verb exists to avoid. What is left is the residue: the
    /// nullability the interceptor depends on, and the absence of the foreign keys the table deliberately does
    /// not have.</para>
    /// </summary>
    private static void VerifyAuditLedger(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        if (!facts.AuditLedger.TableExists)
        {
            // Named rather than skipped, for the reason the stock-batch phases document: a check that quietly
            // vanishes from the report is indistinguishable from one that was forgotten, and the before/after
            // diff only works if every line is accounted for.
            foreach (var check in new[] { "audit-ledger-clinic-nullable", "audit-ledger-has-no-foreign-keys" })
            {
                findings.Add(NotApplicableIn("Audit ledger", check, "AuditEntries does not exist yet"));
            }

            return;
        }

        var nullable = facts.AuditLedger.ClinicIdIsNullable == true;
        findings.Add(new SchemaVerificationFinding(
            "Audit ledger",
            "audit-ledger-clinic-nullable",
            nullable
                ? "AuditEntries.ClinicId is nullable — an unattributable mutation can still be recorded"
                : "AuditEntries.ClinicId is NOT NULL — every job/CLI mutation with no clinic in scope will fail "
                  + "its insert inside the interceptor's own swallow-and-log, so the ledger stops recording silently",
            nullable ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));

        // The one assertion in this whole report that looks for something that must be ABSENT, and the reason it
        // is here: VerifyForeignKeys only diffs model → database, so it reports a *missing* FK and can never see
        // an *extra* one. A well-meaning migration adding AuditEntries.ClinicId → Clinics ON DELETE CASCADE would
        // delete a clinic's entire audit history along with the clinic, which is the one thing a ledger must never
        // do — and nothing else in the codebase would notice.
        var unexpected = facts.Database.ForeignKeys
            .Where(fk => string.Equals(fk.Table, "AuditEntries", StringComparison.OrdinalIgnoreCase))
            .Select(fk => fk.Signature)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        findings.Add(new SchemaVerificationFinding(
            "Audit ledger",
            "audit-ledger-has-no-foreign-keys",
            unexpected.Count == 0
                ? "AuditEntries references nothing — the ledger outlives the clinics and accounts it describes"
                : $"AuditEntries has {unexpected.Count} foreign key(s) it must not have ({string.Join(", ", unexpected)}) "
                  + "— a cascade from Clinics or Users would erase the evidence with its subject",
            unexpected.Count == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
    }

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

        // Multi-act séances. Not "did the backfill insert N rows" but the invariant it establishes: the parent's
        // three procedure scalars are a DERIVED snapshot of the first act, so a scalar with no row behind it means
        // the agenda paints a visit with an act the edit dialog cannot see — and the first save of that visit
        // would then persist the emptiness.
        Add("appointment-act-rows", counts.AppointmentsWithActScalarLackingRow,
            n => n == 0
                ? "0 appointment(s) name an act with no AppointmentProcedures row"
                : $"{n} appointment(s) name an act with NO AppointmentProcedures row — the backfill missed them",
            n => n == 0);

        // The category move. Its failure mode is *quiet*, which is why it is worth a line: the column, the API and
        // the UI can all be present and correct while an act's discipline is still sitting in its Description —
        // and such an act renders as merely unfiled, indistinguishable from one nobody ever categorised. Nothing
        // in the unit-test suite can see this, since none of it touches a database.
        Add("procedure-type-category-move", counts.ProcedureTypesWithCategoryStillInDescription,
            n => n == 0
                ? "0 procedure type(s) still carry a discipline in Description"
                : $"{n} procedure type(s) still carry a discipline in Description — the backfill missed them",
            n => n == 0);

        // L4a's backfill, and the same kind of quiet failure: EF's differ scaffolds `defaultValue: 0` for a new
        // non-nullable int, so a clinic left at zero has a retention policy of « keep nothing » and a staleness
        // threshold that fires immediately — while the columns, the endpoint and the settings screen are all
        // present and correct. Nothing in the test suite can see it, since none of it touches a database.
        Add("backup-schedule-backfill", counts.ClinicsWithUnsetBackupSchedule,
            n => n == 0
                ? "0 clinic(s) have a non-positive backup retention or staleness threshold"
                : $"{n} clinic(s) have a retention or staleness threshold of 0 — the backfill missed them, "
                  + "so retention means « keep nothing »",
            n => n == 0);

        // L8's cheque columns. Their shape — six columns, two widths, two partial indexes — is diffed against the
        // catalog for free by the model comparison, so nothing about it is repeated here. What the model cannot
        // express is the invariant, and that is the whole reason this line exists: cheque details belong only to a
        // cheque, enforced once in `ChequeDetails.For` rather than as a CHECK constraint (a second copy of the rule
        // whose failure would be a 500 instead of a French refusal). A non-zero count therefore means a write path
        // reached the columns without passing the guard — a cheque number sitting on a cash payment, which would
        // make « chèques à encaisser » list a row that is not a cheque.
        Add("cheque-details-only-on-cheques", counts.PaymentsWithChequeDetailsOnNonCheque,
            n => n == 0
                ? "0 payment(s) carry cheque details on a non-cheque method, across both ledgers"
                : $"{n} payment(s) carry cheque details on a NON-cheque method — some write path bypassed "
                  + "ChequeDetails.For",
            n => n == 0);

        // Group B's stamp, and the same reasoning one migration later: three more columns per ledger whose shape
        // the model diff already covers, and one invariant it cannot state — only a cheque can be taken to the
        // bank. A non-zero count means some write path set the stamp without passing `ChequeBankedStamp.For`,
        // which would let « chèques à encaisser » move a cash payment between its two views.
        Add("cheque-banked-only-on-cheques", counts.PaymentsWithBankedStampOnNonCheque,
            n => n == 0
                ? "0 payment(s) carry a banked stamp on a non-cheque method, across both ledgers"
                : $"{n} payment(s) carry a banked stamp on a NON-cheque method — some write path bypassed "
                  + "ChequeBankedStamp.For",
            n => n == 0);

        // L9's backfill. A non-zero count means a row whose practitioner was knowable from its own visit was left
        // unattributed — which renders as « non attribué » and is indistinguishable on every screen from a row that
        // genuinely has none. That is exactly the class of drift only this verb can see: the columns, the indexes and
        // the four FKs are all checked against the catalog by the model diff above.
        Add("practitioner-attribution-backfill", counts.RowsAttributableFromAppointmentButUnattributed,
            n => n == 0
                ? "0 invoice/fiche row is unattributed while its appointment names a practitioner"
                : $"{n} invoice/fiche row(s) could be attributed from their appointment and were not — "
                  + "the L9 backfill did not reach them",
            n => n == 0);

        // Part 6's push tables. Their shape is diffed against the catalog for free, so the only line here is the
        // one relationship no constraint can state: a queued push and the device it is addressed to must belong to
        // the same clinic. A mismatch is a cross-clinic notification, and a lock screen has no request-time check
        // left to stop it — the dispatcher's own comparison is the last one, and this is how we learn it fired.
        Add("push-delivery-clinic-matches-device", counts.PushDeliveriesWithMismatchedClinic,
            n => n == 0
                ? "0 queued push(es) disagree with their device's clinic"
                : $"{n} queued push(es) name a different clinic from the device they are addressed to — "
                  + "some write path produced a cross-clinic delivery",
            n => n == 0);

        // clinic-self-signup. The table's two unique indexes and its columns are diffed against the catalog for
        // free; what needs a line is that it is the one table with **no owner and no foreign key** — a signup
        // exists because its clinic does not — so nothing cascades it away and only the opportunistic purge on
        // the signup path ever deletes a row. A live token for an address that has since become an account is
        // the half that is a real invariant rather than housekeeping.
        Add("clinic-signup-has-no-orphans", counts.ClinicSignupOrphans,
            n => n == 0
                ? "0 stale or superseded clinic signup(s)"
                : $"{n} clinic signup(s) can no longer become anything — a pending row whose address already "
                  + "has an account, or a consumed row past retention that the signup-path purge never reached",
            n => n == 0);

        // hosted-security-hardening FR-1.1. ⚠️ Deliberately not « every admin has a factor or is unenrolled »,
        // which the plan proposed and which is a TAUTOLOGY — every administrator satisfies one branch or the
        // other, so it could never go red. What is falsifiable is an admin still *working* without one.
        Add("admins-without-a-factor-holding-a-live-session", counts.AdminsWithoutFactorHoldingLiveSession,
            n => n == 0
                ? "0 administrator(s) hold a live session without a verified second factor"
                : $"{n} administrator(s) are still working with a live session and no second factor — the "
                  + "per-request enrolment check is not refusing them",
            n => n == 0);

        Add("session-families-have-no-orphans", counts.SessionFamilyOrphans,
            n => n == 0
                ? "0 session family(ies) outlive their account"
                : $"{n} session family(ies) name an account that no longer exists — the cascade is not what the "
                  + "model declares",
            n => n == 0);

        // ⚠️ Info, ALWAYS — never a drift verdict. The comparison is ~0 by construction (one host, one clock),
        // and the failure that actually breaks every TOTP code at once — the host drifting from real time —
        // moves both sides together and cannot be seen from here. Said out loud rather than left implied,
        // because a check reporting « ok » about a thing it cannot measure is worse than no check.
        findings.Add(counts.AppToDatabaseClockOffsetSeconds is { } offset
            ? new SchemaVerificationFinding(
                "Data migrations",
                "server-clock-drift",
                $"application clock is {offset:0.###}s from the database's. ⚠️ Both run on one host and read one "
                + "clock, so this cannot detect the case that matters — the HOST drifting from real time, which "
                + "fails every second-factor code at once with the same message as a wrong password. NTP on the "
                + "host is the real control.",
                SchemaVerificationSeverity.Info)
            : NotApplicable("server-clock-drift", "the database clock could not be read"));

        // The invariant the seven clinical query filters rest on. A non-zero count is one of two failures and
        // both are silent: a backfill that covered nothing (rows stuck at Guid.Empty, so a patient's whole
        // record reads as empty rather than as an error), or a write path that named a clinic other than the
        // patient's (the row is visible — to the wrong practice).
        // platform-console Part 1. Two columns that are halves of one fact, with no constraint saying so — and
        // the broken half locks the vendor out of its own console while every screen says « code invalide ».
        Add("platform-account-has-totp-or-unenrolled", counts.PlatformAccountsEnrolledWithoutSecret,
            n => n == 0
                ? "0 console account(s) are marked enrolled without a second-factor secret"
                : $"{n} console account(s) are marked as having enrolled a second factor but carry NO secret — "
                  + "they cannot sign in and cannot re-enrol; `platform-account --reset-totp` is the only way back",
            n => n == 0);

        // platform-console Part 2. The counter job survives one cabinet's failure on purpose, so a cabinet
        // skipped every night costs nothing visible — the run logs clean and the console says « jamais mesuré »,
        // which on a fresh deployment is also the honest answer. Only this figure tells the two apart.
        Add("clinic-activity-snapshot-covers-every-clinic", counts.ClinicsWithoutActivitySnapshot,
            n => n == 0
                ? "every cabinet has an activity snapshot"
                : $"{n} cabinet(s) have no activity snapshot — either the nightly pass has not run yet on this "
                  + "deployment, or it has been failing for those cabinets while logging a clean run",
            n => n == 0);

        // The relations one Restate call makes true by construction. A violation is a second writer, and its
        // symptom is a portfolio filtered or sorted on a figure that is quietly wrong rather than an error.
        Add("clinic-activity-snapshot-is-internally-consistent", counts.IncoherentActivitySnapshots,
            n => n == 0
                ? "every activity snapshot's figures agree with each other"
                : $"{n} activity snapshot(s) contradict themselves (7 j above 30 j, more than 30 active days, "
                  + "active days with no saves, or saves with no last-write instant) — they were not written by "
                  + "one ClinicActivitySnapshot.Restate call",
            n => n == 0);

        Add("clinical-child-clinic-matches-patient", counts.ClinicalChildrenWithWrongClinic,
            n => n == 0
                ? "every fiche, document, file, folder, antécédent and tooth state names its patient's clinic"
                : $"{n} clinical row(s) name a clinic that is NOT their patient's — either the backfill did not "
                  + "reach them (they are invisible to their own clinic) or a write path set the wrong clinic",
            n => n == 0);

        // Phase 1 (pre-migration): did every item with a legacy scalar expiry get an opening batch? Once the
        // migration drops StockItems.ExpiryDate this becomes unanswerable, which is why phase 2 exists.
        if (counts.StockItemsWithLegacyExpiryLackingBatch is { } uncovered)
        {
            findings.Add(new SchemaVerificationFinding(
                "Data migrations",
                "stock-batch-backfill",
                uncovered == 0
                    ? $"{counts.StockItemsWithLegacyExpiry} item(s) had a legacy expiry; all have an opening batch"
                    : $"{uncovered} of {counts.StockItemsWithLegacyExpiry} item(s) with a legacy expiry have NO opening batch",
                uncovered == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
        else if (counts.StockItemsWithLegacyExpiry is not null)
        {
            findings.Add(NotApplicable("stock-batch-backfill", "per-batch stock does not exist yet"));
        }
        else
        {
            // Post-migration: StockItems.ExpiryDate is gone, so this question can never be asked again. Say so
            // EXPLICITLY rather than dropping the line — a check that silently disappears from the report is
            // indistinguishable from one that was forgotten, and the whole point of the before/after diff is
            // that every line is accounted for.
            findings.Add(NotApplicable(
                "stock-batch-backfill",
                "the legacy expiry column is gone; superseded by every-stocked-item-has-a-batch"));
        }

        // Phase 2 (post-migration): the durable invariant FEFO depends on. An item holding stock with no lot
        // makes every consume report a full shortfall against stock that is physically on the shelf.
        if (counts.StockItemsWithStockLackingBatch is { } orphanedStock)
        {
            findings.Add(new SchemaVerificationFinding(
                "Data migrations",
                "every-stocked-item-has-a-batch",
                orphanedStock == 0
                    ? "every item holding stock has at least one lot"
                    : $"{orphanedStock} item(s) hold stock with NO lot - FEFO has nothing to draw from",
                orphanedStock == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
        else
        {
            findings.Add(NotApplicable("every-stocked-item-has-a-batch", "per-batch stock does not exist yet"));
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

    // ------------------------------------------------------------------ cabinet entitlements

    /// <summary>
    /// The three <c>clinic-subscription</c> checks (FR-9, FR-13, AC-6.4). Each is a shape no other layer can see:
    /// nothing in the test project touches a database, so « every cabinet has an entitlement » and « the stored date
    /// is its ledger's fold » are structurally invisible until here.
    ///
    /// <para>⚠️ <c>subscription-end-date-matches-ledger</c> calls the <b>real</b>
    /// <see cref="SubscriptionLedger.Fold"/> over rows the reader projected, rather than comparing against a count
    /// SQL computed. A recursive CTE reproducing the exclusive-cursor arithmetic would be a second implementation of
    /// the one thing R-6 exists to keep single — and the copy nothing type-checks.</para>
    /// </summary>
    private static void VerifySubscriptions(SchemaFacts facts, List<SchemaVerificationFinding> findings)
    {
        var counts = facts.DataMigrations;

        AddSubscription("every-clinic-has-an-entitlement", counts.ClinicsWithoutEntitlement,
            n => n == 0
                ? "every cabinet has an entitlement"
                : $"{n} cabinet(s) have NO entitlement — some construction door creates a clinic without one, so "
                  + "they will be refused every write the moment the gate ships",
            n => n == 0);

        if (facts.SubscriptionLedgers is not { } ledgers)
        {
            findings.Add(NotApplicableIn(
                "Cabinet entitlements",
                "subscription-end-date-matches-ledger",
                "the entitlement tables do not exist yet"));
        }
        else
        {
            var drifted = ledgers
                .Where(l => l.StoredEndsOn != SubscriptionLedger.Fold(l.Entries))
                .ToList();

            findings.Add(new SchemaVerificationFinding(
                "Cabinet entitlements",
                "subscription-end-date-matches-ledger",
                drifted.Count == 0
                    ? $"{ledgers.Count} entitlement(s), each ending exactly where its ledger folds to"
                    : $"{drifted.Count} of {ledgers.Count} entitlement(s) store an end date that is NOT their "
                      + "ledger's fold — some write path set EndsOn without going through "
                      + "ClinicSubscription.RecomputeFrom",
                drifted.Count == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }

        // `LatestCoverKind` is a denormalisation of the same fold, so the same argument applies twice over: it is
        // re-derived here with the real SubscriptionLedger rather than re-expressed in SQL, and it is checked at all
        // because a denormalised column and its source can disagree while every layer above reports success — the
        // shape `clinical-child-clinic-matches-patient` exists for. Its visible symptom would be a cabinet dropping
        // out of the console's « en essai » filter, which nobody would notice until a churn review came up empty.
        if (facts.SubscriptionLedgers is { } kindLedgers && facts.SubscriptionCoverKindColumnPresent)
        {
            var mismatched = kindLedgers
                .Where(l => l.StoredLatestCoverKind
                            != SubscriptionLedger.FoldWithSpans(l.Entries).LatestCoverKind)
                .ToList();

            findings.Add(new SchemaVerificationFinding(
                "Cabinet entitlements",
                "subscription-cover-kind-matches-ledger",
                mismatched.Count == 0
                    ? $"{kindLedgers.Count} entitlement(s), each naming the cover its ledger actually folds to"
                    : $"{mismatched.Count} of {kindLedgers.Count} entitlement(s) store a LatestCoverKind that is NOT "
                      + "their ledger's — some write path reached the column without going through "
                      + "ClinicSubscription.RecomputeFrom, or the backfill missed them",
                mismatched.Count == 0 ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
        else
        {
            findings.Add(NotApplicableIn(
                "Cabinet entitlements",
                "subscription-cover-kind-matches-ledger",
                "ClinicSubscriptions.LatestCoverKind does not exist yet"));
        }

        // Info with its count, never asserted — see the DTO's own note on why AC-6.4's equality belongs to FR-9's
        // before/after diff and not to a figure this command can know once new cabinets start arriving.
        AddSubscription("subscription-grandfathered-entries", counts.GrandfatheredEntitlementEntries,
            n => $"{n} cabinet(s) were grandfathered open-ended; compare against the pre-deployment cabinet count",
            _ => true);

        void AddSubscription(string check, int? count, Func<int, string> detail, Func<int, bool> ok)
        {
            if (count is null)
            {
                findings.Add(NotApplicableIn(
                    "Cabinet entitlements", check, "the entitlement tables do not exist yet"));
                return;
            }

            findings.Add(new SchemaVerificationFinding(
                "Cabinet entitlements",
                check,
                detail(count.Value),
                ok(count.Value) ? SchemaVerificationSeverity.Info : SchemaVerificationSeverity.Drift));
        }
    }

    /// <summary>
    /// A check that cannot run yet, in a named section — the one construction of that finding.
    ///
    /// <para>Info, not Drift, on purpose: a part that has not been implemented is not a regression, and making
    /// <c>verify-schema</c> exit non-zero for unbuilt work would train the operator to ignore its exit code, which
    /// is the one thing a gate must not do.</para>
    /// </summary>
    private static SchemaVerificationFinding NotApplicableIn(string scope, string check, string why) =>
        new(scope, check, $"not applicable — {why}", SchemaVerificationSeverity.Info);

    /// <summary>The « Data migrations » case, which is most of them.</summary>
    private static SchemaVerificationFinding NotApplicable(string check, string why) =>
        NotApplicableIn("Data migrations", check, why);
}
