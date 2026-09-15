using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.TreatmentPlans;

/// <summary>
/// « Cette fiche fait-elle déjà partie d'un traitement ? » — the one answer, shared by the list that
/// <b>offers</b> a séance to continue and the command that <b>accepts</b> one.
///
/// <para>
/// ⚠️ <b>It exists because the two disagreed, and disagreeing was worse than either answer alone.</b>
/// <c>GetContinuableActsQuery</c> excluded cancelled plans — with its own note explaining that a wrong
/// continuation is otherwise permanent — while <c>ContinueRecordedActCommand</c>'s guard applied no status
/// filter at all. So a fiche whose only devis had been cancelled was <i>listed</i> by the dialog and then
/// <i>refused</i> on the press, with « Cette séance fait déjà partie d'un traitement. Ouvrez le devis pour
/// planifier la suite. » — naming a devis that no longer exists and pointing at a screen that cannot help.
/// The dentist's own recovery path (cancel the wrong devis, redo the continuation) was closed by the half of
/// the rule that had not been updated.
/// </para>
///
/// <para>
/// ⚠️ <b>Cancelled is the only status excluded, and the two near-misses are worth naming.</b> A
/// <c>Completed</c> plan genuinely carries the work — the fiche is its evidence — so it must still block, or
/// finishing a treatment would make its own séances continuable again. A <c>Draft</c> blocks too: « Suivre ce
/// traitement » creates an un-numbered plan that is clinically live, so its fiche is tracked whether or not any
/// financial document exists. Only a cancelled devis is void, and only its acts release what they held.
/// </para>
///
/// <para>
/// ⚠️ <b>Steps are matched as well as acts.</b> A stepped act takes its own <c>LinkedDentalRecordId</c> only
/// once its LAST step lands, so a fiche that carried out step 1 of 3 is recorded on the step alone — matching
/// the act's link would miss exactly the multi-séance case this whole feature is about.
/// </para>
/// </summary>
public static class ContinuationTracking
{
    /// <summary>
    /// How far back a séance can be and still be worth continuing. Roughly a clinical quarter — the question is
    /// « la séance de la semaine dernière », not the patient's whole history.
    ///
    /// <para>⚠️ <b>Here rather than in either reader</b>, because there are now two: the per-patient list the
    /// booking dialog offers (<c>GetContinuableActsQuery</c>) and the clinic-wide worklist of acts marked
    /// « non terminé » (<c>GetUnfinishedActsQuery</c>). Two copies of the window would mean an act that the
    /// worklist still chases and the dialog no longer offers — a row whose own button cannot find it.</para>
    /// </summary>
    public const int LookbackDays = 120;

    /// <summary>
    /// A plan whose acts still speak for the fiches they evidence. False for a cancelled devis, which speaks
    /// for nothing.
    /// </summary>
    public static bool Tracks(TreatmentPlan plan) => TracksStatus(plan.Status);

    /// <summary>
    /// The same rule over a bare status, for the callers that have a projection row rather than an aggregate —
    /// <c>NoteCarriedActGuard</c> held two hand-written copies of it.
    /// </summary>
    public static bool TracksStatus(TreatmentPlanStatus status) => TrackingStatuses.Contains(status);

    /// <summary>
    /// The rule as a collection, so a <b>SQL</b> reader can state it too (<c>Contains</c> translates to
    /// <c>IN</c>) — <c>TreatmentPlanLifecycle.LiveStatuses</c>' shape, and for the same reason: the read that
    /// tells a fiche « Suivi comme traitement » ran with no status filter at all, so a cancelled devis's fiches
    /// still rendered a live link into a devis that speaks for nothing.
    /// </summary>
    public static readonly IReadOnlyCollection<TreatmentPlanStatus> TrackingStatuses =
        Enum.GetValues<TreatmentPlanStatus>()
            .Where(s => s != TreatmentPlanStatus.Cancelled)
            .ToArray();

    /// <summary>
    /// Every dental-record id already evidencing an act — or a step — of one of <paramref name="plans"/>.
    /// The shape <c>GetContinuableActsQuery</c> needs, which filters a list against it.
    /// </summary>
    public static HashSet<Guid> TrackedRecordIds(IEnumerable<TreatmentPlan> plans) =>
        plans
            .Where(Tracks)
            .SelectMany(p => p.Items)
            .SelectMany(i => i.Steps
                .Select(s => s.LinkedDentalRecordId)
                .Append(i.LinkedDentalRecordId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

    /// <summary>
    /// Is this one fiche already part of a treatment? The shape <c>ContinueRecordedActCommand</c> needs, and
    /// deliberately expressed over <see cref="TrackedRecordIds"/> rather than beside it — one traversal, one
    /// status rule, no second copy to update.
    /// </summary>
    public static bool IsTracked(IEnumerable<TreatmentPlan> plans, Guid dentalRecordId) =>
        TrackedRecordIds(plans).Contains(dentalRecordId);
}
