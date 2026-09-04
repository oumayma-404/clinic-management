namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Whether a planned act on a treatment plan has been carried out yet.
/// <para>
/// ⚠️ <b>Append-only.</b> Stored as an <c>int</c> (see <c>TreatmentPlanItemConfiguration</c>), so renumbering or
/// reordering a member silently reinterprets every existing row.
/// </para>
/// </summary>
public enum TreatmentPlanItemStatus
{
    Planned = 0,
    Done = 1,

    /// <summary>
    /// Some — but not all — of the act's <see cref="Entities.TreatmentPlanItemStep"/>s are carried out: the
    /// bridge is prepared and the empreinte taken, and the scellement is still to come.
    /// <para>
    /// Only ever reached by an act that <b>has</b> steps, and always <i>derived</i> from them by
    /// <c>TreatmentPlanItem.RecomputeStatusFromSteps</c> — never set by a caller. An act with no steps still
    /// moves <c>Planned → Done</c> exactly as it always did, which is what makes steps additive: every devis
    /// written before they existed reads and behaves identically.
    /// </para>
    /// <para>
    /// The status is <b>stored</b> rather than computed on read, deliberately. « Traitements en cours » filters
    /// on it in SQL, and a domain property over an unloaded collection navigation answers confidently and
    /// wrongly — the failure mode <c>RecoveryCodeLoadingCoverageTests</c> exists for. Its agreement with the
    /// step rows is held by <c>verify-schema</c>'s <c>plan-step-status-agrees</c>.
    /// </para>
    /// </summary>
    InProgress = 2,

    /// <summary>
    /// The patient stopped the treatment and this act was <b>parked</b> rather than deleted: it is no longer
    /// planned work, contributes nothing to <c>TotalPlanned</c> or to any progress count, and cannot be booked
    /// or recorded — but its row, its steps, their <c>DoneDate</c>s and their fiche links all survive.
    /// <para>
    /// ⚠️ It exists because deleting was destroying delivered work. « Arrêter le traitement » removed the acts
    /// it judged unstarted, and that judgement was made on the act's <i>next step</i> — so a bridge with two of
    /// three séances carried out was dropped, taking its step rows, their evidence links and 1 000 DT of quoted
    /// work with it, while the two fiches survived pointing at nothing. Parking keeps every one of those facts,
    /// which is also what makes <c>TreatmentPlan.Reopen</c> possible: patients come back.
    /// </para>
    /// <para>
    /// Reversible through <c>TreatmentPlan.Reopen</c>, which restores every parked act to the status its own
    /// steps derive. Nothing else may set or clear it.
    /// </para>
    /// </summary>
    Withdrawn = 3
}
