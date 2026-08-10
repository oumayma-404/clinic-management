using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Subscriptions.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Subscriptions;

/// <summary>
/// Recording a received payment (<c>clinic-subscription</c> Part F, US-5 — AC-5.1/5.2/5.3/5.7, EC-3, EC-5).
///
/// <para><b>The load-bearing case is <see cref="Paying_Ten_Days_Early_Never_Costs_Days"/></b> (EC-3). It is the one
/// that fails on the obvious wrong implementation — <c>today + duration</c>, which is exactly how AC-5.2's
/// « whichever is later, the current end date or today » reads in prose — and the failure is silent money: the
/// cabinet loses the remainder of what it already paid for, in a figure nobody recomputes afterwards.</para>
///
/// <para><b>The second is <see cref="Two_Simultaneous_Grants_Both_Land_And_Both_Are_Kept"/></b> (EC-5). The
/// entitlement's <c>Version</c> is mapped onto <c>xmin</c>, so the natural implementation surfaces a 409 to the
/// second writer — which EC-5 forbids in both halves: the grant would not land, and the caller would be shown a
/// conflict it cannot act on. Retrying is only correct because <c>EndsOn</c> is <i>derived</i>, and this case is what
/// says so.</para>
///
/// <para>⚠️ <b>Fixtures are anchored on <c>ClinicClock.ClinicToday()</c></b>, unlike
/// <c>SubscriptionGateMiddlewareTests</c>' decades-away dates: the handler stamps the entry's
/// <c>RecordedOnClinicDay</c> from the real clock, and that day <i>is</i> the fold's anchor — so a fixture at a fixed
/// literal would assert against arithmetic the handler could not have performed.</para>
/// </summary>
public class GrantSubscriptionPeriodCommandHandlerTests
{
    private static readonly DateTime Today = ClinicClock.ClinicToday();

    private static GrantSubscriptionPeriodCommandHandler Handler(SubscriptionVendorHarness harness) =>
        new(harness.Subscriptions, harness.Clinics.Object, harness.Users.Object, harness.UnitOfWork.Object,
            NullLogger<GrantSubscriptionPeriodCommandHandler>.Instance);

    private static GrantSubscriptionPeriodCommand Grant(
        int? months = 12, string? email = null, Guid? clinicId = null) =>
        new()
        {
            ClinicId = email is null ? clinicId ?? SubscriptionVendorHarness.ClinicId : null,
            AdminEmail = email,
            DurationMonths = months,
        };

    // ---- AC-5.1: which cabinet, and what is recorded ----------------------------------------------

    // [AC-5.1] A grant by clinic id records one entry and moves the date.
    [Fact]
    public async Task A_Grant_By_Clinic_Id_Records_One_Entry_And_Extends_The_Entitlement()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var result = await Handler(harness).Handle(Grant(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, harness.Subscriptions.Entries.Count);
        Assert.Equal(SubscriptionPeriodKind.Paid, harness.Subscriptions.Entries[1].Kind);
        Assert.Equal(12, harness.Subscriptions.Entries[1].DurationMonths);
        Assert.Equal(harness.Subscriptions.Subscription!.EndsOn, result.Value!.EndsOn);
    }

    // [AC-5.1] …or by the e-mail of somebody who works there, which is what the vendor actually has to hand.
    [Fact]
    public async Task A_Grant_By_Administrator_Email_Resolves_The_Same_Cabinet()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var result = await Handler(harness).Handle(
            Grant(email: SubscriptionVendorHarness.AdminEmail), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionVendorHarness.ClinicId, result.Value!.ClinicId);
    }

    // [AC-5.1] The optional fields all reach the row. They are what « what were we paid, and for what » is
    // answered from later, and none of them is derivable from anything else.
    [Fact]
    public async Task The_Optional_Payment_Details_Are_All_Recorded()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var result = await Handler(harness).Handle(
            new GrantSubscriptionPeriodCommand
            {
                ClinicId = SubscriptionVendorHarness.ClinicId,
                DurationMonths = 12,
                Plan = SubscriptionPlan.Clinique,
                Amount = 2904.000m,
                Method = SubscriptionPaymentMethod.Transfer,
                Reference = "VIR-2026-0413",
                Note = "Forfait annuel",
                RecordedBy = "job|subscription-grant",
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = harness.Subscriptions.Entries[1];
        Assert.Equal(2904.000m, entry.Amount);
        Assert.Equal(SubscriptionPaymentMethod.Transfer, entry.Method);
        Assert.Equal("VIR-2026-0413", entry.Reference);
        Assert.Equal("Forfait annuel", entry.Note);
        Assert.Equal("job|subscription-grant", entry.RecordedBy);

        // The forfait is a label on the entitlement (FR-10), not on the entry — it is what the cabinet is on now,
        // not a property of one payment.
        Assert.Equal(SubscriptionPlan.Clinique, harness.Subscriptions.Subscription!.Plan);
    }

    // ---- AC-5.2 / EC-3: the arithmetic -----------------------------------------------------------

    // [AC-5.2][EC-3] The one that fails on `today + duration`. A cabinet still covered for another 10 days gets
    // its remaining days ON TOP of the new year, because the fold resumes at the first day not yet covered.
    [Fact]
    public async Task Paying_Ten_Days_Early_Never_Costs_Days()
    {
        var harness = new SubscriptionVendorHarness();
        // One month recorded 20 days ago: the cover runs 10 more days.
        var subscription = harness.GivenEntitlement(Today.AddDays(-20), durationMonths: 1);
        var endBefore = subscription.EndsOn;

        var result = await Handler(harness).Handle(Grant(months: 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(endBefore, result.Value!.PreviousEndsOn);
        Assert.Equal(endBefore!.Value.AddDays(1).AddMonths(12).AddDays(-1), result.Value.EndsOn);
        Assert.True(result.Value.EndsOn > Today.AddMonths(12), "Paying early must not lose the remainder.");
    }

    // [AC-5.2] A LAPSED cabinet is the other branch, and a single « old end + duration » gets it wrong the other
    // way — it would grant a year from a date in the past. Cover restarts from the day the payment was recorded.
    [Fact]
    public async Task A_Lapsed_Cabinet_Restarts_From_The_Day_It_Paid()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today.AddDays(-200), durationMonths: 1);

        var result = await Handler(harness).Handle(Grant(months: 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Today.AddMonths(12).AddDays(-1), result.Value!.EndsOn);
    }

    // [AC-5.4] The date is never accumulated: it is always the fold over the whole ledger, so the handler's answer
    // and an independent fold of the same rows must agree. This is what makes cancelling any entry able to correct
    // the date later.
    [Fact]
    public async Task The_New_Date_Is_The_Fold_Over_The_Whole_Ledger()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today.AddDays(-5), durationMonths: 1);

        var result = await Handler(harness).Handle(Grant(months: 6), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SubscriptionLedger.Fold(harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            result.Value!.EndsOn);
    }

    // ---- AC-5.3: entries accumulate --------------------------------------------------------------

    // [AC-5.3] Nothing overwrites an earlier entry. Three grants leave three rows, and the date is the fold of all.
    [Fact]
    public async Task Successive_Grants_Accumulate_Rather_Than_Overwrite()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationDays: 30);
        var handler = Handler(harness);

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await handler.Handle(Grant(months: 1), CancellationToken.None)).IsSuccess);
        }

        Assert.Equal(4, harness.Subscriptions.Entries.Count);
        Assert.Equal(
            SubscriptionLedger.Fold(harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            harness.Subscriptions.Subscription!.EndsOn);
    }

    // ---- AC-5.7: refusals, and that nothing is written ------------------------------------------

    // [AC-5.7] A non-positive duration is refused, and the message says which figure. Asserted alongside
    // Times.Never on the save, per this project's convention: a refusal that still wrote would be worse than one
    // that refused wrongly.
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task A_Non_Positive_Duration_Is_Refused_And_Nothing_Is_Written(int months)
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var result = await Handler(harness).Handle(Grant(months: months), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("positive", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(harness.Subscriptions.Entries);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5.7] An unknown cabinet is refused naming which — by id…
    [Fact]
    public async Task An_Unknown_Clinic_Id_Is_Refused_Naming_It()
    {
        var harness = new SubscriptionVendorHarness();
        var unknown = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        harness.Clinics.Setup(c => c.ExistsAsync(unknown, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler(harness).Handle(Grant(clinicId: unknown), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains(unknown.ToString(), result.Error);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5.7] …and by e-mail, with a DIFFERENT sentence. « Cabinet introuvable » for both would hide a typo in the
    // address as an unknown practice, sending the operator to look in the wrong place.
    [Fact]
    public async Task An_Unknown_Email_Is_Refused_Naming_The_Address_Not_The_Cabinet()
    {
        var harness = new SubscriptionVendorHarness();

        var result = await Handler(harness).Handle(Grant(email: "nobody@nowhere.tn"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("nobody@nowhere.tn", result.Error);
        Assert.Contains("compte", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // Naming no cabinet at all is its own refusal — it is the likeliest slip and « aucun cabinet avec
    // l'identifiant 00000000-… » would be a confusing way to say « you forgot a flag ».
    [Fact]
    public async Task Naming_No_Cabinet_Is_Refused_With_The_Usage_Sentence()
    {
        var harness = new SubscriptionVendorHarness();

        var result = await Handler(harness).Handle(
            new GrantSubscriptionPeriodCommand { DurationMonths = 12 }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionCabinetLookup.NothingSuppliedError, result.Error);
    }

    // A grant with NO duration form at all would be « sans échéance » — permanent free cover, reachable by
    // forgetting one flag and unnoticeable afterwards. Refused rather than merely undocumented.
    [Fact]
    public async Task A_Grant_With_No_Duration_Is_Refused_Rather_Than_Made_Open_Ended()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var result = await Handler(harness).Handle(
            new GrantSubscriptionPeriodCommand { ClinicId = SubscriptionVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(GrantSubscriptionPeriodCommandHandler.NoDurationError, result.Error);
        Assert.Single(harness.Subscriptions.Entries);
    }

    // [EC-6] A cabinet with no entitlement row is a fault on our side, and it carries the gate's own distinct code
    // so it is diagnosable rather than reading as a lapse.
    [Fact]
    public async Task A_Cabinet_With_No_Entitlement_Is_Refused_Under_The_Missing_Code()
    {
        var harness = new SubscriptionVendorHarness();

        var result = await Handler(harness).Handle(Grant(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefusals.MissingCode, result.Code);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- EC-5: two grants at the same moment ----------------------------------------------------

    // [EC-5] Both land and both are kept, and the caller is never shown a conflict. The first save loses the xmin
    // race; the retry re-reads the ledger — which now holds the other writer's entry too — and folds over ALL of
    // them, so the date is right for both rather than for whichever went second.
    [Fact]
    public async Task Two_Simultaneous_Grants_Both_Land_And_Both_Are_Kept()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var attempts = 0;
        SubscriptionPeriod? concurrent = null;

        harness.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (++attempts > 1)
                {
                    return 1;
                }

                // Another writer committed between our read and our save: a second grant, and a fresh row version.
                concurrent = harness.Subscriptions.Seed(SubscriptionPeriod.Create(
                    SubscriptionVendorHarness.ClinicId, SubscriptionPeriodKind.Paid, Today,
                    DateTime.UtcNow, durationMonths: 3));

                harness.Subscriptions.ReloadsAs = ClinicSubscription.For(
                    SubscriptionVendorHarness.ClinicId, DateTime.UtcNow);

                throw new ConflictException("Cet enregistrement a été modifié par quelqu'un d'autre.");
            });

        var result = await Handler(harness).Handle(Grant(months: 12), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);

        // Three entries: the opening one, the concurrent grant, and ours. Nothing was overwritten or discarded.
        Assert.Equal(3, harness.Subscriptions.Entries.Count);
        Assert.NotNull(concurrent);
        Assert.Contains(concurrent!, harness.Subscriptions.Entries);

        // And the date accounts for every one of them, which is the property that makes retrying legitimate here
        // rather than a way of hiding a lost update.
        Assert.Equal(
            SubscriptionLedger.Fold(harness.Subscriptions.Entries.Select(e => e.ToLedgerEntry())),
            result.Value!.EndsOn);
    }

    // [EC-5] A conflict that never clears is still not shown as a conflict: the caller gets a French sentence it
    // can act on, never a 409 that escapes as « modifié par quelqu'un d'autre » — which for a vendor verb is advice
    // about the wrong subject.
    [Fact]
    public async Task A_Conflict_That_Never_Clears_Refuses_In_French_Rather_Than_Escaping_As_A_Conflict()
    {
        var harness = new SubscriptionVendorHarness();
        harness.GivenEntitlement(Today, durationMonths: 1);

        var attempts = 0;
        harness.UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(_ =>
            {
                attempts++;
                throw new ConflictException("conflit");
            });

        var result = await Handler(harness).Handle(Grant(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefold.ExhaustedError, result.Error);
        Assert.Equal(SubscriptionRefold.MaxAttempts, attempts);
    }
}
