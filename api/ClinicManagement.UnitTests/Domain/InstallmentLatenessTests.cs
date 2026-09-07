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

    /// <summary>An échéance due today still has the day to run.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Today_And_Later_Are_Never_Late(bool auto)
    {
        Assert.False(Late(Today, isAutoRaised: auto));
        Assert.False(Late(Tomorrow, isAutoRaised: auto));
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
