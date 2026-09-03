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
    InProgress = 2
}
