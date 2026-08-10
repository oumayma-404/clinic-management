using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Maintenance;
using Moq;

namespace ClinicManagement.UnitTests.Common.Maintenance;

/// <summary>
/// The <c>verify-schema</c> assertions (plan Testing Strategy). Nothing in this project touches a database, so a
/// migration is the one class of change unit tests structurally cannot verify — this service is the gate for it,
/// and these tests are what keep the gate honest.
///
/// The bar is the same one <c>MoneyReconciliationServiceTests</c> sets: a <b>false clean is worse than no
/// report</b>. Every check therefore gets both directions — the satisfied case reporting Info, and the violated
/// case reporting Drift — because a check that can only ever pass is indistinguishable from no check at all.
/// </summary>
public class SchemaVerificationServiceTests
{
    private readonly Mock<ISchemaVerificationReader> _reader = new();

    private SchemaVerificationService CreateService() => new(_reader.Object);

    /// <summary>A schema where everything agrees. Individual tests override one facet at a time.</summary>
    private void Arrange(
        IReadOnlyList<string>? extensions = null,
        IReadOnlyList<TableConstraintFact>? constraints = null,
        SchemaSide? model = null,
        SchemaSide? database = null,
        IReadOnlyList<MappedDecimalFact>? mappedDecimals = null,
        DataMigrationCounts? counts = null,
        AuditLedgerFacts? auditLedger = null)
    {
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaFacts(
                extensions ?? new[] { "plpgsql", "btree_gist", "unaccent" },
                constraints ?? new[] { PartialExclusionConstraint },
                model ?? EmptySide,
                database ?? EmptySide,
                mappedDecimals ?? Array.Empty<MappedDecimalFact>(),
                counts ?? CleanCounts,
                // Default: the ledger exists and ClinicId is nullable, so the audit checks pass and individual
                // tests override just this facet — the same one-facet-at-a-time shape as every other parameter.
                auditLedger ?? new AuditLedgerFacts(TableExists: true, ClinicIdIsNullable: true)));
    }

    private static SchemaSide EmptySide => new(
        Array.Empty<IndexFact>(), Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>());

    private static TableConstraintFact PartialExclusionConstraint => new(
        "Appointments",
        "EX_Appointments_NoDoubleBooking",
        'x',
        "EXCLUDE USING gist (\"DoctorId\" WITH =, slot WITH &&) WHERE (\"Status\" <> ALL (ARRAY[5, 6]))");

    // Positional because the record is: `PatientsTotal` (the 7th) is a population, not a defect count, so it is
    // the one non-zero entry. The three trailing zeros are platform-console's checks.
    private static DataMigrationCounts CleanCounts =>
        new(0, 0, 0, 0, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    private static SchemaVerificationFinding Finding(SchemaVerificationReport report, string check) =>
        report.Findings.Single(f => f.Check == check);

    private static bool IsDrift(SchemaVerificationFinding finding) =>
        finding.Severity == SchemaVerificationSeverity.Drift;

    // ------------------------------------------------------------------ extensions

    [Fact]
    public async Task A_Schema_That_Matches_The_Model_Reports_No_Drift()
    {
        Arrange();

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.Equal(0, report.DriftCount);
    }

    // btree_gist is what lets one GiST index mix `=` (uuid) with `&&` (range). Without it the exclusion
    // constraint cannot exist at all, so its absence is drift even when every other check passes.
    [Fact]
    public async Task A_Missing_btree_gist_Is_Drift()
    {
        Arrange(extensions: new[] { "plpgsql" });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "btree_gist")));
        Assert.True(report.HasDrift);
    }

    // unaccent backs the free-text search on every paginated list. Its absence does not degrade search, it makes
    // it throw 42883 the first time anyone types in a box — so it has to be caught here rather than in production.
    [Fact]
    public async Task A_Missing_unaccent_Is_Drift()
    {
        Arrange(extensions: new[] { "plpgsql", "btree_gist" });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "unaccent")));
        Assert.True(report.HasDrift);
    }

    // ------------------------------------------------------------------ the booking constraint

    [Fact]
    public async Task A_Missing_Exclusion_Constraint_Is_Drift()
    {
        Arrange(constraints: Array.Empty<TableConstraintFact>());

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "exclusion-constraint");
        Assert.True(IsDrift(finding));
        Assert.Contains("MISSING", finding.Detail);
    }

    /// <summary>
    /// The check that matters most here. A NON-partial constraint is <b>worse than none</b>: it makes a
    /// cancelled slot permanently unbookable, and rebooking a cancelled slot is the most common scheduling
    /// action there is (AC-P1.16). A report that accepted it would certify the schema as correct while the
    /// clinic could no longer rebook.
    /// </summary>
    [Fact]
    public async Task An_Exclusion_Constraint_Without_A_Predicate_Is_Drift()
    {
        Arrange(constraints: new[]
        {
            new TableConstraintFact(
                "Appointments",
                "EX_Appointments_NoDoubleBooking",
                'x',
                "EXCLUDE USING gist (\"DoctorId\" WITH =, slot WITH &&)"),
        });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "exclusion-constraint");
        Assert.True(IsDrift(finding));
        Assert.Contains("NOT PARTIAL", finding.Detail);
    }

    // A constraint of another kind on the same table must not be mistaken for the exclusion constraint —
    // 'x' is the only contype that excludes overlapping rows.
    [Fact]
    public async Task A_Non_Exclusion_Constraint_Does_Not_Satisfy_The_Check()
    {
        Arrange(constraints: new[]
        {
            new TableConstraintFact("Appointments", "PK_Appointments", 'p', "PRIMARY KEY (\"Id\")"),
        });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "exclusion-constraint")));
    }

    // ------------------------------------------------------------------ model vs database

    /// <summary>
    /// The core of the tool: an index the model declares but the database lacks means a migration was never
    /// applied. This is the failure a green build and a green test suite both miss completely.
    /// </summary>
    [Fact]
    public async Task An_Index_In_The_Model_But_Not_The_Database_Is_Drift()
    {
        var index = new IndexFact("Notifications", "IX_Notifications_Status_ScheduledFor",
            new[] { "Status", "ScheduledFor" }, false, null);
        Arrange(
            model: new SchemaSide(new[] { index }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()),
            database: EmptySide);

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "Notifications(Status, ScheduledFor)");
        Assert.True(IsDrift(finding));
        Assert.Contains("MISSING", finding.Detail);
    }

    /// <summary>
    /// Matched on table + ordered columns, deliberately <b>not</b> on name: EF's generated name and a
    /// hand-written migration's name legitimately differ, and it is the covered columns that decide whether a
    /// query is actually served. Naming the check after the name would make every hand-written index read as
    /// missing.
    /// </summary>
    [Fact]
    public async Task An_Index_Is_Matched_By_Columns_Not_By_Name()
    {
        var modelIndex = new IndexFact("StockMovements", "IX_StockMovements_ClinicId",
            new[] { "ClinicId" }, false, null);
        var databaseIndex = new IndexFact("StockMovements", "ix_stockmovements_clinic_handwritten",
            new[] { "ClinicId" }, false, null);

        Arrange(
            model: new SchemaSide(new[] { modelIndex }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()),
            database: new SchemaSide(new[] { databaseIndex }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()));

        var report = await CreateService().RunAsync();

        Assert.False(IsDrift(Finding(report, "StockMovements(ClinicId)")));
    }

    // Column ORDER is part of an index's identity — (A, B) does not serve the queries (B, A) does.
    [Fact]
    public async Task An_Index_With_The_Columns_In_A_Different_Order_Is_Drift()
    {
        var modelIndex = new IndexFact("Notifications", "IX", new[] { "Status", "ScheduledFor" }, false, null);
        var databaseIndex = new IndexFact("Notifications", "IX", new[] { "ScheduledFor", "Status" }, false, null);

        Arrange(
            model: new SchemaSide(new[] { modelIndex }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()),
            database: new SchemaSide(new[] { databaseIndex }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()));

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "Notifications(Status, ScheduledFor)")));
    }

    // An EXTRA index in the database is not drift: a DBA may add one for a slow query, and failing the gate for
    // that would train the operator to ignore its exit code.
    [Fact]
    public async Task An_Extra_Index_In_The_Database_Is_Not_Drift()
    {
        var databaseOnly = new IndexFact("Patients", "ix_dba_added", new[] { "LastName" }, false, null);
        Arrange(
            model: EmptySide,
            database: new SchemaSide(new[] { databaseOnly }, Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>()));

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
    }

    [Fact]
    public async Task A_Foreign_Key_In_The_Model_But_Not_The_Database_Is_Drift()
    {
        var fk = new ForeignKeyFact("StockMovements", new[] { "ClinicId" }, "Clinics");
        Arrange(
            model: new SchemaSide(Array.Empty<IndexFact>(), new[] { fk }, Array.Empty<DecimalColumnFact>()),
            database: EmptySide);

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "StockMovements(ClinicId) -> Clinics");
        Assert.True(IsDrift(finding));
    }

    // ------------------------------------------------------------------ decimal precision

    /// <summary>
    /// § 9.5 / AC-P4.36 — the real defect this found on the live database: <c>StockItem.UnitPrice</c> at
    /// <c>(18,2)</c> silently truncates the millime on every Tunisian price.
    /// </summary>
    [Fact]
    public async Task A_Money_Column_With_Two_Decimals_Is_Drift()
    {
        Arrange(database: new SchemaSide(
            Array.Empty<IndexFact>(),
            Array.Empty<ForeignKeyFact>(),
            new[] { new DecimalColumnFact("StockItems", "UnitPrice", 18, 2) }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "StockItems.UnitPrice");
        Assert.True(IsDrift(finding));
        Assert.Contains("millime", finding.Detail);
    }

    [Fact]
    public async Task A_Money_Column_With_Three_Decimals_Is_Clean()
    {
        Arrange(database: new SchemaSide(
            Array.Empty<IndexFact>(),
            Array.Empty<ForeignKeyFact>(),
            new[] { new DecimalColumnFact("Invoices", "TotalTtc", 18, 3) }));

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
    }

    /// <summary>
    /// AC-P4.38 — the two rate columns keep their own precision on purpose. They are rates, not money, and a
    /// convention that silently widened a VAT rate would be worse than the drift it fixes.
    /// </summary>
    [Theory]
    [InlineData("Clinics")]
    [InlineData("Invoices")]
    public async Task A_Rate_Column_Keeping_Its_Own_Precision_Is_Clean(string table)
    {
        Arrange(database: new SchemaSide(
            Array.Empty<IndexFact>(),
            Array.Empty<ForeignKeyFact>(),
            new[] { new DecimalColumnFact(table, "VatRate", 5, 2) }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, $"{table}.VatRate");
        Assert.False(IsDrift(finding));
        Assert.Contains("rate column", finding.Detail);
    }

    /// <summary>
    /// Drift in the other direction, and the reason the exception list is not just "skip these": a rate WIDENED
    /// to money precision means the convention swallowed an annotation that existed for a reason. Skipping the
    /// column entirely would have let that through silently.
    /// </summary>
    [Fact]
    public async Task A_Rate_Column_Widened_To_Money_Precision_Is_Drift()
    {
        Arrange(database: new SchemaSide(
            Array.Empty<IndexFact>(),
            Array.Empty<ForeignKeyFact>(),
            new[] { new DecimalColumnFact("Invoices", "VatRate", 18, 3) }));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "Invoices.VatRate");
        Assert.True(IsDrift(finding));
        Assert.Contains("must keep its own", finding.Detail);
    }

    /// <summary>
    /// AC-P4.39 — the model side is asserted independently, and this is what makes the precision fix durable: a
    /// contributor re-adding an explicit <c>HasColumnType</c> would match the database once their own migration
    /// applied, so checking the database alone could never notice.
    /// </summary>
    [Fact]
    public async Task A_Mapped_Decimal_With_The_Wrong_Store_Type_Is_Drift()
    {
        Arrange(mappedDecimals: new[]
        {
            new MappedDecimalFact("StockItem", "UnitPrice", "StockItems", "UnitPrice", "numeric(18,2)"),
        });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "StockItem.UnitPrice");
        Assert.True(IsDrift(finding));
    }

    // `decimal(18,3)` and `numeric(18,3)` are the same store type spelled two ways; only the scale matters.
    [Fact]
    public async Task A_Mapped_Decimal_Spelled_decimal_Is_Accepted()
    {
        Arrange(mappedDecimals: new[]
        {
            new MappedDecimalFact("Invoice", "TotalTtc", "Invoices", "TotalTtc", "decimal(18,3)"),
        });

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
    }

    // ------------------------------------------------------------------ data migrations

    // Multi-act séances: the parent's ProcedureTypeId is a DERIVED snapshot of the first AppointmentProcedures
    // row, so a scalar with no row is a visit whose act the edit dialog cannot see — and the first save of that
    // visit would persist the emptiness. Nothing in the test project touches a database, so this diff is the only
    // gate on the migration's backfill actually having covered those rows.
    [Fact]
    public async Task Appointments_Naming_An_Act_With_No_Procedure_Row_Are_Drift()
    {
        Arrange(counts: CleanCounts with { AppointmentsWithActScalarLackingRow = 4 });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "appointment-act-rows")));
    }

    // Before the migration applies there is no child table to count, so the line must read « not applicable »
    // rather than 0 — a 0 would claim a backfill succeeded that has not run.
    [Fact]
    public async Task Appointment_Act_Rows_Reads_Not_Applicable_Before_The_Table_Exists()
    {
        Arrange(counts: CleanCounts with { AppointmentsWithActScalarLackingRow = null });

        var report = await CreateService().RunAsync();

        Assert.False(IsDrift(Finding(report, "appointment-act-rows")));
    }

    [Fact]
    public async Task Appointment_Notes_Still_Carrying_A_Type_Prefix_Are_Drift()
    {
        Arrange(counts: CleanCounts with { AppointmentsWithTypePrefixRemaining = 3 });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "type-prefix-removed")));
    }

    [Fact]
    public async Task Pre_Existing_Overlapping_Pairs_Are_Drift()
    {
        Arrange(counts: CleanCounts with { OverlappingAppointmentPairs = 2 });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "overlapping-appointment-pairs");
        Assert.True(IsDrift(finding));
        Assert.Contains("cannot be installed", finding.Detail);
    }

    /// <summary>
    /// The failure this count exists for: the schema change landed but the backfill covered nothing, so those
    /// items' expiry dates were dropped. A green migration and a green build both look identical.
    /// </summary>
    [Fact]
    public async Task A_Legacy_Expiry_With_No_Opening_Batch_Is_Drift()
    {
        Arrange(counts: CleanCounts with { StockItemsWithLegacyExpiry = 5, StockItemsWithLegacyExpiryLackingBatch = 5 });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "stock-batch-backfill");
        Assert.True(IsDrift(finding));
        Assert.Contains("NO opening batch", finding.Detail);
    }

    [Fact]
    public async Task Patients_Missing_A_Normalized_Name_Are_Drift()
    {
        Arrange(counts: CleanCounts with { PatientsMissingNormalizedName = 4 });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "normalized-name-populated")));
    }

    /// <summary>
    /// A check whose subject does not exist yet reports <b>not applicable</b>, not drift. Two reasons: work that
    /// has not been implemented is not a regression, and a gate that exits non-zero for unbuilt parts trains the
    /// operator to ignore its exit code — the one thing a gate must never do. Reporting 0 instead would be
    /// worse still: it would claim a backfill succeeded when it never ran.
    /// </summary>
    [Fact]
    public async Task A_Count_Whose_Subject_Does_Not_Exist_Yet_Is_Not_Applicable_Rather_Than_Drift()
    {
        // Every part that has not run yet reports « not applicable » rather than a misleading 0. Spelled out as
        // named overrides because *which* facets are null is the whole assertion.
        Arrange(counts: CleanCounts with
        {
            StockItemsWithLegacyExpiry = null,
            StockItemsWithLegacyExpiryLackingBatch = null,
            StockItemsWithStockLackingBatch = null,
            PatientsMissingNormalizedName = null,
            PatientsTotal = null,
            AppointmentsWithActScalarLackingRow = null,
            ProcedureTypesWithCategoryStillInDescription = null,
            PaymentsWithChequeDetailsOnNonCheque = null,
            PaymentsWithBankedStampOnNonCheque = null,
            PushDeliveriesWithMismatchedClinic = null,
            ClinicSignupOrphans = null,
        });

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.Contains("not applicable", Finding(report, "stock-batch-backfill").Detail);
        Assert.Contains("not applicable", Finding(report, "every-stocked-item-has-a-batch").Detail);
        Assert.Contains("not applicable", Finding(report, "normalized-name-populated").Detail);
    }

    /// <summary>
    /// Group B's stamp columns, before its migration has run. The guard is on <c>ChequeBankedOn</c> and NOT on
    /// L8's <c>ChequeNumber</c>, which is the whole reason this case is worth pinning: a database carrying L8 and
    /// not Group B must read « not applicable », because a reassuring <c>0</c> there would claim an invariant was
    /// verified over columns that do not exist.
    /// </summary>
    [Fact]
    public async Task The_Banked_Stamp_Invariant_Is_Not_Applicable_Before_Its_Own_Migration()
    {
        // L8 present (its own count is a real 0), Group B absent.
        Arrange(counts: CleanCounts with { PaymentsWithBankedStampOnNonCheque = null });

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.Contains("not applicable", Finding(report, "cheque-banked-only-on-cheques").Detail);
        Assert.DoesNotContain("not applicable", Finding(report, "cheque-details-only-on-cheques").Detail);
    }

    /// <summary>
    /// A banked stamp on a cash payment means a write path reached the columns without passing
    /// <c>ChequeBankedStamp.For</c> — the one thing about these columns the EF model cannot state, and the reason
    /// the invariant is verified here instead of being duplicated as a CHECK constraint.
    /// </summary>
    [Fact]
    public async Task A_Banked_Stamp_On_A_Non_Cheque_Payment_Is_Drift()
    {
        Arrange(counts: CleanCounts with { PaymentsWithBankedStampOnNonCheque = 2 });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "cheque-banked-only-on-cheques");
        Assert.True(IsDrift(finding));
        Assert.Contains("ChequeBankedStamp.For", finding.Detail);
    }

    /// <summary>
    /// The post-migration state the live database is in once the batch migration has run: the legacy expiry
    /// column is gone, so the original backfill question is unanswerable forever and the durable invariant takes
    /// over. The retired line must still APPEAR, saying it was superseded — a check that silently vanishes from
    /// the report is indistinguishable from one that was forgotten, which defeats the before/after diff.
    /// </summary>
    [Fact]
    public async Task After_The_Migration_The_Backfill_Check_Says_It_Was_Superseded()
    {
        Arrange(counts: CleanCounts with { StockItemsWithLegacyExpiry = null, StockItemsWithLegacyExpiryLackingBatch = null });

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.Contains("superseded", Finding(report, "stock-batch-backfill").Detail);
        Assert.Contains("at least one lot", Finding(report, "every-stocked-item-has-a-batch").Detail);
    }

    /// <summary>
    /// The durable invariant FEFO depends on: an item holding stock with no lot makes every consume report a
    /// full shortfall against stock that is physically on the shelf.
    /// </summary>
    [Fact]
    public async Task An_Item_Holding_Stock_With_No_Lot_Is_Drift()
    {
        Arrange(counts: CleanCounts with { StockItemsWithLegacyExpiry = null, StockItemsWithLegacyExpiryLackingBatch = null, StockItemsWithStockLackingBatch = 3 });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "every-stocked-item-has-a-batch");
        Assert.True(IsDrift(finding));
        Assert.Contains("FEFO has nothing to draw from", finding.Detail);
    }

    // ------------------------------------------------------------------ the report contract

    // The verb's exit code is driven by HasDrift, so the two must agree or a drifted schema exits 0.
    [Fact]
    public async Task HasDrift_Agrees_With_DriftCount()
    {
        Arrange(counts: CleanCounts with { AppointmentsWithTypePrefixRemaining = 1, OverlappingAppointmentPairs = 1 });

        var report = await CreateService().RunAsync();

        Assert.True(report.HasDrift);
        Assert.Equal(2, report.DriftCount);
    }

    // Read-only: the service must never be handed a way to mutate, and must ask its reader exactly once.
    [Fact]
    public async Task The_Report_Reads_Once_And_Writes_Nothing()
    {
        Arrange();

        await CreateService().RunAsync();

        _reader.Verify(r => r.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
        _reader.VerifyNoOtherCalls();
    }
    // ---------------------------------------------------------------- I6: the audit ledger

    /// <summary>
    /// [I6] A healthy ledger reports both checks clean.
    ///
    /// <para>Only two checks, deliberately: <c>AuditEntryConfiguration</c> declares both of the table's indexes,
    /// so the model-driven diff already verifies them and naming them here would rebuild the hand-maintained
    /// expectation list this whole verb exists to avoid.</para>
    /// </summary>
    [Fact]
    public async Task A_Healthy_Audit_Ledger_Reports_Both_Checks_Clean()
    {
        Arrange();

        var report = await CreateService().RunAsync();

        Assert.False(IsDrift(Finding(report, "audit-ledger-clinic-nullable")));
        Assert.False(IsDrift(Finding(report, "audit-ledger-has-no-foreign-keys")));
    }

    /// <summary>
    /// [I6][DEV-4] A <c>NOT NULL</c> <c>ClinicId</c> is drift, and this is the check's whole reason for existing.
    ///
    /// <para>The failure it catches is silent: a job or console verb mutating a row with no clinic derivable from
    /// it writes the audit row with a null clinic, and if the column were tightened that insert would throw
    /// <b>inside the interceptor's own swallow-and-log</b>. The ledger would simply stop recording every
    /// non-interactive mutation, with nothing on any screen to say so — and no unit test could see it, because
    /// nothing in this suite touches a database.</para>
    /// </summary>
    [Fact]
    public async Task A_Not_Null_Audit_ClinicId_Is_Drift()
    {
        Arrange(auditLedger: new AuditLedgerFacts(TableExists: true, ClinicIdIsNullable: false));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "audit-ledger-clinic-nullable");
        Assert.True(IsDrift(finding));
        Assert.Contains("NOT NULL", finding.Detail);
    }

    /// <summary>
    /// [I6] A foreign key on <c>AuditEntries</c> is drift — the one assertion in the whole report that looks for
    /// something <b>absent</b>.
    ///
    /// <para>It is needed because the model-to-database FK diff only reports a <i>missing</i> key and can never
    /// see an <i>extra</i> one. A well-meaning <c>ClinicId -&gt; Clinics ON DELETE CASCADE</c> would erase a
    /// clinic's audit history along with the clinic, which is the one thing a ledger must never do.</para>
    /// </summary>
    [Fact]
    public async Task A_Foreign_Key_On_The_Audit_Table_Is_Drift()
    {
        var database = new SchemaSide(
            Array.Empty<IndexFact>(),
            new[] { new ForeignKeyFact("AuditEntries", new[] { "ClinicId" }, "Clinics") },
            Array.Empty<DecimalColumnFact>());

        Arrange(database: database);

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "audit-ledger-has-no-foreign-keys");
        Assert.True(IsDrift(finding));
        Assert.Contains("AuditEntries(ClinicId) -> Clinics", finding.Detail);
    }

    // A foreign key on some OTHER table is none of this check's business — it must not fire on the ~40 legitimate
    // ones the schema already has.
    [Fact]
    public async Task A_Foreign_Key_On_Another_Table_Does_Not_Trip_The_Audit_Check()
    {
        var fk = new ForeignKeyFact("Expenses", new[] { "ClinicId" }, "Clinics");
        var side = new SchemaSide(
            Array.Empty<IndexFact>(), new[] { fk }, Array.Empty<DecimalColumnFact>());

        Arrange(model: side, database: side);

        var report = await CreateService().RunAsync();

        Assert.False(IsDrift(Finding(report, "audit-ledger-has-no-foreign-keys")));
    }

    /// <summary>
    /// [I6] Before the migration is applied, both checks report « not applicable » — <b>named, not dropped</b>.
    ///
    /// <para>Same rule as the stock-batch phases: a check that silently vanishes from the report is
    /// indistinguishable from one that was forgotten, and the whole before/after-and-diff workflow depends on
    /// every line being accounted for.</para>
    /// </summary>
    [Fact]
    public async Task Before_The_Migration_Both_Audit_Checks_Are_Named_As_Not_Applicable()
    {
        Arrange(auditLedger: new AuditLedgerFacts(TableExists: false, ClinicIdIsNullable: null));

        var report = await CreateService().RunAsync();

        foreach (var check in new[] { "audit-ledger-clinic-nullable", "audit-ledger-has-no-foreign-keys" })
        {
            var finding = Finding(report, check);
            Assert.False(IsDrift(finding));
            Assert.Contains("not applicable", finding.Detail);
        }
    }

    // The findings are filed under their own section, so the operator diff groups them rather than scattering
    // them through « Data migrations ».
    [Fact]
    public async Task The_Audit_Findings_Are_Filed_Under_Their_Own_Section()
    {
        Arrange();

        var report = await CreateService().RunAsync();

        Assert.Equal("Audit ledger", Finding(report, "audit-ledger-clinic-nullable").Scope);
        Assert.Equal("Audit ledger", Finding(report, "audit-ledger-has-no-foreign-keys").Scope);
    }

    // L4a's backfill, and the same quiet failure as the category move above: EF's differ scaffolds
    // `defaultValue: 0` for a new non-nullable int, so a clinic left at zero has a retention policy of
    // « keep nothing » and a staleness threshold that fires immediately — while the columns, the endpoint and
    // the settings screen are all present and correct. Only a database read can see it.
    [Fact]
    public async Task Clinics_Left_With_A_Zero_Backup_Retention_Are_Drift()
    {
        Arrange(counts: CleanCounts with { ClinicsWithUnsetBackupSchedule = 3 });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "backup-schedule-backfill")));
    }

    // Before the columns exist the question cannot be asked, so the line must read « not applicable » rather
    // than a reassuring 0 — the distinction the whole nullable-count convention in this file exists for.
    [Fact]
    public async Task Backup_Schedule_Backfill_Reads_Not_Applicable_Before_The_Columns_Exist()
    {
        Arrange(counts: CleanCounts with { ClinicsWithUnsetBackupSchedule = null });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "backup-schedule-backfill");
        Assert.False(IsDrift(finding));
        Assert.Contains("not applicable", finding.Detail, StringComparison.OrdinalIgnoreCase);
    }

    // Part 6 — the one relationship in the push tables no constraint can state, because the two clinic ids live in
    // different tables. A mismatch is a cross-clinic notification, and a lock screen has no request-time check left
    // to stop one; the dispatcher's own comparison is the last line of defence, so a non-zero count here means a
    // write path already produced what it exists to catch.
    [Fact]
    public async Task Queued_Pushes_Naming_A_Different_Clinic_From_Their_Device_Are_Drift()
    {
        Arrange(counts: CleanCounts with { PushDeliveriesWithMismatchedClinic = 2 });

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "push-delivery-clinic-matches-device")));
    }

    [Fact]
    public async Task Push_Clinic_Match_Reads_Not_Applicable_Before_The_Tables_Exist()
    {
        Arrange(counts: CleanCounts with { PushDeliveriesWithMismatchedClinic = null });

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "push-delivery-clinic-matches-device");
        Assert.False(IsDrift(finding));
        Assert.Contains("not applicable", finding.Detail, StringComparison.OrdinalIgnoreCase);
    }

}
