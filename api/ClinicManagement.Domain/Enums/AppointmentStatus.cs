namespace ClinicManagement.Domain.Enums;

public enum AppointmentStatus
{
    Scheduled = 1,
    Confirmed = 2,
    InProgress = 3,
    Completed = 4,
    Cancelled = 5,
    NoShow = 6,

    /// <summary>
    /// « Séance passée » — the slot has ended and nobody has said whether the patient came.
    /// <para>
    /// Written only by <c>AppointmentProgressJob</c>'s elapse pass. It answers the <b>presence</b> question and
    /// nothing else: what the visit still owes (a fiche, an encaissement) stays <c>VisitClosureRules</c>'
    /// business, which is why a <c>Completed</c> visit can legitimately still be « à clôturer ».
    /// </para>
    /// <para>
    /// Appended at 7 because the column is an <c>int</c>; members are never reordered. The PostgreSQL exclusion
    /// constraint filters on <c>"Status" NOT IN (5, 6)</c>, so a row here keeps holding its slot with no
    /// migration — which is correct, a visit that happened did occupy the chair.
    /// </para>
    /// </summary>
    AwaitingClosure = 7
}



