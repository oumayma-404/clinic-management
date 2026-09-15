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
    Stopped = 5,

    /// <summary>
    /// The balance has been abandoned: the practice has decided it will never be collected, and says so once
    /// rather than leaving a créance standing for ever.
    /// <para>
    /// ⚠️ <b>It exists because there was no third answer.</b> A patient who dies, emigrates or simply cannot
    /// pay leaves a live <c>Stopped</c> devis, and <c>CarriesDebt(Stopped)</c> is true — correctly, the work
    /// was delivered — so the amount sat in « Créances », in « Solde patient », on the dashboard and in
    /// « Chèques à encaisser » indefinitely. The invoice track has the avoir for this; the devis track had only
    /// <c>Cancel</c>, which is the wrong instrument twice over: it is refused outright once any money has been
    /// collected, and it removes the whole document from la caisse, rewriting days that are already closed.
    /// </para>
    /// <para>
    /// ⚠️ <b>What is written off is the OUTSTANDING balance, never the cash.</b> Payments already taken stay
    /// exactly where they are — they really were received, and la caisse's past days must not move. So
    /// <c>CarriesDebt</c> is false (nothing more is owed) while <c>AmountPaid</c>, the ledger rows and every
    /// receipt are untouched. That is the whole difference from <see cref="Cancelled"/>.
    /// </para>
    /// <para>
    /// Closed clinically (<c>IsLive</c> false — no new séance) and closed financially. Reversible through
    /// <c>TreatmentPlan.Reopen</c>, which is how a write-off entered by mistake is undone: a status nothing can
    /// leave is the defect <c>Uncancel</c> exists for, and appending a second absorbing state would repeat it.
    /// </para>
    /// </summary>
    WrittenOff = 6
}
