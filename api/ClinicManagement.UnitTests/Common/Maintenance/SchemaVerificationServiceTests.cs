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
        DataMigrationCounts? counts = null)
    {
        _reader
            .Setup(r => r.ReadAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SchemaFacts(
                extensions ?? new[] { "plpgsql", "btree_gist" },
                constraints ?? new[] { PartialExclusionConstraint },
                model ?? EmptySide,
                database ?? EmptySide,
                mappedDecimals ?? Array.Empty<MappedDecimalFact>(),
                counts ?? CleanCounts));
    }

    private static SchemaSide EmptySide => new(
        Array.Empty<IndexFact>(), Array.Empty<ForeignKeyFact>(), Array.Empty<DecimalColumnFact>());

    private static TableConstraintFact PartialExclusionConstraint => new(
        "Appointments",
        "EX_Appointments_NoDoubleBooking",
        'x',
        "EXCLUDE USING gist (\"DoctorId\" WITH =, slot WITH &&) WHERE (\"Status\" <> ALL (ARRAY[5, 6]))");

    private static DataMigrationCounts CleanCounts => new(0, 0, 0, 0, 0, 12);

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

    [Fact]
    public async Task Appointment_Notes_Still_Carrying_A_Type_Prefix_Are_Drift()
    {
        Arrange(counts: new DataMigrationCounts(3, 0, 0, 0, 0, 12));

        var report = await CreateService().RunAsync();

        Assert.True(IsDrift(Finding(report, "type-prefix-removed")));
    }

    [Fact]
    public async Task Pre_Existing_Overlapping_Pairs_Are_Drift()
    {
        Arrange(counts: new DataMigrationCounts(0, 2, 0, 0, 0, 12));

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
        Arrange(counts: new DataMigrationCounts(0, 0, 5, 5, 0, 12));

        var report = await CreateService().RunAsync();

        var finding = Finding(report, "stock-batch-backfill");
        Assert.True(IsDrift(finding));
        Assert.Contains("NO opening batch", finding.Detail);
    }

    [Fact]
    public async Task Patients_Missing_A_Normalized_Name_Are_Drift()
    {
        Arrange(counts: new DataMigrationCounts(0, 0, 0, 0, 4, 12));

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
        Arrange(counts: new DataMigrationCounts(0, 0, null, null, null, null));

        var report = await CreateService().RunAsync();

        Assert.False(report.HasDrift);
        Assert.Contains("not applicable", Finding(report, "stock-batch-backfill").Detail);
        Assert.Contains("not applicable", Finding(report, "normalized-name-populated").Detail);
    }

    // ------------------------------------------------------------------ the report contract

    // The verb's exit code is driven by HasDrift, so the two must agree or a drifted schema exits 0.
    [Fact]
    public async Task HasDrift_Agrees_With_DriftCount()
    {
        Arrange(counts: new DataMigrationCounts(1, 1, 0, 0, 0, 12));

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
}
