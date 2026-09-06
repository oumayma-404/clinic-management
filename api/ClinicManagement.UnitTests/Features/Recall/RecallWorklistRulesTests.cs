using ClinicManagement.Application.Features.Recall;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Recall;

/// <summary>
/// The « à rappeler » worklist rules. The list used to answer one question — "not seen for the recall interval" —
/// which for a perio/implant practice is the least informative of the reasons to call: a patient seen last week who
/// stopped halfway through an accepted devis is both lost revenue and an unfinished surgical case, and no
/// time-since-visit rule can surface them.
///
/// <para>These are pure rules, so this class is the whole test surface for <b>which</b> reasons apply. The handler
/// tests cover gathering and ordering.</para>
/// </summary>
public class RecallWorklistRulesTests
{
    private static readonly DateTime Now = new(2026, 7, 29, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PatientId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static RecallPlanFact Plan(
        TreatmentPlanStatus status,
        DateTime createdAt,
        DateTime? acceptedDate = null,
        int total = 6,
        int done = 2,
        string? number = "2026-0007") =>
        new(PatientId, Guid.NewGuid(), number, status, createdAt, acceptedDate, total, done);

    private static IReadOnlyList<RecallReason> Reasons(
        DateTime anchor,
        IEnumerable<RecallPlanFact>? plans = null,
        DateTime? oldestOverdue = null,
        decimal outstanding = 0m,
        int interval = 6) =>
        RecallWorklistRules.ReasonsFor(
            anchor, interval, plans ?? Array.Empty<RecallPlanFact>(), oldestOverdue, outstanding, Now);

    // ---- the reason that could never be surfaced before -------------------------------------------------

    // The headline case for this whole change: seen ONE DAY ago, so the old rule says nothing, but there is an
    // accepted devis with four acts left and nothing booked. That patient is exactly who a specialist practice
    // needs to call.
    [Fact]
    public void A_Stalled_Devis_Surfaces_A_Patient_Seen_Yesterday()
    {
        var seenYesterday = Now.AddDays(-1);
        var accepted = Now.AddDays(-60);

        var reasons = Reasons(seenYesterday, new[] { Plan(TreatmentPlanStatus.Accepted, accepted, accepted) });

        Assert.Single(reasons);
        Assert.Equal(RecallReasonKind.StalledPlan, reasons[0].Kind);
        Assert.Equal(accepted, reasons[0].DueSince);
        // And the old rule alone would have produced nothing at all.
        Assert.Empty(Reasons(seenYesterday));
    }

    // A plan accepted this morning is not stalled — its next séance simply has not been booked yet. Without the
    // grace period every freshly-accepted devis would appear the moment it was signed.
    [Fact]
    public void A_Freshly_Accepted_Devis_Is_Not_Stalled()
    {
        var accepted = Now.AddDays(-(RecallWorklistRules.StalledPlanGraceDays - 1));

        Assert.Empty(Reasons(Now.AddDays(-1), new[] { Plan(TreatmentPlanStatus.Accepted, accepted, accepted) }));
    }

    // A devis whose acts are all done has nothing left to chase, however old it is.
    [Fact]
    public void A_Devis_With_Every_Act_Done_Is_Not_Stalled()
    {
        var accepted = Now.AddDays(-200);

        Assert.Empty(Reasons(
            Now.AddDays(-1),
            new[] { Plan(TreatmentPlanStatus.Accepted, accepted, accepted, total: 4, done: 4) }));
    }

    // InProgress counts too — it is the state a plan enters on its first act, and the one most likely to stall.
    [Fact]
    public void An_InProgress_Devis_Can_Stall()
    {
        var accepted = Now.AddDays(-90);

        var reasons = Reasons(Now.AddDays(-1), new[] { Plan(TreatmentPlanStatus.InProgress, accepted, accepted) });

        Assert.Equal(RecallReasonKind.StalledPlan, Assert.Single(reasons).Kind);
    }

    // Acceptance date, not creation date: a devis drafted in January and accepted yesterday is not stalled.
    [Fact]
    public void Stalled_Is_Measured_From_Acceptance_Not_Creation()
    {
        var created = Now.AddDays(-200);
        var acceptedYesterday = Now.AddDays(-1);

        Assert.Empty(Reasons(
            Now.AddDays(-1),
            new[] { Plan(TreatmentPlanStatus.Accepted, created, acceptedYesterday) }));
    }

    // ---- unanswered devis -------------------------------------------------------------------------------

    // ⚠️ This case used to assert `UnansweredDevis` and it was rewritten rather than deleted, because the fixture
    // it describes changed meaning underneath it. « Suivre ce traitement » makes an un-numbered `Draft` a LIVE
    // treatment, so a Draft with acts left is a treatment somebody stopped coming to — « au point mort » — and
    // not a quote awaiting an answer. The old assertion froze the reading that made the worklist chase a patient
    // about a devis they were never shown.
    [Fact]
    public void A_Followed_Treatment_With_Acts_Left_Is_Stalled_Rather_Than_Unanswered()
    {
        var created = Now.AddDays(-(RecallWorklistRules.StalledPlanGraceDays + 1));

        var reasons = Reasons(
            Now.AddDays(-1),
            new[] { Plan(TreatmentPlanStatus.Draft, created, number: null) });

        var reason = Assert.Single(reasons);
        Assert.Equal(RecallReasonKind.StalledPlan, reason.Kind);
        Assert.Equal(created, reason.DueSince);
        // With no devis number the row still has to say something useful — the progress stands in for it.
        Assert.Equal("2/6", reason.Detail);
    }

    // The gap this closes, stated as the patient rather than as the rule: a bridge followed without a devis, two
    // séances in, nobody coming back. `AdvanceAfterWorkRecorded` keeps such a plan a `Draft` however much work it
    // records, so the old `Accepted || InProgress` test never saw it — and the recall worklist exists precisely
    // for this patient.
    [Fact]
    public void A_Half_Finished_Followed_Treatment_Reaches_The_Worklist()
    {
        var reasons = Reasons(
            Now.AddDays(-1),
            new[]
            {
                Plan(
                    TreatmentPlanStatus.Draft,
                    Now.AddDays(-90),
                    total: 3,
                    done: 2,
                    number: null),
            });

        Assert.Equal(RecallReasonKind.StalledPlan, Assert.Single(reasons).Kind);
    }

    // The other half of the exclusivity: a Draft with nothing left to do is not stalled, so the unanswered-devis
    // reason is still reachable and a plan is never reported under two contradictory reasons at once.
    [Fact]
    public void A_Draft_With_No_Acts_Left_Is_Unanswered_And_Not_Also_Stalled()
    {
        var created = Now.AddDays(-(RecallWorklistRules.UnansweredDevisGraceDays + 1));

        var reasons = Reasons(
            Now.AddDays(-1),
            new[] { Plan(TreatmentPlanStatus.Draft, created, total: 6, done: 6) });

        var reason = Assert.Single(reasons);
        Assert.Equal(RecallReasonKind.UnansweredDevis, reason.Kind);
        Assert.Equal(created, reason.DueSince);
    }

    [Fact]
    public void A_Draft_Devis_Inside_The_Grace_Period_Is_Not_Chased()
    {
        Assert.Empty(Reasons(Now.AddDays(-1), new[] { Plan(TreatmentPlanStatus.Draft, Now.AddDays(-2)) }));
    }

    // ---- money ------------------------------------------------------------------------------------------

    [Fact]
    public void An_Overdue_Installment_Is_A_Reason_On_Its_Own()
    {
        var overdueSince = Now.AddDays(-20);

        var reasons = Reasons(Now.AddDays(-1), oldestOverdue: overdueSince, outstanding: 300.500m);

        var reason = Assert.Single(reasons);
        Assert.Equal(RecallReasonKind.OverdueInstallment, reason.Kind);
        Assert.Equal(overdueSince, reason.DueSince);
        Assert.Equal("300.500", reason.Detail);
    }

    // ---- composition & ordering -------------------------------------------------------------------------

    // Money leads, then the stalled surgical case, then the unanswered quote, then the routine check-up. The enum's
    // declaration order IS this ordering, so a reordering of the enum is a behaviour change.
    [Fact]
    public void All_Four_Reasons_Compose_Most_Urgent_First()
    {
        var old = Now.AddDays(-300);

        var reasons = Reasons(
            anchor: old,
            plans: new[]
            {
                Plan(TreatmentPlanStatus.Accepted, old, old),
                // `done == total`, so this one is unanswered rather than stalled — the two reasons are mutually
                // exclusive now, and composing them needs a plan genuinely in each state.
                Plan(TreatmentPlanStatus.Draft, old, total: 6, done: 6, number: "2026-0009")
            },
            oldestOverdue: Now.AddDays(-10),
            outstanding: 500m);

        Assert.Equal(
            new[]
            {
                RecallReasonKind.OverdueInstallment,
                RecallReasonKind.StalledPlan,
                RecallReasonKind.UnansweredDevis,
                RecallReasonKind.OverdueVisit
            },
            reasons.Select(r => r.Kind).ToArray());
    }

    // The original rule still works on its own — this change adds reasons, it does not replace the old one.
    [Fact]
    public void An_Overdue_Visit_Alone_Still_Puts_A_Patient_On_The_List()
    {
        var reasons = Reasons(Now.AddMonths(-8));

        Assert.Equal(RecallReasonKind.OverdueVisit, Assert.Single(reasons).Kind);
    }

    // A patient with nothing pending and a recent visit is not on the list at all — the list must not become
    // "everyone".
    [Fact]
    public void A_Recently_Seen_Patient_With_Nothing_Pending_Has_No_Reasons()
    {
        Assert.Empty(Reasons(Now.AddDays(-3)));
    }

    // Two stalled devis produce two reasons — the row shows both rather than collapsing to one.
    [Fact]
    public void Multiple_Stalled_Devis_Each_Produce_A_Reason()
    {
        var old = Now.AddDays(-100);

        var reasons = Reasons(
            Now.AddDays(-1),
            new[]
            {
                Plan(TreatmentPlanStatus.Accepted, old, old, number: "2026-0001"),
                Plan(TreatmentPlanStatus.InProgress, old, old, number: "2026-0002")
            });

        Assert.Equal(2, reasons.Count);
        Assert.All(reasons, r => Assert.Equal(RecallReasonKind.StalledPlan, r.Kind));
    }

    // Within one kind, the longest-waiting comes first.
    [Fact]
    public void Within_A_Kind_The_Longest_Waiting_Comes_First()
    {
        var older = Now.AddDays(-200);
        var newer = Now.AddDays(-40);

        var reasons = Reasons(
            Now.AddDays(-1),
            new[]
            {
                Plan(TreatmentPlanStatus.Accepted, newer, newer, number: "newer"),
                Plan(TreatmentPlanStatus.Accepted, older, older, number: "older")
            });

        Assert.Equal("older", reasons[0].Detail);
        Assert.Equal("newer", reasons[1].Detail);
    }
}
