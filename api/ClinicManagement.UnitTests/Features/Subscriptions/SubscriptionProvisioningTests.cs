using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using ClinicManagement.Infrastructure.Deployment;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// What entitlement a brand-new cabinet starts with (AC-1.2, AC-1.5, AC-7.1–7.3, FR-13), and the capability seam
/// that decides it.
///
/// <para>The load-bearing case is <see cref="No_Configuration_Key_Can_Turn_Enforcement_On_Or_Off"/>. AC-7.3 says
/// whether subscriptions apply is decided by the deployment's <b>kind</b> and by nothing an operator can set — and
/// the failure it prevents is severe in one direction: a <c>Subscription:*</c> key able to flip it would put a
/// clinic's own Windows PC one config edit away from refusing its own patient records. It is the
/// <c>httpsConfigured</c> trap from <c>LEARNINGS.md :45</c>, and the reason <c>TrialDays</c> and
/// <c>RequiresSubscription</c> live on the same interface but come from different places.</para>
/// </summary>
public class SubscriptionProvisioningTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime CreationDay = new(2026, 8, 10);
    private static readonly DateTime NowUtc = new(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc);

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // ---- AC-1.2 / AC-1.1: the trial ---------------------------------------------------------------

    // [AC-1.1][AC-1.2] Where subscriptions are enforced, a new cabinet gets a Trial entry running to the end of its
    // 30th clinic-local day, the creation day counting as day 1.
    [Fact]
    public void A_Cabinet_On_The_Hosted_Deployment_Gets_Thirty_Trial_Days()
    {
        var entitlement = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: true, CreationDay, trialDays: 30, NowUtc);

        Assert.Equal(SubscriptionPeriodKind.Trial, entitlement.OpeningEntry.Kind);
        Assert.Equal(30, entitlement.OpeningEntry.DurationDays);
        Assert.Equal(new DateTime(2026, 9, 8), entitlement.Subscription.EndsOn);
    }

    // [R-6] The trial's date comes out of the FOLD, not from a hand-written AddDays — which is what keeps
    // « one write path to EndsOn » literally true and stops `subscription-end-date-matches-ledger` going red on
    // every newly created cabinet. Asserted by folding the entry independently and comparing.
    [Fact]
    public void The_Trials_End_Date_Is_Its_Own_Ledgers_Fold()
    {
        var entitlement = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: true, CreationDay, trialDays: 30, NowUtc);

        Assert.Equal(
            SubscriptionLedger.Fold(new[] { entitlement.OpeningEntry.ToLedgerEntry() }),
            entitlement.Subscription.EndsOn);
    }

    // ---- AC-1.2 / AC-7.1 / AC-7.2 / FR-13: the other two topologies -------------------------------

    // [AC-1.2][FR-13] Where subscriptions are NOT enforced the entitlement still exists — open-ended. That is what
    // makes « no cabinet without an entitlement » true in all three topologies while nothing can ever expire in
    // two of them, and it is why `every-clinic-has-an-entitlement` can be a flat count over every cabinet.
    [Fact]
    public void A_Cabinet_Where_Subscriptions_Are_Not_Enforced_Gets_An_Open_Ended_Entitlement()
    {
        var entitlement = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: false, CreationDay, trialDays: 30, NowUtc);

        Assert.True(entitlement.OpeningEntry.IsOpenEnded);
        Assert.Null(entitlement.Subscription.EndsOn);
        Assert.NotNull(entitlement.OpeningEntry.Note);
    }

    // Both rows name the cabinet they belong to. The whole tenant isolation of this feature rests on it: the
    // filters compare that column to the scoped clinic, so a row carrying the wrong id is either invisible to its
    // own practice or visible to another.
    [Fact]
    public void Both_Rows_Name_The_Cabinet_They_Belong_To()
    {
        var entitlement = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: true, CreationDay, trialDays: 30, NowUtc);

        Assert.Equal(ClinicId, entitlement.Subscription.ClinicId);
        Assert.Equal(ClinicId, entitlement.OpeningEntry.ClinicId);
    }

    // ---- AC-1.5 / EC-12: changing the setting later ----------------------------------------------

    // [AC-1.5][EC-12] The trial length is recorded as the entry's DURATION, so a later config change moves no
    // existing cabinet's end date. Two cabinets provisioned under different settings keep their own dates for ever
    // — which is only true because nothing re-reads the setting when the date is recomputed.
    [Fact]
    public void Changing_The_Configured_Trial_Length_Moves_No_Existing_Cabinets_Date()
    {
        var underThirty = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: true, CreationDay, trialDays: 30, NowUtc);
        var underFourteen = SubscriptionProvisioning.CreateForNewClinic(
            ClinicId, requiresSubscription: true, CreationDay, trialDays: 14, NowUtc);

        Assert.Equal(new DateTime(2026, 9, 8), underThirty.Subscription.EndsOn);
        Assert.Equal(new DateTime(2026, 8, 23), underFourteen.Subscription.EndsOn);

        // Re-folding the older cabinet's own ledger still yields its original date: the duration is in the row.
        underThirty.Subscription.RecomputeFrom(new[] { underThirty.OpeningEntry }, DateTime.UtcNow);
        Assert.Equal(new DateTime(2026, 9, 8), underThirty.Subscription.EndsOn);
    }

    // ---- AC-7.3: the capability, and what cannot flip it -----------------------------------------

    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant, true)]
    [InlineData(DeploymentKind.SelfHostedLan, false)]
    [InlineData(DeploymentKind.CloudBrowser, false)]
    public void Enforcement_Follows_The_Deployment_Kind(DeploymentKind kind, bool expected)
    {
        var policy = new SubscriptionPolicy(DeploymentProfile.For(kind), Configuration());

        Assert.Equal(expected, policy.RequiresSubscription);
    }

    // [AC-7.3] The boundary the whole seam exists to protect. Every plausible key an operator might reach for is
    // set here, on BOTH the profile that enforces and the one that must not, and neither answer moves. `false` in
    // particular is the dangerous direction: a clinic's own PC refusing its own patient records because somebody
    // set a flag is not a configuration mistake, it is a lockout.
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan, false)]
    [InlineData(DeploymentKind.HostedMultiTenant, true)]
    public void No_Configuration_Key_Can_Turn_Enforcement_On_Or_Off(DeploymentKind kind, bool expected)
    {
        var policy = new SubscriptionPolicy(
            DeploymentProfile.For(kind),
            Configuration(
                ("Subscription:Enabled", (!expected).ToString()),
                ("Subscription:RequiresSubscription", (!expected).ToString()),
                ("Subscription:Enforce", (!expected).ToString()),
                ("Subscription:TrialDays", "1")));

        Assert.Equal(expected, policy.RequiresSubscription);
    }

    // TrialDays IS the operator's, unlike the capability above — that is the split, and it has to work.
    [Theory]
    [InlineData(null, 30)]
    [InlineData("", 30)]
    [InlineData("14", 14)]
    [InlineData("1", 1)]
    [InlineData("365", 365)]
    // Out of range falls back rather than refusing: a typo must not create a cabinet with a nonsensical trial, and
    // 0 or a negative would mean « expired the day it signed up ».
    [InlineData("0", 30)]
    [InlineData("-5", 30)]
    [InlineData("4000", 30)]
    [InlineData("not-a-number", 30)]
    public void The_Trial_Length_Is_Operator_Configuration_With_A_Guarded_Fallback(string? configured, int expected)
    {
        var policy = new SubscriptionPolicy(
            DeploymentProfile.For(DeploymentKind.HostedMultiTenant),
            Configuration(("Subscription:TrialDays", configured)));

        Assert.Equal(expected, policy.TrialDays);
        Assert.Equal(30, SubscriptionPolicy.DefaultTrialDays);
    }

    // ---- AC-2.4: the price is configuration, and an unreadable one is « non publié » ---------------

    // [AC-2.4] A price the deployment has not filled in reads as ABSENT, not as 0,000 DT: « le tarif n'est pas
    // publié » is true of an unconfigured deployment and a zero is not. An unparseable one behaves the same way
    // rather than throwing — this feeds the one screen an expired cabinet opens, so a typo must not 500 it.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-40")]
    [InlineData("quarante")]
    public void An_Absent_Zero_Or_Unreadable_Price_Reads_As_Not_Published(string? configured)
    {
        var pricing = new SubscriptionPricing(
            Configuration(("Subscription:Plans:Cabinet:PriceMonthlyDt", configured)));

        Assert.Null(pricing.MonthlyPrice(SubscriptionPlan.Cabinet));
    }

    // Invariant culture, deliberately: a config file is not localised, and on a fr-TN host « 120.5 » read with the
    // ambient culture would become 1205 — a tenfold price nobody typed.
    [Fact]
    public void A_Price_Is_Read_With_The_Invariant_Culture()
    {
        var pricing = new SubscriptionPricing(Configuration(
            ("Subscription:Plans:Cabinet:PriceMonthlyDt", "120.500"),
            ("Subscription:Plans:Clinique:PriceAnnualDt", "1300")));

        Assert.Equal(120.5m, pricing.MonthlyPrice(SubscriptionPlan.Cabinet));
        Assert.Equal(1300m, pricing.AnnualPrice(SubscriptionPlan.Clinique));
        // Each plan reads its own key — a shared read would quote one forfait's price for another.
        Assert.Null(pricing.MonthlyPrice(SubscriptionPlan.Clinique));
    }
}
