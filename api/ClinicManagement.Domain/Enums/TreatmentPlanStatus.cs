namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.TreatmentPlan"/>: Draft (editable devis, deletable) →
/// Accepted (numbered, frozen) → InProgress (first act done or first installment paid) → Completed
/// (all acts done) or Stopped (the patient is not continuing), or Cancelled (motif kept, no further changes).
/// <para>
/// ⚠️ <b>Append-only.</b> Stored as an <c>int</c> (<c>TreatmentPlanConfiguration</c>), so renumbering or
/// reordering a member silently reinterprets every existing row.
/// </para>
/// <para>
/// ⚠️ <b>A new member is invisible to a negative test, and two of them decide money.</b>
/// <c>TreatmentPlanLifecycle.IsLive</c> and <c>PlanBillingRules.CarriesDebt</c> must both classify every
/// member — a status neither list names drops out of « Traitements en cours » <i>and</i> out of « Créances »
/// with no error anywhere. <c>TreatmentPlanStatusCoverageTests</c> is the derived guard that fails on an
/// unclassified member; do not delete it to make a build pass.
/// </para>
/// </summary>
public enum TreatmentPlanStatus
{
    Draft = 0,
    Accepted = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,

    /// <summary>
    /// The patient is not continuing, and work had already been delivered — so the treatment is closed while
    /// the acts that <i>were</i> carried out stay quoted, billable and owed.
    /// <para>
    /// ⚠️ <b>It exists because « Arrêter » used to write <c>Completed</c>.</b> A stopped treatment therefore
    /// wore the badge « Terminé », indistinguishable in the database and on screen from one carried to term —
    /// and since <c>primaryAction</c> tested « facturable » before « terminé », « Reprendre le traitement »
    /// was unreachable anywhere in the product. Measured on the dev database: zero acts had ever been parked,
    /// so nothing had ever exercised the state.
    /// </para>
    /// <para>
    /// Closed clinically (<see cref="Entities.TreatmentPlan"/> refuses a new séance) and <b>open
    /// financially</b>: <c>PlanBillingRules.CarriesDebt</c> includes it, because the delivered work is owed.
    /// Reversible through <c>TreatmentPlan.Reopen</c>, which also restores the parked acts.
    /// </para>
    /// </summary>
    Stopped = 5
}
