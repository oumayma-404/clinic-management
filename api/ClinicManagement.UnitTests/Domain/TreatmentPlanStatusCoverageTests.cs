using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-14] The derived guard for <see cref="TreatmentPlanStatus"/>: every member must be <b>deliberately</b>
/// classified by the two rules whose wrong answer is silent.
///
/// <para>
/// ⚠️ <b>Why this file exists.</b> Appending <c>Stopped</c> was a two-line change to the enum and it would
/// have gone wrong in four places, none of which throws:
/// </para>
/// <list type="bullet">
/// <item><c>TreatmentPlanLifecycle.IsLive</c> read « not Cancelled and not Completed », so a stopped treatment
/// came back as <b>live</b> — bookable, listed under « Traitements en cours », ringed on the odontogramme.</item>
/// <item><c>PlanBillingRules.CarriesDebt</c> ended in <c>_ =&gt; false</c>, so a stopped treatment's balance
/// would have vanished from « Créances », « Solde patient », la caisse and the dashboard at once.</item>
/// <item><c>EnsurePayable</c> and <c>EnsureCorrectable</c> are negative lists, so the money the ledger still
/// claimed would have been uncollectable and a fiche recorded by mistake undetachable.</item>
/// </list>
/// <para>
/// The two expected sets below are written out <b>by hand on purpose</b>. The point is that a human looked at
/// the new member and decided, not that a clever expression re-derived the answer from the production code it
/// is supposed to be checking.
/// </para>
/// </summary>
public class TreatmentPlanStatusCoverageTests
{
    /// <summary>Statuses in which a treatment is still being carried out.</summary>
    private static readonly HashSet<TreatmentPlanStatus> ExpectedLive = new()
    {
        TreatmentPlanStatus.Draft,
        TreatmentPlanStatus.Accepted,
        TreatmentPlanStatus.InProgress,
    };

    /// <summary>Statuses in which the patient owes money on the plan.</summary>
    private static readonly HashSet<TreatmentPlanStatus> ExpectedDebtBearing = new()
    {
        TreatmentPlanStatus.Accepted,
        TreatmentPlanStatus.InProgress,
        TreatmentPlanStatus.Completed,
        TreatmentPlanStatus.Stopped,
    };

    /// <summary>The one status that is deliberately in neither set: a cancelled devis is void.</summary>
    private static readonly HashSet<TreatmentPlanStatus> ExpectedNeither = new()
    {
        TreatmentPlanStatus.Cancelled,
    };

    private static TreatmentPlanStatus[] AllStatuses => Enum.GetValues<TreatmentPlanStatus>();

    [Fact]
    public void Every_status_is_deliberately_classified()
    {
        foreach (var status in AllStatuses)
        {
            var classified = ExpectedLive.Contains(status)
                             || ExpectedDebtBearing.Contains(status)
                             || ExpectedNeither.Contains(status);

            Assert.True(classified,
                $"{status} is in none of LiveStatuses, DebtBearingPlanStatuses or the explicit neither-list. "
                + "A status nobody placed vanishes from « Traitements en cours » AND from « Créances » with no "
                + "error anywhere — decide where it belongs rather than deleting this assertion.");
        }
    }

    [Fact]
    public void IsLive_answers_for_every_status_the_way_the_review_decided()
    {
        foreach (var status in AllStatuses)
        {
            Assert.Equal(ExpectedLive.Contains(status), TreatmentPlanLifecycle.IsLive(status));
        }
    }

    [Fact]
    public void The_live_SQL_filter_and_the_in_memory_test_cannot_disagree()
    {
        // LiveStatuses is the `IN (…)` behind « Traitements en cours »; IsLive is what the odontogramme and the
        // booking dialogs ask. A drift would list one set and ring another.
        foreach (var status in AllStatuses)
        {
            Assert.Equal(
                TreatmentPlanLifecycle.LiveStatuses.Contains(status),
                TreatmentPlanLifecycle.IsLive(status));
        }
    }

    [Fact]
    public void CarriesDebt_answers_for_every_status_the_way_the_review_decided()
    {
        foreach (var status in AllStatuses)
        {
            Assert.Equal(ExpectedDebtBearing.Contains(status), PlanBillingRules.CarriesDebt(status));
        }
    }

    [Fact]
    public void The_debt_SQL_filter_and_the_in_memory_test_cannot_disagree()
    {
        foreach (var status in AllStatuses)
        {
            Assert.Equal(
                PlanBillingRules.DebtBearingPlanStatuses.Contains(status),
                PlanBillingRules.CarriesDebt(status));
        }
    }

    [Fact]
    public void A_stopped_treatment_is_closed_clinically_and_open_financially()
    {
        // The whole reason the status exists, as one sentence: the work stops, the money does not.
        Assert.False(TreatmentPlanLifecycle.IsLive(TreatmentPlanStatus.Stopped));
        Assert.True(PlanBillingRules.CarriesDebt(TreatmentPlanStatus.Stopped));
    }

    [Fact]
    public void Stopped_is_appended_and_is_not_Completed()
    {
        // « Arrêter » wrote Completed until 2026-09-09, which is what made the two indistinguishable; and the
        // enum persists as an int, so the value may never be renumbered.
        Assert.NotEqual(TreatmentPlanStatus.Completed, TreatmentPlanStatus.Stopped);
        Assert.Equal(5, (int)TreatmentPlanStatus.Stopped);
    }
}
