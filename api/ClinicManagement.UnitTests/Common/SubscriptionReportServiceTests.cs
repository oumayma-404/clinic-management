using ClinicManagement.Application.Common.Maintenance;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The vendor's read-only report (<c>clinic-subscription</c> Part F — AC-5.9), and what makes its verb exit
/// <b>2</b>.
///
/// <para><b>The load-bearing case is <see cref="A_Suspended_Cabinet_Is_Listed_But_Does_Not_Count_As_A_Finding"/>.</b>
/// The report is meant to be scheduled, and everything about it is worthless if it always alarms: suspension is a
/// decision the vendor already made, so counting it would leave a nightly run permanently at exit 2 with nothing to
/// act on — and an alarm that is always on is one nobody reads. Its exact counterpart is
/// <see cref="A_Cabinet_With_No_Entitlement_Is_Its_Own_Group_And_Does_Count"/>: that one is FR-13's failure state,
/// a defect rather than a state anyone chose, and folding it into « expired » would describe it as an ordinary
/// lapse.</para>
///
/// <para>⚠️ <b>Today is a parameter</b>, which is what lets these fixtures sit at fixed literals — unlike the three
/// command classes, whose handlers stamp a recorded day from the real clock.</para>
/// </summary>
public class SubscriptionReportServiceTests
{
    private static readonly DateTime Today = new(2026, 8, 10);
    private static readonly DateTime BaseUtc = new(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc);

    private sealed class Deployment
    {
        public FakeSubscriptionRepository Repository { get; } = new();

        private int _next = 1;

        /// <summary>A cabinet whose entitlement ends on a chosen day, through the fold rather than by assignment.</summary>
        public Guid WithCabinet(string name, DateTime? endsOn, bool suspended = false)
        {
            var clinicId = Guid.Parse($"{_next++:D8}-0000-0000-0000-000000000000");
            var subscription = ClinicSubscription.For(clinicId, BaseUtc);

            if (endsOn is { } day)
            {
                subscription.RecomputeFrom(new[]
                {
                    SubscriptionPeriod.Create(
                        clinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc, explicitEndsOn: day),
                });
            }
            else
            {
                subscription.RecomputeFrom(new[]
                {
                    SubscriptionPeriod.OpenEnded(clinicId, SubscriptionPeriodKind.Grandfathered, Today, BaseUtc),
                });
            }

            if (suspended)
            {
                subscription.Suspend("Usage frauduleux signalé", "job|subscription-suspend", BaseUtc);
            }

            Repository.ReportRows.Add(new ClinicSubscriptionReportRow(clinicId, name, subscription));
            return clinicId;
        }

        /// <summary>FR-13's failure state: a cabinet with no entitlement row at all.</summary>
        public Guid WithCabinetLackingAnEntitlement(string name)
        {
            var clinicId = Guid.Parse($"{_next++:D8}-0000-0000-0000-000000000000");
            Repository.ReportRows.Add(new ClinicSubscriptionReportRow(clinicId, name, null));
            return clinicId;
        }

        public SubscriptionReportService Service() => new(Repository);
    }

    // [AC-5.9] The two groups AC-5.9 names, each holding the cabinet it should.
    [Fact]
    public async Task Cabinets_Are_Grouped_By_Expiring_Expired_And_Nothing_To_Do()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Expire dans 3 jours", Today.AddDays(3));
        deployment.WithCabinet("Expiré la semaine dernière", Today.AddDays(-7));
        deployment.WithCabinet("Payé jusqu'en 2027", Today.AddMonths(12));
        deployment.WithCabinet("Sans échéance", null);

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Equal(4, report.TotalCabinets);
        Assert.Single(report.Expiring);
        Assert.Single(report.Expired);
        Assert.Equal(2, report.Healthy.Count);
        Assert.Empty(report.Suspended);
        Assert.Empty(report.WithoutEntitlement);
        Assert.True(report.NeedsAttention);
    }

    // [FR-1] « Expiring » is inclusive of the last working day and of the threshold itself. A cabinet whose date is
    // today has 0 days remaining and can still work all of today, so it belongs in the group being acted on.
    [Theory]
    [InlineData(0, true)]
    [InlineData(7, true)]
    [InlineData(8, false)]
    public async Task The_Expiring_Window_Is_Inclusive_On_Both_Ends(int daysAway, bool expiring)
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Cabinet", Today.AddDays(daysAway));

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Equal(expiring ? 1 : 0, report.Expiring.Count);
        Assert.Equal(expiring, report.NeedsAttention);
    }

    // [FR-7] Listed, so the vendor can see it — but not a finding, or a scheduled report never returns 0 again for
    // a deployment holding one deliberately suspended cabinet.
    [Fact]
    public async Task A_Suspended_Cabinet_Is_Listed_But_Does_Not_Count_As_A_Finding()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Suspendu", Today.AddMonths(6), suspended: true);

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Single(report.Suspended);
        Assert.Equal("Suspendu", report.Suspended[0].StateLabel);
        Assert.Equal("Usage frauduleux signalé", report.Suspended[0].SuspensionReason);
        Assert.False(report.Suspended[0].AllowsWrites);
        Assert.False(report.NeedsAttention);
    }

    // [EC-11] A suspended cabinet whose date has ALSO passed is still reported as suspended, never as expired —
    // the same precedence the cabinet's own screen applies, because both read one rule.
    [Fact]
    public async Task A_Suspended_And_Lapsed_Cabinet_Is_Reported_Suspended_Not_Expired()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Suspendu et périmé", Today.AddDays(-30), suspended: true);

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Single(report.Suspended);
        Assert.Empty(report.Expired);
    }

    // [FR-13] Its own group, and it does count. « Aucun abonnement » is what the gate refuses such a cabinet under
    // (subscription_missing), so the report says the same thing rather than inventing a state.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Its_Own_Group_And_Does_Count()
    {
        var deployment = new Deployment();
        deployment.WithCabinetLackingAnEntitlement("Sans abonnement");

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Single(report.WithoutEntitlement);
        Assert.Null(report.WithoutEntitlement[0].State);
        Assert.False(report.WithoutEntitlement[0].AllowsWrites);
        Assert.Empty(report.Expired);
        Assert.Empty(report.Healthy);
        Assert.True(report.NeedsAttention);
    }

    // A clean deployment is the case that has to be able to return 0, or the exit code carries no information.
    [Fact]
    public async Task A_Deployment_With_Nothing_To_Do_Reports_No_Findings()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Payé", Today.AddMonths(12));
        deployment.WithCabinet("Sans échéance", null);

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.False(report.NeedsAttention);
        Assert.Equal(2, report.Healthy.Count);
    }

    // Soonest first inside each group: the cabinet that stops working first is the one to act on, and an operator
    // reads the top of a list.
    [Fact]
    public async Task Expiring_Cabinets_Are_Listed_Soonest_First()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Dans 6 jours", Today.AddDays(6));
        deployment.WithCabinet("Demain", Today.AddDays(1));
        deployment.WithCabinet("Dans 3 jours", Today.AddDays(3));

        var report = await deployment.Service().RunAsync(Today, withinDays: 7);

        Assert.Equal(new[] { "Demain", "Dans 3 jours", "Dans 6 jours" }, report.Expiring.Select(l => l.ClinicName));
    }

    // ---- The single-cabinet mode ----------------------------------------------------------------

    // The period ids are the whole reason this mode exists: subscription-cancel takes one, and nothing else in the
    // product prints them.
    [Fact]
    public async Task One_Cabinets_Report_Lists_Its_Ledger_With_Period_Ids_And_Covered_Spans()
    {
        var deployment = new Deployment();
        var clinicId = deployment.WithCabinet("Cabinet Ben Salah", Today.AddMonths(1));

        var trial = deployment.Repository.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Trial, Today.AddDays(-60), BaseUtc.AddMinutes(-2), durationDays: 30));
        var paid = deployment.Repository.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc, durationMonths: 12,
            amount: 1200.000m, method: SubscriptionPaymentMethod.Transfer, reference: "VIR-1"));

        var cabinet = await deployment.Service().RunForCabinetAsync(clinicId, Today);

        Assert.NotNull(cabinet);
        Assert.Equal("Cabinet Ben Salah", cabinet!.Cabinet.ClinicName);
        Assert.Equal(2, cabinet.Ledger.Count);

        Assert.Equal(trial.Id, cabinet.Ledger[0].EntryId);
        Assert.Equal("Essai gratuit", cabinet.Ledger[0].KindLabel);
        Assert.Equal(Today.AddDays(-60), cabinet.Ledger[0].FromDay);
        Assert.Equal(Today.AddDays(-31), cabinet.Ledger[0].ThroughDay);

        Assert.Equal(paid.Id, cabinet.Ledger[1].EntryId);
        Assert.Equal(1200.000m, cabinet.Ledger[1].Amount);
        Assert.Equal("Virement", cabinet.Ledger[1].MethodLabel);
        Assert.Equal("VIR-1", cabinet.Ledger[1].Reference);
    }

    // A cancelled entry is shown with its motif and contributes no span — « annulé » is a fact about the ledger, and
    // dropping the row would make the correction invisible.
    [Fact]
    public async Task A_Cancelled_Entry_Is_Listed_With_Its_Motif_And_No_Covered_Span()
    {
        var deployment = new Deployment();
        var clinicId = deployment.WithCabinet("Cabinet", Today.AddMonths(1));

        var mistake = deployment.Repository.Seed(SubscriptionPeriod.Create(
            clinicId, SubscriptionPeriodKind.Paid, Today, BaseUtc, durationMonths: 12));
        mistake.Cancel("Mauvais cabinet", "job|subscription-cancel", BaseUtc.AddHours(1));

        var cabinet = await deployment.Service().RunForCabinetAsync(clinicId, Today);

        Assert.NotNull(cabinet);
        var entry = Assert.Single(cabinet!.Ledger);
        Assert.True(entry.IsCancelled);
        Assert.Equal("Mauvais cabinet", entry.CancelReason);
        Assert.Null(entry.FromDay);
        Assert.Null(entry.ThroughDay);
    }

    // An unknown cabinet is null rather than an empty report: « this deployment has no such cabinet » and « this
    // cabinet has no entries » are different statements and the verb refuses on one of them only.
    [Fact]
    public async Task An_Unknown_Cabinet_Is_Null_Rather_Than_An_Empty_Report()
    {
        var deployment = new Deployment();
        deployment.WithCabinet("Cabinet", Today.AddMonths(1));

        Assert.Null(await deployment.Service().RunForCabinetAsync(Guid.NewGuid(), Today));
    }
}
