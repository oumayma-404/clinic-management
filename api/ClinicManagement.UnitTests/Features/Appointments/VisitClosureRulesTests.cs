using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// « À clôturer » — the rule the whole feature rests on. Pure, so the truth table is the test.
///
/// <para>These cases are the design, not coverage: which visits are in scope at all, that the three gaps
/// <b>cascade</b> rather than stack, and each of the four ways the money question can be closed.</para>
/// </summary>
public class VisitClosureRulesTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    /// <summary>A one-hour visit that ended two hours ago, with every gap open unless overridden.</summary>
    private static VisitClosureInput Visit(
        AppointmentStatus status = AppointmentStatus.InProgress,
        Guid? patientId = null,
        DateTime? startUtc = null,
        TimeSpan? duration = null,
        bool hasFiche = false,
        decimal? ficheCost = null,
        bool hasLiveInvoice = false,
        bool coveredByPlan = false,
        bool nothingToBill = false) =>
        new(
            AppointmentId: Guid.NewGuid(),
            PatientId: patientId ?? PatientId,
            Status: status,
            StartUtc: startUtc ?? Now.AddHours(-3),
            Duration: duration ?? TimeSpan.FromHours(1),
            HasFiche: hasFiche,
            FicheCost: ficheCost,
            HasLiveInvoice: hasLiveInvoice,
            CoveredByPlan: coveredByPlan,
            NothingToBill: nothingToBill);

    // ------------------------------------------------------------------ scope

    // The whole point of the window: a visit still running is not late, it is happening.
    [Fact]
    public void A_Visit_Still_Inside_Its_Slot_Is_Not_Closable()
    {
        var running = Visit(startUtc: Now.AddMinutes(-10), duration: TimeSpan.FromHours(1));

        Assert.False(VisitClosureRules.IsClosable(running, Now));
    }

    // The exact boundary, and the reason the end-of-slot test cannot live in SQL: `Duration` is ticks behind a
    // value converter, so this comparison only exists here.
    [Fact]
    public void A_Visit_Whose_Slot_Has_Just_Ended_Is_Closable()
    {
        var justEnded = Visit(startUtc: Now.AddHours(-1), duration: TimeSpan.FromHours(1));

        Assert.True(VisitClosureRules.IsClosable(justEnded, Now));
    }

    // A « créneau occupé » — a blocked slot with no patient — has nothing to close.
    [Fact]
    public void A_Patientless_Busy_Slot_Is_Not_Closable()
    {
        Assert.False(VisitClosureRules.IsClosable(Visit() with { PatientId = null }, Now));
    }

    // Both are COMPLETE ANSWERS about a visit that did not happen, not gaps. Excluding them here rather than
    // treating them as satisfied is what stops the reader ever reporting a cancelled visit as « closed », which
    // would invite a caller to count it as work done.
    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public void A_Cancelled_Or_Missed_Visit_Is_Not_Closable(AppointmentStatus status)
    {
        Assert.False(VisitClosureRules.IsClosable(Visit(status: status), Now));
    }

    // ------------------------------------------------------------------ the cascade

    // The single most important behaviour: a row asks ONE question, and it is the first unanswered one.
    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    public void Presence_Is_Asked_First_Whatever_Else_Is_Missing(AppointmentStatus status)
    {
        var state = VisitClosureRules.Evaluate(Visit(status: status));

        Assert.False(state.PresenceAnswered);
        Assert.Equal(VisitClosureStep.Presence, state.NextStep);
    }

    // `InProgress` is the DOMINANT status in a real clinic's first list: the minutely pass auto-starts every visit
    // and nothing has ever closed one. If this ever reads as « answered », the feature has no work to do.
    [Fact]
    public void InProgress_Past_Its_Slot_Means_Nobody_Has_Said_Whether_The_Patient_Came()
    {
        Assert.False(VisitClosureRules.Evaluate(Visit(status: AppointmentStatus.InProgress)).PresenceAnswered);
    }

    // A visit nobody has confirmed happened is not « missing » a fiche — asking would be nagging about something
    // that cannot be answered yet.
    [Fact]
    public void The_Fiche_Is_Not_Asked_Before_Presence_Is_Answered()
    {
        var state = VisitClosureRules.Evaluate(Visit(status: AppointmentStatus.Scheduled, hasFiche: false));

        Assert.NotEqual(VisitClosureStep.Fiche, state.NextStep);
    }

    [Fact]
    public void Once_The_Patient_Came_The_Fiche_Is_The_Next_Question()
    {
        var state = VisitClosureRules.Evaluate(Visit(status: AppointmentStatus.Completed));

        Assert.True(state.PresenceAnswered);
        Assert.Equal(VisitClosureStep.Fiche, state.NextStep);
    }

    // A séance with no fiche has no acts to price, so the money question comes last.
    [Fact]
    public void Money_Is_The_Last_Question_And_Only_Once_A_Fiche_Exists()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 120m));

        Assert.True(state.PresenceAnswered);
        Assert.True(state.FicheRecorded);
        Assert.False(state.BillingSettled);
        Assert.Equal(VisitClosureStep.Billing, state.NextStep);
    }

    // ------------------------------------------------------------------ the four ways money closes

    [Fact]
    public void A_Live_Invoice_Closes_The_Money_Question()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 120m, hasLiveInvoice: true));

        Assert.True(state.BillingSettled);
        Assert.Null(state.NextStep);
        Assert.False(state.IsOpen);
    }

    // A contrôle gratuit is complete work with no document to raise — derived, so nobody has to dismiss it.
    [Fact]
    public void A_Fiche_Worth_Nothing_Closes_The_Money_Question()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 0m));

        Assert.True(state.BillingSettled);
        Assert.Null(state.NextStep);
    }

    // The money lives on the échéancier; counting it here as well would double it in the reader's eyes.
    [Fact]
    public void A_Seance_Carried_By_A_Devis_Closes_The_Money_Question()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 300m, coveredByPlan: true));

        Assert.True(state.BillingSettled);
    }

    // The escape hatch of last resort — the only one of the four that a human had to type.
    [Fact]
    public void A_Recorded_Nothing_To_Bill_Closes_The_Money_Question()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 80m, nothingToBill: true));

        Assert.True(state.BillingSettled);
        Assert.Null(state.NextStep);
    }

    // ⚠️ `null` (no fiche at all) and `0` (a fiche worth nothing) are opposite facts and must not collapse: a
    // missing fiche closing the money question would take the séance off the list with nothing recorded at all.
    [Fact]
    public void No_Fiche_Is_Not_The_Same_As_A_Fiche_Worth_Nothing()
    {
        var noFiche = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: false, ficheCost: null));

        Assert.False(noFiche.BillingSettled);
        Assert.Equal(VisitClosureStep.Fiche, noFiche.NextStep);
    }

    // ------------------------------------------------------------------ closed

    [Fact]
    public void A_Fully_Answered_Visit_Is_Closed()
    {
        var state = VisitClosureRules.Evaluate(
            Visit(status: AppointmentStatus.Completed, hasFiche: true, ficheCost: 120m, hasLiveInvoice: true));

        Assert.True(state.PresenceAnswered);
        Assert.True(state.FicheRecorded);
        Assert.True(state.BillingSettled);
        Assert.False(state.IsOpen);
    }
}
