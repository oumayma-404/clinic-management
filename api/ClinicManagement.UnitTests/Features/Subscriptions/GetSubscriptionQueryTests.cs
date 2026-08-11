using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Subscriptions.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// `GET /api/subscription` — what « Abonnement » renders (`clinic-subscription` Part C, US-2).
///
/// <para><b>What this class is for, given the two beside it.</b> The fold's arithmetic is
/// <c>SubscriptionLedgerTests</c>' business and the state rule is <c>SubscriptionStateReaderTests</c>'; both are
/// clock-free and neither can see the *wiring*. What is only checkable here is that the handler reads the ledger at
/// all — which is the one thing the entitlement row cannot answer (« is the cover in force the free trial? ») and
/// therefore the one thing a plausible implementation gets wrong by reading the row alone and calling every
/// non-expired cabinet « Actif ».</para>
///
/// <para>⚠️ <b>These fixtures are anchored on <c>ClinicClock.ClinicToday()</c>, deliberately, and that is the
/// opposite of what <c>ClinicClockTests</c> and <c>SubscriptionGateMiddlewareTests</c> do.</b> Those two either own
/// the clock arithmetic (so reading the clock would agree with the defect by construction) or assert a coarse
/// verdict decades away. Here the property under test is « which ledger entry covers <i>today</i> », so a fixture
/// that does not straddle today has no covering entry at all and the case ceases to exist. The countdown is
/// asserted against the same anchor rather than a literal, for the same reason.</para>
/// </summary>
public class GetSubscriptionQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Published for two forfaits and deliberately not for the third — « Sur-mesure » is sold on quote.</summary>
    private sealed class Pricing : ISubscriptionPricing
    {
        public decimal? MonthlyPrice(SubscriptionPlan plan) => plan switch
        {
            SubscriptionPlan.Cabinet => 120.000m,
            SubscriptionPlan.Clinique => 290.000m,
            _ => null,
        };

        public decimal? AnnualPrice(SubscriptionPlan plan) => plan switch
        {
            SubscriptionPlan.Cabinet => 1200.000m,
            SubscriptionPlan.Clinique => 2904.000m,
            _ => null,
        };

        public string? PaymentInstructions => "Virement BIAT 08 xxx\nRéférence : le nom du cabinet.";

        public string? ContactEmail => "facturation@example.tn";

        public string? ContactPhone => "+216 71 000 000";
    }

    private sealed class Harness
    {
        public Mock<IClinicSubscriptionRepository> Subscriptions { get; } = new();

        public GetSubscriptionQueryHandler Handler()
        {
            var resolver = new Mock<ICurrentClinicResolver>();
            resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));

            return new GetSubscriptionQueryHandler(
                Subscriptions.Object,
                resolver.Object,
                new Pricing(),
                NullLogger<GetSubscriptionQueryHandler>.Instance);
        }

        /// <summary>Stages an entitlement whose <c>EndsOn</c> is the fold of <paramref name="entries"/>, never a literal.</summary>
        public ClinicSubscription With(params SubscriptionPeriod[] entries)
        {
            var subscription = ClinicSubscription.For(ClinicId, DateTime.UtcNow);
            subscription.RecomputeFrom(entries, DateTime.UtcNow);

            Subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(subscription);
            Subscriptions.Setup(s => s.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(entries);

            return subscription;
        }
    }

    private static SubscriptionPeriod Trial(DateTime day, int days = 30) =>
        SubscriptionPeriod.Trial(ClinicId, day, days, day.AddHours(9));

    private static SubscriptionPeriod Paid(DateTime day, int months) =>
        SubscriptionPeriod.Create(
            ClinicId, SubscriptionPeriodKind.Paid, day, day.AddHours(9),
            durationMonths: months, amount: 1200.000m, method: SubscriptionPaymentMethod.Transfer,
            reference: "VIR-1", recordedBy: "job|subscription-grant");

    private static SubscriptionPeriod OpenEnded(DateTime day) =>
        SubscriptionPeriod.OpenEnded(
            ClinicId, SubscriptionPeriodKind.Grandfathered, day, day.AddHours(9),
            note: "Cabinet existant à la mise en service de l'abonnement.");

    private static async Task<SubscriptionDto> Read(Harness harness)
    {
        var result = await harness.Handler().Handle(new GetSubscriptionQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ---- the state, and the one label the entitlement row alone cannot produce ---------------------------

    // [AC-2.1] A cabinet on its free days reads « Essai gratuit », not « Actif ». This is the assertion that fails
    // if the handler stops reading the ledger — every other field would still be right.
    [Fact]
    public async Task A_Cabinet_On_Its_Free_Days_Reads_Essai_Gratuit()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        harness.With(Trial(today));

        var dto = await Read(harness);

        Assert.Equal("Trial", dto.State);
        Assert.Equal("Essai gratuit", dto.StateLabel);
        Assert.True(dto.AllowsWrites);
        Assert.Equal(today.AddDays(29), dto.EndsOn);
        Assert.Equal(29, dto.DaysRemaining);
    }

    // [AC-2.1] And once a payment covers today it reads « Actif », though the trial entry is still in the ledger.
    // The trial label is a fact about *which entry covers today*, not about the cabinet ever having had one.
    [Fact]
    public async Task A_Cabinet_That_Has_Paid_Reads_Actif_Even_Though_Its_Trial_Is_Still_On_Record()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        harness.With(Trial(today.AddDays(-40)), Paid(today.AddDays(-20), months: 12));

        var dto = await Read(harness);

        Assert.Equal("Active", dto.State);
        Assert.Equal("Actif", dto.StateLabel);
        Assert.True(dto.AllowsWrites);
    }

    // [AC-2.5] « Sans échéance » is a state, and it travels as a null date rather than a far-future one — the screen
    // has to be able to say it in words, and there is no date to say.
    [Fact]
    public async Task An_Open_Ended_Entitlement_Carries_No_Date_And_No_Countdown()
    {
        var harness = new Harness();
        harness.With(OpenEnded(ClinicClock.ClinicToday().AddDays(-400)));

        var dto = await Read(harness);

        Assert.Equal("Active", dto.State);
        Assert.Null(dto.EndsOn);
        Assert.Null(dto.DaysRemaining);
        Assert.True(dto.AllowsWrites);
    }

    // [EC-11] Suspension outranks a date still in the future: « Suspendu », never « Actif », and the motif travels
    // so « suspendu pourquoi ? » is answerable on the screen rather than by telephone.
    [Fact]
    public async Task A_Suspended_Cabinet_Reads_Suspendu_And_Carries_Its_Motif()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        var subscription = harness.With(Paid(today.AddDays(-10), months: 12));
        subscription.Suspend("Litige commercial en cours.", "job|subscription-suspend", DateTime.UtcNow);

        var dto = await Read(harness);

        Assert.Equal("Suspended", dto.State);
        Assert.Equal("Suspendu", dto.StateLabel);
        Assert.False(dto.AllowsWrites);
        Assert.Equal("Litige commercial en cours.", dto.SuspensionReason);
    }

    // [AC-2.1] A lapsed cabinet: no countdown at all rather than a negative one — « −192 jours restants » is not a
    // thing to tell anybody — and writes refused while the screen itself still reads (AC-4.8).
    [Fact]
    public async Task An_Expired_Cabinet_Reads_Expire_With_No_Countdown()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        harness.With(Trial(today.AddDays(-200)));

        var dto = await Read(harness);

        Assert.Equal("Expired", dto.State);
        Assert.Equal("Expiré", dto.StateLabel);
        Assert.False(dto.AllowsWrites);
        Assert.Null(dto.DaysRemaining);
        Assert.Equal(today.AddDays(-171), dto.EndsOn);
    }

    // [AC-1.1] The last working day is `daysRemaining == 0`, not 1 and not expired: the cabinet may work all of its
    // end date. Off by one in either direction is a day of a practice's work.
    [Fact]
    public async Task The_Last_Working_Day_Reads_Zero_Days_Remaining_And_Still_Allows_Writes()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        harness.With(Trial(today.AddDays(-29)));

        var dto = await Read(harness);

        Assert.Equal(0, dto.DaysRemaining);
        Assert.True(dto.AllowsWrites);
        Assert.Equal(today, dto.EndsOn);
    }

    // ---- EC-6: no entitlement row is a fault, not a lapse ------------------------------------------------

    // [EC-6] It carries the gate's own sentence and its distinct code, so the screen says « nous le rétablissons »
    // rather than sending a cabinet to pay for something it cannot renew. And it is a *failure*, never an empty
    // success — EC-13's « never aucun abonnement » applies to this path too.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Row_Meets_The_Distinct_Missing_Code()
    {
        var harness = new Harness();
        harness.Subscriptions
            .Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicSubscription?)null);

        var result = await harness.Handler().Handle(new GetSubscriptionQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefusals.MissingCode, result.Code);
        Assert.Equal(SubscriptionRefusals.Missing, result.Error);
        Assert.NotEqual(SubscriptionRefusals.RequiredCode, result.Code);
    }

    // ---- AC-2.1 / AC-2.4: the tariff -------------------------------------------------------------------

    // [AC-2.1][AC-2.4] Every forfait is listed, in enum order, and an unpublished price stays **null** rather than
    // becoming 0,000 DT — « sur devis » is a true statement about Sur-mesure and a zero is not.
    [Fact]
    public async Task The_Published_Tariff_Lists_Every_Forfait_And_Keeps_An_Unpublished_Price_Absent()
    {
        var harness = new Harness();
        harness.With(Trial(ClinicClock.ClinicToday()));

        var dto = await Read(harness);

        Assert.Equal(new[] { "Cabinet", "Clinique", "SurMesure" }, dto.Plans.Select(p => p.Plan));
        Assert.Equal(new[] { "Cabinet", "Clinique", "Sur-mesure" }, dto.Plans.Select(p => p.Label));

        var surMesure = dto.Plans.Single(p => p.Plan == "SurMesure");
        Assert.Null(surMesure.PriceMonthlyDt);
        Assert.Null(surMesure.PriceAnnualDt);

        Assert.Equal(290.000m, dto.Plans.Single(p => p.Plan == "Clinique").PriceMonthlyDt);
        Assert.Equal(2904.000m, dto.Plans.Single(p => p.Plan == "Clinique").PriceAnnualDt);
    }

    // [AC-2.1] A cabinet that has chosen no forfait quotes no price of its own — the tariff above is what the screen
    // shows it instead. A default here would state a commercial choice nobody made.
    [Fact]
    public async Task A_Cabinet_With_No_Forfait_Quotes_No_Price_Of_Its_Own()
    {
        var harness = new Harness();
        harness.With(Trial(ClinicClock.ClinicToday()));

        var dto = await Read(harness);

        Assert.Null(dto.Plan);
        Assert.Null(dto.PlanLabel);
        Assert.Null(dto.PriceMonthlyDt);
        Assert.Null(dto.PriceAnnualDt);
        Assert.NotEmpty(dto.Plans);
    }

    // [AC-2.1] And a cabinet that has one quotes exactly that one's figures.
    [Fact]
    public async Task A_Cabinet_With_A_Forfait_Quotes_That_Forfaits_Figures()
    {
        var today = ClinicClock.ClinicToday();
        var harness = new Harness();
        var entries = new[] { Paid(today.AddDays(-10), months: 12) };

        var subscription = ClinicSubscription.For(ClinicId, DateTime.UtcNow, SubscriptionPlan.Clinique);
        subscription.RecomputeFrom(entries, DateTime.UtcNow);
        harness.Subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        harness.Subscriptions.Setup(s => s.GetEntriesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entries);

        var dto = await Read(harness);

        Assert.Equal("Clinique", dto.Plan);
        Assert.Equal("Clinique", dto.PlanLabel);
        Assert.Equal(290.000m, dto.PriceMonthlyDt);
        Assert.Equal(2904.000m, dto.PriceAnnualDt);
    }

    // [AC-2.4] The instructions and the contact details are configuration, passed through untouched — this screen is
    // the only place they are published, so a handler that dropped one would leave a refused cabinet with the
    // sentence « rendez-vous dans Abonnement » and nothing to act on when it got there.
    [Fact]
    public async Task The_Payment_Instructions_And_Contacts_Reach_The_Screen_Verbatim()
    {
        var harness = new Harness();
        harness.With(Trial(ClinicClock.ClinicToday()));

        var dto = await Read(harness);

        Assert.Equal("Virement BIAT 08 xxx\nRéférence : le nom du cabinet.", dto.PaymentInstructions);
        Assert.Equal("facturation@example.tn", dto.ContactEmail);
        Assert.Equal("+216 71 000 000", dto.ContactPhone);
    }
}
