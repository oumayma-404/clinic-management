using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// « Cette échéance est-elle en retard ? »
///
/// <para>
/// Before <see cref="InstallmentLateness"/> the two surfaces that answered it each wrote
/// <c>isBeforeToday(dueDate)</c>, which flagged <b>25 of 27</b> unpaid rows on the dev database — every devis
/// went red the day after it was signed, cancelled and already-invoiced ones included. The cause was never the
/// badge: <c>TreatmentPlan.Accept</c> raises a lump-sum row for the whole total dated at the acceptance instant
/// when no schedule was given.
/// </para>
/// </summary>
public class InstallmentLatenessTests
{
    private static readonly DateTime Today = new(2026, 9, 7);
    private static readonly DateTime Yesterday = new(2026, 9, 6);
    private static readonly DateTime Tomorrow = new(2026, 9, 8);

    private static bool Late(
        DateTime dueDate,
        bool isAutoRaised = false,
        bool isPaid = false,
        TreatmentPlanStatus status = TreatmentPlanStatus.InProgress,
        bool billed = false,
        bool unrealisedWork = false) =>
        InstallmentLateness.IsLate(isPaid, isAutoRaised, dueDate, status, billed, unrealisedWork, Today);

    /// <summary>The whole point: an agreed date that has passed still goes red.</summary>
    [Fact]
    public void An_Agreed_Date_That_Has_Passed_Is_Late()
    {
        Assert.True(Late(Yesterday));
    }

    /// <summary>
    /// ⚠️ <b>And it stays red while the treatment is still under way.</b> The withholding below applies to the
    /// auto-raised row ONLY — a schedule a dentist typed says when the money is due whatever the clinical state,
    /// and silencing it would destroy the only thing an échéancier is for.
    /// </summary>
    [Fact]
    public void An_Agreed_Date_Is_Late_Even_With_Work_Outstanding()
    {
        Assert.True(Late(Yesterday, unrealisedWork: true));
    }

    /// <summary>An échéance <b>somebody agreed</b> and due today still has the day to run.</summary>
    [Fact]
    public void An_Agreed_Date_Today_Or_Later_Is_Never_Late()
    {
        Assert.False(Late(Today));
        Assert.False(Late(Tomorrow));
    }

    /// <summary>
    /// ⚠️ <b>An auto-raised row's own date is never consulted, and this used to be a <c>[Theory]</c> asserting
    /// the opposite for it.</b>
    ///
    /// <para>
    /// That row's <c>DueDate</c> is the acceptance instant — a value <c>Accept</c> has to write because a
    /// payment needs somewhere to attach, not a day anybody agreed. Comparing it made « en retard » mean « this
    /// devis was signed before today », which is true of every devis; the <c>unrealisedWork</c> short-circuit
    /// was a patch over that rather than the rule. The rule is: <b>the work is finished and the balance is
    /// unpaid</b>, whatever date the container row happens to carry. The frontend renders it as
    /// « Solde à régler » with no date for the same reason — a fabricated date must be neither shown nor read.
    /// </para>
    /// </summary>
    [Fact]
    public void An_Auto_Raised_Row_Ignores_Its_Own_Date()
    {
        // Work finished: late whatever the date says — yesterday, today, or a date still in the future.
        Assert.True(Late(Yesterday, isAutoRaised: true));
        Assert.True(Late(Today, isAutoRaised: true));
        Assert.True(Late(Tomorrow, isAutoRaised: true));

        // Work outstanding: never late, again whatever the date says.
        Assert.False(Late(Yesterday, isAutoRaised: true, unrealisedWork: true));
        Assert.False(Late(Tomorrow, isAutoRaised: true, unrealisedWork: true));
    }

    /// <summary>
    /// The user-reported case: an auto-raised row on a treatment whose remaining séances have not happened.
    /// Money for work not yet done cannot be late.
    /// </summary>
    [Fact]
    public void An_Auto_Raised_Row_Waits_For_The_Work()
    {
        Assert.False(Late(Yesterday, isAutoRaised: true, unrealisedWork: true));
    }

    /// <summary>
    /// …but once every act is carried out, the total IS due and an unpaid balance is genuinely late. Otherwise
    /// the fix would silence the collections that actually need chasing.
    /// </summary>
    [Fact]
    public void An_Auto_Raised_Row_Goes_Late_Once_The_Work_Is_Finished()
    {
        Assert.True(Late(Yesterday, isAutoRaised: true, unrealisedWork: false));
    }

    /// <summary>A devis a note d'honoraires represents: its échéancier can no longer receive money at all.</summary>
    [Fact]
    public void A_Billed_Devis_Is_Never_Late()
    {
        Assert.False(Late(Yesterday, billed: true));
        Assert.False(Late(Yesterday, billed: true, isAutoRaised: true));
    }

    /// <summary>
    /// A status that carries no debt owes nothing — gated through <see cref="PlanBillingRules.CarriesDebt"/>
    /// rather than by listing statuses a second time.
    /// </summary>
    [Theory]
    [InlineData(TreatmentPlanStatus.Draft)]
    [InlineData(TreatmentPlanStatus.Cancelled)]
    public void A_Plan_That_Owes_Nothing_Is_Never_Late(TreatmentPlanStatus status)
    {
        Assert.False(Late(Yesterday, status: status));
    }

    /// <summary>Completed carries debt — treatment routinely finishes before the last échéance is collected.</summary>
    [Fact]
    public void A_Completed_Plan_Still_Owes_Its_Balance()
    {
        Assert.True(Late(Yesterday, status: TreatmentPlanStatus.Completed));
    }

    [Fact]
    public void A_Paid_Row_Is_Never_Late()
    {
        Assert.False(Late(Yesterday, isPaid: true));
    }

    /// <summary>
    /// The clinic's own day decides, never the server's: for the first hour of every Tunisian day the UTC date
    /// is yesterday, and an échéance due today would read « en retard ».
    /// </summary>
    [Fact]
    public void The_Comparison_Ignores_The_Time_Of_Day()
    {
        Assert.False(InstallmentLateness.IsLate(
            isPaid: false, isAutoRaised: false, dueDate: new DateTime(2026, 9, 7, 23, 30, 0),
            TreatmentPlanStatus.InProgress, planIsBilled: false, planHasUnrealisedWork: false,
            clinicToday: new DateTime(2026, 9, 7, 0, 30, 0)));
    }
}
