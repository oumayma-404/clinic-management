namespace ClinicManagement.Domain.Enums;

/// <summary>
/// What <see cref="Entities.Appointment.MarkVisitCompleted"/> actually did (AC-P1.12).
/// <para>
/// It used to return <c>void</c> and <c>return</c> silently for every state it could not act on, which
/// collapsed two genuinely different situations into one: "already closed, nothing to do" and "a fiche de soins
/// has been filed against a visit the schedule says never happened". The second is an inconsistency someone
/// should see; the first is routine.
/// </para>
/// <para>
/// Deliberately an outcome rather than an exception: both callers are post-commit best-effort helpers running
/// after the fiche has already committed, and a throw would jump over <c>CancelPostVisitReviewAsync</c>,
/// leaving the post-visit prompt nagging forever.
/// </para>
/// </summary>
public enum VisitCompletionOutcome
{
    /// <summary>The visit was open and is now <c>Completed</c>.</summary>
    Completed = 0,

    /// <summary>
    /// Already <c>Completed</c> — nothing changed. Idempotent: a second staff member filing a record is
    /// harmless, and the caller must still cancel the post-visit review and broadcast.
    /// </summary>
    AlreadyCompleted = 1,

    /// <summary>
    /// The appointment is <c>Cancelled</c> or <c>NoShow</c>, so the record contradicts the schedule. The status
    /// is left alone — a cancelled visit is not silently reopened — and the caller surfaces it.
    /// </summary>
    Contradicted = 2,
}
