using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Messaging;
using ClinicManagement.UnitTests.Features.Platform;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The <c>messaging-report</c> verb's core (<c>vendor-whatsapp-messaging-quota</c> AC-8.6, AC-9.4).
///
/// <para><b>The exit code is what this class is really about.</b> A report that quietly stops alarming reads exactly like
/// a clean run — the lesson <c>SubscriptionReportServiceTests</c> records — so every case here asserts the <i>bucket</i>
/// alongside the figures, and the single-cabinet mode's verdict is asserted to come from the same classification the
/// deployment-wide run uses rather than being re-derived in the verb.</para>
///
/// <para>⚠️ <b>Its month keys are fixed literals</b>, unlike <c>MessagingVendorCommandTests</c>' — and deliberately: this
/// service takes the month as a parameter and reads no clock, which is exactly what
/// <see cref="A_Closed_Month_Still_Answers"/> pins. A fixture anchored on « now » could not distinguish « answers for the
/// month asked about » from « answers for today ».</para>
/// </summary>
public class MessagingReportServiceTests
{
    private const string July = "2026-07";
    private const string August = "2026-08";

    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Recorded = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeMessagingAllowanceRepository _allowances = new();

    private MessagingReportService Service() =>
        new(_allowances, PlatformMessagingReadStubs.NoReminderSettings());

    private void GivenCabinet(
        Guid clinicId, string name, int? standing, string monthKey, int? allowance = null, int consumed = 0)
    {
        if (standing is { } messages)
        {
            _allowances.Seed(MessagingAllowanceEntry.Provisioned(clinicId, messages, monthKey, Recorded));
        }

        ClinicMessagingMonth? month = null;
        if (allowance is { } cap)
        {
            month = _allowances.SeedMonth(ClinicMessagingMonth.For(clinicId, monthKey, cap, Recorded));
            for (var i = 0; i < consumed; i++)
            {
                month.RecordSend(Recorded);
            }
        }

        _allowances.ReportRows.Add(new ClinicMessagingReportRow(clinicId, name, month));
    }

    // [AC-9.4] A cabinet out of messages is a finding, and the verb exits 2. This is money the practice is losing right
    // now — its patients are not being warned — so it leads the report.
    [Fact]
    public async Task An_Exhausted_Cabinet_Is_A_Finding()
    {
        GivenCabinet(ClinicA, "Cabinet Ben Ali", standing: 200, August, allowance: 200, consumed: 200);

        var report = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        var line = Assert.Single(report.Exhausted);
        Assert.Equal(MessagingReportBucket.Exhausted, line.Bucket);
        Assert.Equal(200, line.Allowance);
        Assert.Equal(200, line.Consumed);
        Assert.Equal(0, line.Remaining);
        Assert.True(report.NeedsAttention);
    }

    // [AC-9.4] « Aucun forfait » is its OWN finding, distinct from « épuisé »: there is nothing the practice could have
    // spent, so telling the vendor to top it up would skip the question of how it came to have none (AC-4.3's shape).
    [Fact]
    public async Task A_Cabinet_With_No_Allowance_Record_Is_Its_Own_Finding()
    {
        GivenCabinet(ClinicA, "Cabinet sans forfait", standing: null, August, allowance: 200);

        var report = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        var line = Assert.Single(report.NoAllowance);
        Assert.Equal(MessagingReportBucket.NoAllowance, line.Bucket);

        // Null, not zero: the two are opposite facts and the report must not collapse them.
        Assert.Null(line.Allowance);
        Assert.Empty(report.Exhausted);
        Assert.True(report.NeedsAttention);
    }

    // [AC-8.3][FR-1a] « Non mesuré » is a third finding: an allocation exists but the daily pass has written no counting
    // row, so nothing is metering that cabinet. Keying the report off the counting table would have made this the one
    // state it could not show — the opposite of what a safety net is for.
    [Fact]
    public async Task A_Cabinet_With_No_Counting_Row_Is_Reported_As_Unmeasured()
    {
        GivenCabinet(ClinicA, "Cabinet non mesuré", standing: 200, August, allowance: null);

        var report = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        var line = Assert.Single(report.Unmeasured);
        Assert.Equal(MessagingReportBucket.Unmeasured, line.Bucket);

        // The forfait still reads — it comes from the ledger — which is what tells the vendor « we owe them 200 and
        // nothing is counting » rather than « they have nothing ».
        Assert.Equal(200, line.Allowance);
        Assert.Null(line.Consumed);
        Assert.True(report.NeedsAttention);
    }

    // A quiet cabinet with room left is not a finding, and the verb exits 0. Without this case every other one here is
    // satisfied by a report that alarms unconditionally — which is an alarm nobody reads.
    [Fact]
    public async Task A_Healthy_Cabinet_Is_Not_A_Finding()
    {
        GivenCabinet(ClinicA, "Cabinet tranquille", standing: 200, August, allowance: 200, consumed: 12);

        var report = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        Assert.Single(report.Healthy);
        Assert.False(report.NeedsAttention);
    }

    // A month with a row reading zero sent is « 0 », never « non mesuré » — a fact about the practice, not about us.
    [Fact]
    public async Task A_Quiet_Month_Reads_Zero_Rather_Than_Unmeasured()
    {
        GivenCabinet(ClinicA, "Cabinet silencieux", standing: 200, August, allowance: 200, consumed: 0);

        var report = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        var line = Assert.Single(report.Healthy);
        Assert.Equal(0, line.Consumed);
        Assert.NotNull(line.Consumed);
        Assert.Empty(report.Unmeasured);
    }

    // ── The month is a parameter, and that is the whole point of --month ───────────────────────────────────────────
    //
    // [AC-9.4] The report answers for a CLOSED month, which is when the vendor reconciles against Meta's bill. A service
    // that read the clock could not do this at all: the same ledger and the same rows produce a different answer for July
    // than for August, and the July figure is the one that has to be invoiceable in August.
    [Fact]
    public async Task A_Closed_Month_Still_Answers()
    {
        // A cabinet whose July was exhausted and whose August has room: one ledger, two truthful answers.
        _allowances.Seed(MessagingAllowanceEntry.Provisioned(ClinicA, 200, July, Recorded));

        var julyRow = _allowances.SeedMonth(ClinicMessagingMonth.For(ClinicA, July, 200, Recorded));
        for (var i = 0; i < 200; i++)
        {
            julyRow.RecordSend(Recorded);
        }

        var augustRow = _allowances.SeedMonth(ClinicMessagingMonth.For(ClinicA, August, 200, Recorded));
        augustRow.RecordSend(Recorded);

        // GetForReportAsync is keyed on the month by the real repository; the fake serves whatever the fixture staged, so
        // each run is given the row for the month it is about.
        _allowances.ReportRows.Clear();
        _allowances.ReportRows.Add(new ClinicMessagingReportRow(ClinicA, "Cabinet Ben Ali", julyRow));

        var july = await Service().RunAsync(July, ClinicClock.MonthLabelFr(July));

        Assert.Equal(July, july.MonthKey);
        Assert.Single(july.Exhausted);
        Assert.True(july.NeedsAttention);

        _allowances.ReportRows.Clear();
        _allowances.ReportRows.Add(new ClinicMessagingReportRow(ClinicA, "Cabinet Ben Ali", augustRow));

        var august = await Service().RunAsync(August, ClinicClock.MonthLabelFr(August));

        Assert.Equal(August, august.MonthKey);
        Assert.Single(august.Healthy);
        Assert.False(august.NeedsAttention);
    }

    // The single-cabinet mode's verdict comes from the SAME classification the deployment-wide run buckets on — not
    // re-derived in the verb over the figures, which is how two implementations of « must the vendor act on this? » come
    // to agree only by coincidence.
    [Fact]
    public async Task The_Single_Cabinet_Mode_Shares_The_Deployment_Wide_Verdict()
    {
        GivenCabinet(ClinicA, "Cabinet épuisé", standing: 200, August, allowance: 200, consumed: 200);
        GivenCabinet(ClinicB, "Cabinet tranquille", standing: 500, August, allowance: 500, consumed: 5);

        var service = Service();

        var exhausted = await service.RunForCabinetAsync(ClinicA, August, ClinicClock.MonthLabelFr(August));
        var healthy = await service.RunForCabinetAsync(ClinicB, August, ClinicClock.MonthLabelFr(August));

        Assert.NotNull(exhausted);
        Assert.Equal(MessagingReportBucket.Exhausted, exhausted!.Cabinet.Bucket);
        Assert.True(exhausted.NeedsAttention);

        Assert.NotNull(healthy);
        Assert.Equal(MessagingReportBucket.Healthy, healthy!.Cabinet.Bucket);
        Assert.False(healthy.NeedsAttention);
    }

    // The single-cabinet mode prints the allocation ids `messaging-cancel` takes — the only place in the product that
    // does, so without it a mis-keyed forfait older than the current console session would be uncorrectable.
    [Fact]
    public async Task The_Single_Cabinet_Mode_Lists_The_Allocations_Behind_The_Figure()
    {
        GivenCabinet(ClinicA, "Cabinet Ben Ali", standing: 200, August, allowance: 500, consumed: 10);

        var topUp = _allowances.Seed(MessagingAllowanceEntry.Create(
            ClinicA, MessagingAllowanceKind.TopUp, 300, August, Recorded.AddDays(1), amount: 45.000m));

        var cancelled = _allowances.Seed(MessagingAllowanceEntry.Create(
            ClinicA, MessagingAllowanceKind.TopUp, 999, August, Recorded.AddDays(2)));
        cancelled.Cancel("Erreur de cabinet", "console|abc", Recorded.AddDays(3));

        var report = await Service().RunForCabinetAsync(ClinicA, August, ClinicClock.MonthLabelFr(August));

        Assert.NotNull(report);
        Assert.Equal(3, report!.Ledger.Count);
        Assert.Contains(report.Ledger, e => e.EntryId == topUp.Id && e.Amount == 45.000m);

        // A cancelled allocation is LISTED and marked, never hidden: a history that tidied them away would answer
        // « what were we paid, and for what? » with a curated version of the truth.
        var struckThrough = Assert.Single(report.Ledger, e => e.IsCancelled);
        Assert.Equal(cancelled.Id, struckThrough.EntryId);
        Assert.Equal("Erreur de cabinet", struckThrough.CancelReason);
    }

    // An unknown cabinet is null, so the verb can say « aucun cabinet » and exit 1 rather than printing an empty report
    // that reads as « rien à signaler ».
    [Fact]
    public async Task An_Unknown_Cabinet_Reports_Nothing_At_All()
    {
        GivenCabinet(ClinicA, "Cabinet Ben Ali", standing: 200, August, allowance: 200);

        var report = await Service().RunForCabinetAsync(ClinicB, August, ClinicClock.MonthLabelFr(August));

        Assert.Null(report);
    }

    // [FR-7b] A template category is a finding only when it is present AND not UTILITY. Nothing stores one until Part 4,
    // so a null must not alarm — otherwise this verb would exit 2 for every cabinet of the deployment for two parts,
    // which is an alarm nobody reads.
    [Theory]
    [InlineData(null, MessagingReportBucket.Healthy)]
    [InlineData("UTILITY", MessagingReportBucket.Healthy)]
    [InlineData("utility", MessagingReportBucket.Healthy)]
    [InlineData("MARKETING", MessagingReportBucket.TemplateNotUtility)]
    public void A_Template_Category_Is_A_Finding_Only_When_It_Has_Moved(string? category, MessagingReportBucket expected)
    {
        var month = ClinicMessagingMonth.For(ClinicA, August, 200, Recorded);

        Assert.Equal(expected, MessagingReportService.Classify(200, month, category));
    }

    // The classification order is the design: « aucun forfait » is asked before « épuisé », because a cabinet with no
    // forfait on record cannot meaningfully be out of one — there was nothing it was allowed to spend.
    [Fact]
    public void No_Allowance_Outranks_Exhausted()
    {
        var spent = ClinicMessagingMonth.For(ClinicA, August, 0, Recorded);
        Assert.True(spent.IsExhausted);

        Assert.Equal(
            MessagingReportBucket.NoAllowance,
            MessagingReportService.Classify(allowance: null, spent, templateCategory: null));
    }
}
