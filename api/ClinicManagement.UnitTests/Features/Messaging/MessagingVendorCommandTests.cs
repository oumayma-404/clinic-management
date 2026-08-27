using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Features.Messaging.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Messaging;

/// <summary>
/// The vendor's two allocation commands (<c>vendor-whatsapp-messaging-quota</c> US-6, US-7).
///
/// <para>They run over an <b>in-memory ledger</b> rather than a mocked repository, because every AC here is about what
/// the ledger ends up holding and what the fold makes of it. The two highest-value cases are
/// <see cref="A_Lowering_Takes_Effect_Next_Month_And_Leaves_This_Month_Alone"/> — which fails on the naive « the entry
/// starts now », the reading AC-6.1 invites, and whose failure is a practice cut off mid-afternoon — and
/// <see cref="Cancelling_A_Consumed_Top_Up_Reaches_The_CURRENT_Month_And_Leaves_Consumption_Alone"/>, which is the
/// deliberate asymmetry with a lowering (AC-7.4a) and fails on the tidier « a cancellation is just a lowering ».</para>
///
/// <para>⚠️ <b>The fixtures anchor on <c>ClinicClock.CurrentMonthKey()</c></b>, the opposite of
/// <c>SubscriptionGateMiddlewareTests</c>' decades-away dates and for <c>GrantSubscriptionPeriodCommandHandlerTests</c>'
/// reason: the property under test is « which month does this take effect in <i>relative to now</i> », so a fixture
/// pinned to 2020 has no current month in it and the case ceases to exist. The month <i>arithmetic</i> is
/// <c>ClinicClockMonthTests</c>' business and is pinned there against fixed instants.</para>
/// </summary>
public class MessagingVendorCommandTests
{
    private readonly MessagingVendorHarness _harness = new();

    private static string ThisMonth => ClinicClock.CurrentMonthKey();

    private static string NextMonth => ClinicClock.NextMonthKey(ClinicClock.CurrentMonthKey());

    private GrantMessagingAllowanceCommandHandler Grant() =>
        new(_harness.Allowances, _harness.Clinics.Object, _harness.Users.Object, _harness.UnitOfWork.Object,
            NullLogger<GrantMessagingAllowanceCommandHandler>.Instance);

    private CancelMessagingAllowanceCommandHandler Cancel() =>
        new(_harness.Allowances, _harness.Clinics.Object, _harness.Users.Object, _harness.UnitOfWork.Object,
            NullLogger<CancelMessagingAllowanceCommandHandler>.Instance);

    // [AC-6.1][AC-6.3][AC-6.4a] A raise is effective THIS month, and the month's snapshot is rewritten with it — which
    // is what releases held reminders within one dispatch cycle.
    [Fact]
    public async Task A_Raise_Takes_Effect_This_Month_And_Rewrites_The_Snapshot()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth, consumed: 200);
        Assert.True(month.IsExhausted);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand { ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 500 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MessagingAllowanceKind.Standing, result.Value!.Kind);
        Assert.Equal(ThisMonth, result.Value.EffectiveMonth);
        Assert.Equal(200, result.Value.PreviousAllowanceThisMonth);
        Assert.Equal(500, result.Value.AllowanceThisMonth);

        // The stored snapshot moved too, not just the fold: it is what the outbox gate enforces on, so a fold that
        // was right while the row stayed at 200 would leave the reminders held anyway.
        Assert.Equal(500, month.AllowanceMessages);
        Assert.Equal(200, month.ConsumedMessages);
        Assert.False(month.IsExhausted);
    }

    // [AC-6.4][AC-6.4a] A lowering is effective NEXT month and this month's figure is untouched. The naive
    // implementation — « the entry starts now » — passes every other case in this file and cuts a practice off
    // mid-afternoon by a change it had no warning of.
    [Fact]
    public async Task A_Lowering_Takes_Effect_Next_Month_And_Leaves_This_Month_Alone()
    {
        var (_, month) = _harness.GivenStanding(500, ThisMonth, consumed: 300);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand { ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 100 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(NextMonth, result.Value!.EffectiveMonth);

        // Both halves matter. The month row keeps 500 — the practice can still send the 200 it has left — and the
        // response reports the unchanged figure rather than the one that will apply later.
        Assert.Equal(500, month.AllowanceMessages);
        Assert.Equal(500, result.Value.AllowanceThisMonth);
    }

    // [AC-6.4a] The decision is measured against the STANDING figure, not the folded total. A cabinet on 200/month with
    // a +300 top-up this month is folded to 500; comparing 400 against that would read an ordinary RAISE as a lowering
    // and defer it by a month for a reason nobody chose.
    [Fact]
    public async Task A_Raise_Is_Not_Read_As_A_Lowering_Because_A_Top_Up_Inflated_The_Month()
    {
        _harness.GivenStanding(200, ThisMonth);
        _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.TopUp, 300, ThisMonth,
            new DateTime(2026, 1, 6, 9, 0, 0, DateTimeKind.Utc)));

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand { ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 400 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(ThisMonth, result.Value!.EffectiveMonth);
    }

    // [AC-6.5] A top-up may name the current or a future month, never a past one — it would release nothing and would
    // rewrite a figure the practice has already been shown. Carries its own code so a console can point at the field.
    [Fact]
    public async Task A_Top_Up_For_A_Past_Month_Is_Refused_Under_Its_Own_Code()
    {
        _harness.GivenStanding(200, ThisMonth);
        var lastMonth = ClinicClock.PrecedingMonthKeys(ThisMonth, 1).Single();

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                TopUpMessages = 300,
                AppliesToMonth = lastMonth
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingAllowancePlan.PastMonthCode, result.Code);
        Assert.Contains(ClinicClock.MonthLabelFr(lastMonth), result.Error);
        Assert.Empty(_harness.Allowances.Entries.Where(e => e.Kind == MessagingAllowanceKind.TopUp));
    }

    // [AC-6.5] …and a FUTURE month is accepted, which is the other half: the vendor sells August's capacity in July.
    // Without this case the one above is satisfied by refusing every top-up.
    [Fact]
    public async Task A_Top_Up_For_A_Future_Month_Is_Accepted_And_Leaves_This_Month_Alone()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                TopUpMessages = 300,
                AppliesToMonth = NextMonth
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MessagingAllowanceKind.TopUp, result.Value!.Kind);
        Assert.Equal(NextMonth, result.Value.EffectiveMonth);
        Assert.Equal(200, month.AllowanceMessages);
    }

    // [AC-6.4a] A month on a STANDING figure is refused rather than honoured: the server owns that decision, and
    // accepting a caller's month here would be the second answer AC-6.4a exists to prevent.
    [Fact]
    public async Task A_Standing_Figure_May_Not_Name_Its_Own_Month()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                MessagesPerMonth = 500,
                AppliesToMonth = NextMonth
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingAllowancePlan.MonthOnStandingError, result.Error);
    }

    // [AC-6.6] « Offert » is no amount at all. An amount of 0,000 DT is refused rather than normalised to null, because
    // the two are different statements about the same allocation and picking a side for the vendor is how a fiche comes
    // to show a payment nobody made.
    [Fact]
    public async Task An_Amount_Of_Zero_Is_Refused_Rather_Than_Read_As_Complimentary()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                MessagesPerMonth = 500,
                AmountDt = 0m
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingAllowancePlan.ZeroAmountError, result.Error);
    }

    // [AC-6.6] …and a complimentary allocation with NO amount lands, carrying null. The pair is what makes « no amount
    // rather than an amount of zero » a testable claim rather than a wording preference.
    [Fact]
    public async Task A_Complimentary_Allocation_Carries_No_Amount()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                TopUpMessages = 100,
                AppliesToMonth = ThisMonth,
                Note = "Geste commercial"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = _harness.Allowances.Entries.Single(e => e.Id == result.Value!.EntryId);
        Assert.Null(entry.Amount);
    }

    // [AC-6.2][EC-5] Two genuinely different allocations both land and are both kept. An append-only ledger has no
    // conflict to report, and the surplus one is corrected by a cancellation rather than by refusing the money.
    [Fact]
    public async Task Two_Different_Allocations_Both_Land_And_Both_Are_Kept()
    {
        _harness.GivenStanding(200, ThisMonth);
        var handler = Grant();

        var first = await handler.Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, TopUpMessages = 100, AppliesToMonth = ThisMonth
            },
            CancellationToken.None);

        var second = await handler.Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, TopUpMessages = 50, AppliesToMonth = ThisMonth
            },
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.EntryId, second.Value!.EntryId);

        // Both top-ups add on top of the standing figure: 200 + 100 + 50.
        Assert.Equal(350, second.Value.AllowanceThisMonth);
    }

    // [AC-7.1] The motif is mandatory. Not politeness: every month the entry fed recomputes as a result, possibly to
    // « épuisé », and « pourquoi le forfait a-t-il diminué ? » must stay answerable afterwards.
    [Fact]
    public async Task A_Cancellation_Without_A_Motif_Is_Refused()
    {
        var (entry, _) = _harness.GivenStanding(200, ThisMonth);

        var result = await Cancel().Handle(
            new CancelMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = entry.Id, Reason = "   "
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelMessagingAllowanceCommandHandler.ReasonRequiredError, result.Error);
        Assert.False(entry.IsCancelled);
    }

    // [AC-7.2][AC-7.4] The deliberate asymmetry with AC-6.4, and the case a tidier « a cancellation is just a lowering »
    // gets wrong: it reaches the CURRENT month, consumption is untouched, remaining floors at 0, and the month reads
    // « épuisé ». The entry is KEPT, carrying its motif and its canceller.
    [Fact]
    public async Task Cancelling_A_Consumed_Top_Up_Reaches_The_CURRENT_Month_And_Leaves_Consumption_Alone()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth, consumed: 260);

        var topUp = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.TopUp, 300, ThisMonth,
            new DateTime(2026, 1, 6, 9, 0, 0, DateTimeKind.Utc)));

        month.SetAllowance(500, new DateTime(2026, 1, 6, 9, 0, 0, DateTimeKind.Utc));

        var result = await Cancel().Handle(
            new CancelMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                EntryId = topUp.Id,
                Reason = "Complément enregistré sur le mauvais cabinet",
                CancelledBy = "console|abc"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(500, result.Value!.PreviousAllowanceThisMonth);
        Assert.Equal(200, result.Value.AllowanceThisMonth);
        Assert.Equal(260, result.Value.ConsumedThisMonth);
        Assert.True(result.Value.ExhaustedThisMonth);

        // The entry stays, struck through, with its motif and its author — never edited and never deleted.
        Assert.Contains(topUp, _harness.Allowances.Entries);
        Assert.True(topUp.IsCancelled);
        Assert.Equal("Complément enregistré sur le mauvais cabinet", topUp.CancelReason);
        Assert.Equal("console|abc", topUp.CancelledBy);

        // Consumption is untouched and remaining floors at zero: nothing was unsent and nothing was clawed back.
        Assert.Equal(260, month.ConsumedMessages);
        Assert.Equal(200, month.AllowanceMessages);
        Assert.Equal(0, month.RemainingMessages);
    }

    // [AC-7.4] Cancelling a STANDING entry hands the month back to the earlier standing figure — not to « the current
    // figure minus this entry's messages », which is the arithmetic a client-side preview would reach for and which is
    // wrong in both directions here.
    [Fact]
    public async Task Cancelling_A_Standing_Entry_Restores_The_Earlier_Standing_Figure()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth);

        var raised = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.ClinicId, MessagingAllowanceKind.Standing, 900, ThisMonth,
            new DateTime(2026, 1, 7, 9, 0, 0, DateTimeKind.Utc)));

        month.SetAllowance(900, new DateTime(2026, 1, 7, 9, 0, 0, DateTimeKind.Utc));

        var result = await Cancel().Handle(
            new CancelMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = raised.Id, Reason = "Erreur de saisie"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.Value!.AllowanceThisMonth);
        Assert.Equal(200, month.AllowanceMessages);
    }

    // [AC-7.5] Cancelling an already-cancelled allocation is refused with a distinct machine-readable outcome — that
    // entry was struck through by SOMEBODY, and which colleague and for what motif is what the refusal sends the reader
    // back to.
    [Fact]
    public async Task Cancelling_An_Already_Cancelled_Allocation_Is_Refused_With_Its_Own_Code()
    {
        var (entry, _) = _harness.GivenStanding(200, ThisMonth);
        entry.Cancel("Première annulation", "console|abc", new DateTime(2026, 2, 1, 9, 0, 0, DateTimeKind.Utc));

        var result = await Cancel().Handle(
            new CancelMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = entry.Id, Reason = "Deuxième tentative"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelMessagingAllowanceCommandHandler.AlreadyCancelledCode, result.Code);

        // The first motif survives: a second cancellation must not overwrite a colleague's reasoning.
        Assert.Equal("Première annulation", entry.CancelReason);
    }

    // Another practice's allocation is structurally unreachable rather than checked for: the lookup is scoped to the
    // cabinet, so an id from elsewhere is simply « pas dans ce journal ».
    [Fact]
    public async Task An_Allocation_Of_Another_Cabinet_Is_Not_Found()
    {
        _harness.GivenStanding(200, ThisMonth);

        var foreign = _harness.Allowances.Seed(MessagingAllowanceEntry.Create(
            MessagingVendorHarness.OtherClinicId, MessagingAllowanceKind.Standing, 999, ThisMonth,
            new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc)));

        var result = await Cancel().Handle(
            new CancelMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId, EntryId = foreign.Id, Reason = "Tentative"
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CancelMessagingAllowanceCommandHandler.UnknownEntryCode, result.Code);
        Assert.False(foreign.IsCancelled);
    }

    // [AC-9.1] The cabinet is named by id OR by the e-mail of somebody who works there — the companion's own
    // `SubscriptionCabinetLookup` rule, so a verb and the console cannot disagree about what identifies a practice.
    [Fact]
    public async Task A_Cabinet_Can_Be_Named_By_An_Administrators_Email()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                AdminEmail = MessagingVendorHarness.AdminEmail, MessagesPerMonth = 500
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MessagingVendorHarness.ClinicId, result.Value!.ClinicId);
    }

    // Neither figure supplied is a refusal rather than a no-op entry: an allocation of nothing is indistinguishable on
    // screen from one the vendor meant to make.
    [Fact]
    public async Task An_Allocation_With_Neither_Figure_Is_Refused()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand { ClinicId = MessagingVendorHarness.ClinicId },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingAllowancePlan.NoFormError, result.Error);
    }

    // Both figures at once is a refusal too: a standing forfait and a top-up are two records, and guessing which one
    // was meant would write the wrong one into a ledger nobody can edit.
    [Fact]
    public async Task An_Allocation_With_Both_Figures_Is_Refused()
    {
        _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand
            {
                ClinicId = MessagingVendorHarness.ClinicId,
                MessagesPerMonth = 500,
                TopUpMessages = 100,
                AppliesToMonth = ThisMonth
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(MessagingAllowancePlan.BothFormsError, result.Error);
    }

    // A standing forfait of ZERO is legal — « ce cabinet n'envoie pas de rappels WhatsApp » is a decision the vendor may
    // record — and it is not the same state as having no allocation at all (AC-4.3). A guard refusing every zero would
    // make one of the two unrecordable.
    //
    // ⚠️ And it is a LOWERING like any other, so it lands next month: the cabinet keeps the 200 it was already promised
    // for this one. That reading is not obvious — « zéro » sounds like « stop now » — and getting it the other way round
    // would silence a practice's reminders on the afternoon the vendor typed the figure, which is precisely what AC-6.4
    // forbids. Turning it off immediately is a *cancellation* of the standing entry, not a zero.
    [Fact]
    public async Task A_Standing_Forfait_Of_Zero_Is_Recordable_And_Defers_Like_Any_Other_Lowering()
    {
        var (_, month) = _harness.GivenStanding(200, ThisMonth);

        var result = await Grant().Handle(
            new GrantMessagingAllowanceCommand { ClinicId = MessagingVendorHarness.ClinicId, MessagesPerMonth = 0 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.Messages);
        Assert.Equal(NextMonth, result.Value.EffectiveMonth);
        Assert.Equal(200, month.AllowanceMessages);

        // Zero folds to zero next month, never to null: null means « no allowance record », which is our own
        // bookkeeping fault rather than a decision, and the two are held under different reasons and sentences.
        var entries = await _harness.Allowances.GetEntriesAsync(MessagingVendorHarness.ClinicId);
        Assert.Equal(
            0,
            MessagingAllowanceLedger.Fold(
                entries.Select(e => e.ToLedgerEntry()).ToList(), NextMonth));
    }
}
