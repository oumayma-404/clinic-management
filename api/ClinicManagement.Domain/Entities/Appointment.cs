using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class Appointment : AggregateRoot<Guid>
{
    public Guid ClinicId { get; private set; }
    public Guid? PatientId { get; private set; }
    /// <summary>The practitioner this appointment is booked with — an FK to <see cref="Entities.Doctor"/> (null = unassigned).</summary>
    public Guid? DoctorId { get; private set; }
    public DateTime AppointmentDateTime { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string? DoctorName { get; private set; }
    public string? Notes { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public Guid? RecurringAppointmentId { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? GoogleCalendarEventId { get; private set; }
    public Guid? ProcedureTypeId { get; private set; }
    public int? ProcedureDurationMinutes { get; private set; }
    public string? ProcedureColorHex { get; private set; }
    /// <summary>Optional link to the treatment-plan step this appointment schedules (null for ad-hoc visits).</summary>
    public Guid? TreatmentPlanItemId { get; private set; }

    /// <summary>
    /// True when this appointment was booked <b>outside</b> the practitioner's resolved working hours and the
    /// booker explicitly confirmed the override (AC-P1.31).
    /// <para>
    /// Recorded rather than silently allowed, and available to <b>any role that can book</b> — a secretary
    /// handling an emergency Sunday call must have a path, or the guard simply gets worked around by
    /// falsifying the time, which is worse than an audited exception.
    /// </para>
    /// </summary>
    public bool BookedOutsideWorkingHours { get; private set; }

    /// <summary>Record that this booking was an explicitly-confirmed out-of-hours exception.</summary>
    public void MarkBookedOutsideWorkingHours()
    {
        BookedOutsideWorkingHours = true;
        UpdatedAt = DateTime.UtcNow;
    }

    // Navigation properties
    public Clinic Clinic { get; private set; } = null!;
    public Patient? Patient { get; private set; }
    public Doctor? Doctor { get; private set; }
    public ProcedureType? ProcedureType { get; private set; }

    /// <summary>
    /// The declared legal status moves — **the single authority** on what an appointment may become
    /// (AC-P1.3). Every mutator below asks this table; nothing decides for itself, and the command layer asks
    /// it too instead of carrying a parallel <c>switch</c> that silently fell through to HTTP 200.
    /// <para>
    /// Shape of the lifecycle: a booked visit may be confirmed, started, closed, cancelled or marked absent;
    /// a started visit can no longer be un-started; and the two terminal-looking states are **not** dead ends —
    /// <c>Cancelled → Scheduled</c> is the existing reactivation path, and <c>NoShow → Scheduled</c> is
    /// rebooking a patient who missed their slot.
    /// </para>
    /// <para>
    /// <b><c>Completed → Cancelled</c> is new (AC-P1.5)</b> and is the point of the table: a visit is
    /// auto-completed merely by saving its fiche de soins, so "completed" is reached by accident often enough
    /// that having no way back made a mis-saved fiche permanent. It is the ONLY exit from
    /// <c>Completed</c> — a closed visit cannot be re-opened, only voided.
    /// </para>
    /// </summary>
    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> AllowedTransitions = new()
    {
        [AppointmentStatus.Scheduled] = new[]
        {
            AppointmentStatus.Confirmed, AppointmentStatus.InProgress, AppointmentStatus.Completed,
            AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
        },
        // Deliberately NO Confirmed → Scheduled. "Withdrawing a confirmation" has no clinical meaning (the
        // slot is booked either way) and it was already unreachable: the old command switch's Scheduled arm
        // only ever acted on a Cancelled appointment. Adding it would also need a domain method that does not
        // exist — Reschedule now *preserves* Confirmed (A-2), which is the whole point of that fix.
        [AppointmentStatus.Confirmed] = new[]
        {
            AppointmentStatus.InProgress, AppointmentStatus.Completed,
            AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
        },
        // A visit that has started cannot be un-started; it ends one of three ways.
        [AppointmentStatus.InProgress] = new[]
        {
            AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
        },
        [AppointmentStatus.Completed] = new[] { AppointmentStatus.Cancelled },
        [AppointmentStatus.Cancelled] = new[] { AppointmentStatus.Scheduled },
        // Cancelled stays reachable from NoShow: `CancelRecurringSeriesCommand` voids a whole series and does
        // not skip missed occurrences, so removing it would make that command throw on rows it cancels today.
        [AppointmentStatus.NoShow] = new[] { AppointmentStatus.Scheduled, AppointmentStatus.Cancelled },
    };

    /// <summary>
    /// The statuses this appointment may move to right now. Surfaced on <c>AppointmentDto</c> so the status
    /// control and the « Annuler » button derive their options and their <c>disabled</c> state from the domain
    /// instead of re-deriving a second, drifting copy client-side (AC-P1.6).
    /// </summary>
    public static IReadOnlyCollection<AppointmentStatus> NextStatusesFrom(AppointmentStatus current) =>
        AllowedTransitions.TryGetValue(current, out var allowed) ? allowed : Array.Empty<AppointmentStatus>();

    /// <summary>
    /// True when <paramref name="to"/> is reachable from <paramref name="from"/>. Re-assigning the current
    /// status counts as legal — a UI select can re-emit it, and the mutators treat it as a no-op.
    /// </summary>
    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to) =>
        from == to || NextStatusesFrom(from).Contains(to);

    /// <summary>French stage name, so a refusal names the statuses the way the user sees them.</summary>
    public static string FrenchLabel(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "Planifié",
        AppointmentStatus.Confirmed => "Confirmé",
        AppointmentStatus.InProgress => "En cours",
        AppointmentStatus.Completed => "Terminé",
        AppointmentStatus.Cancelled => "Annulé",
        AppointmentStatus.NoShow => "Absent",
        _ => status.ToString(),
    };

    /// <summary>
    /// Guard every mutator funnels through. Throws a French <see cref="InvalidOperationException"/> naming
    /// **both** statuses (AC-P1.2) — the old messages were English and named neither, so a refusal told the
    /// user nothing about what state their appointment was actually in.
    /// </summary>
    private void EnsureCanTransitionTo(AppointmentStatus target)
    {
        if (!CanTransition(Status, target))
        {
            throw new InvalidOperationException(
                $"Transition impossible : un rendez-vous « {FrenchLabel(Status)} » ne peut pas passer à « {FrenchLabel(target)} ».");
        }
    }

    private Appointment() { } // For EF Core

    public Appointment(
        Guid id,
        Guid clinicId,
        Guid? patientId,
        Guid? doctorId,
        DateTime appointmentDateTime,
        TimeSpan duration,
        string? doctorName = null,
        string? notes = null,
        Guid? recurringAppointmentId = null,
        Guid? procedureTypeId = null,
        int? procedureDurationMinutes = null,
        string? procedureColorHex = null,
        Guid? treatmentPlanItemId = null)
    {
        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        DoctorId = doctorId;
        AppointmentDateTime = appointmentDateTime;
        Duration = duration;
        DoctorName = doctorName;
        Notes = notes;
        Status = AppointmentStatus.Scheduled;
        RecurringAppointmentId = recurringAppointmentId;
        ProcedureTypeId = procedureTypeId;
        ProcedureDurationMinutes = procedureDurationMinutes;
        ProcedureColorHex = procedureColorHex;
        TreatmentPlanItemId = treatmentPlanItemId;
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Confirm a booked visit. Refuses from <c>Completed</c> as well as <c>Cancelled</c> — adjacent defect
    /// <b>A-1</b>: it only checked <c>Cancelled</c>, so a finished visit could be walked back to « Confirmé »,
    /// silently un-completing it for every read that keys off <c>Completed</c> (the plan's act état, the recall
    /// list's <c>lastVisit</c>).
    /// </summary>
    public void Confirm()
    {
        EnsureCanTransitionTo(AppointmentStatus.Confirmed);
        if (Status == AppointmentStatus.Confirmed)
        {
            return;
        }

        Status = AppointmentStatus.Confirmed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Start()
    {
        EnsureCanTransitionTo(AppointmentStatus.InProgress);
        if (Status == AppointmentStatus.InProgress)
        {
            return;
        }

        Status = AppointmentStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Close the visit. Reachable from <c>Scheduled</c> and <c>Confirmed</c> as well as <c>InProgress</c>
    /// (AC-P1.1) — it previously demanded <c>InProgress</c>, which no UI ever sets, so « Terminé » chosen in
    /// the status control hit the command layer's no-op arm and returned HTTP 200 having changed nothing.
    /// </summary>
    public void Complete()
    {
        EnsureCanTransitionTo(AppointmentStatus.Completed);
        if (Status == AppointmentStatus.Completed)
        {
            return;
        }

        Status = AppointmentStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records that the visit happened, because a fiche de soins or a medical document was filed against it.
    /// <para>
    /// Returns an <see cref="VisitCompletionOutcome"/> rather than throwing, and rather than the silent
    /// <c>return</c> it used to do (AC-P1.12). Both callers are **post-commit best-effort** helpers running
    /// after the fiche has already committed, inside a catch that only logs — so a throw here would jump over
    /// <c>CancelPostVisitReviewAsync</c> and leave the post-visit prompt nagging forever, trading a harmless
    /// no-op for a stuck loop. But the two "nothing happened" cases are **not** the same thing and must stop
    /// being collapsed into one:
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="VisitCompletionOutcome.Completed"/> — the visit was open and is now closed.</item>
    /// <item><see cref="VisitCompletionOutcome.AlreadyCompleted"/> — idempotent. A second staff member filing a
    /// record is harmless, and the caller must still cancel the review and broadcast.</item>
    /// <item><see cref="VisitCompletionOutcome.Contradicted"/> — the appointment is <c>Cancelled</c> or
    /// <c>NoShow</c>, so a fiche has been filed against a visit the schedule says did not happen. That is a
    /// real inconsistency for someone to look at, not a no-op to swallow.</item>
    /// </list>
    /// </summary>
    public VisitCompletionOutcome MarkVisitCompleted()
    {
        if (Status == AppointmentStatus.Completed)
        {
            return VisitCompletionOutcome.AlreadyCompleted;
        }

        if (Status == AppointmentStatus.Cancelled || Status == AppointmentStatus.NoShow)
        {
            return VisitCompletionOutcome.Contradicted;
        }

        Status = AppointmentStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
        return VisitCompletionOutcome.Completed;
    }

    /// <summary>
    /// Void the appointment. <b>Now reachable from <c>Completed</c> (AC-P1.5)</b>: a visit is auto-completed
    /// merely by saving its fiche, so a fiche saved against the wrong appointment used to leave that
    /// appointment permanently closed with no way to void it.
    /// </summary>
    public void Cancel(string? reason = null)
    {
        EnsureCanTransitionTo(AppointmentStatus.Cancelled);
        if (Status == AppointmentStatus.Cancelled)
        {
            return;
        }

        Status = AppointmentStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsNoShow()
    {
        EnsureCanTransitionTo(AppointmentStatus.NoShow);
        if (Status == AppointmentStatus.NoShow)
        {
            return;
        }

        Status = AppointmentStatus.NoShow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Move the appointment to a new time.
    /// <para>
    /// <b>Preserves <c>Confirmed</c> and <c>InProgress</c></b> — adjacent defect <b>A-2</b>: it force-set
    /// <c>Scheduled</c>, so moving a visit the patient had already confirmed silently discarded that
    /// confirmation and the desk would chase them for it again.
    /// </para>
    /// <para>
    /// <c>NoShow</c> is deliberately **not** preserved (AC-P1.9): rebooking a patient who missed their slot is
    /// exactly how a no-show is resolved, and carrying the absence onto the new date would mark them absent
    /// from a visit that has not happened yet.
    /// </para>
    /// </summary>
    public void Reschedule(DateTime newDateTime)
    {
        if (Status == AppointmentStatus.Completed)
        {
            throw new InvalidOperationException(
                "Un rendez-vous terminé ne peut pas être déplacé. Annulez-le puis créez-en un nouveau.");
        }

        if (Status == AppointmentStatus.Cancelled)
        {
            throw new InvalidOperationException(
                "Un rendez-vous annulé ne peut pas être déplacé. Réactivez-le d'abord.");
        }

        AppointmentDateTime = newDateTime;
        if (Status == AppointmentStatus.NoShow)
        {
            Status = AppointmentStatus.Scheduled;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Un-cancel a cancelled appointment back to Scheduled at the given time. This is the explicit
    /// reactivation path — <see cref="Reschedule"/> deliberately forbids operating on a cancelled
    /// appointment, so a "reactivate and move" edit routes here instead.
    /// </summary>
    public void Reactivate(DateTime newDateTime)
    {
        if (Status != AppointmentStatus.Cancelled)
            throw new InvalidOperationException("Only a cancelled appointment can be reactivated");

        AppointmentDateTime = newDateTime;
        Status = AppointmentStatus.Scheduled;
        CancellationReason = null;
        CancelledAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDoctorName(string? doctorName)
    {
        DoctorName = doctorName;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Assign (or clear) the practitioner this appointment is booked with.</summary>
    public void SetDoctorId(Guid? doctorId)
    {
        DoctorId = doctorId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("Duration must be greater than zero", nameof(duration));

        Duration = duration;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetGoogleCalendarEventId(string? eventId)
    {
        GoogleCalendarEventId = eventId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetProcedureType(Guid? procedureTypeId, int? procedureDurationMinutes, string? procedureColorHex)
    {
        ProcedureTypeId = procedureTypeId;
        ProcedureDurationMinutes = procedureDurationMinutes;
        ProcedureColorHex = procedureColorHex;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Link (or unlink) the treatment-plan step this appointment schedules.</summary>
    public void SetTreatmentPlanItem(Guid? treatmentPlanItemId)
    {
        TreatmentPlanItemId = treatmentPlanItemId;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsUpcoming()
    {
        return AppointmentDateTime > DateTime.UtcNow &&
               (Status == AppointmentStatus.Scheduled || Status == AppointmentStatus.Confirmed);
    }
}

