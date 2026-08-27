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
    /// <summary>
    /// The visit's <b>lead</b> act. Since multi-act séances exist this is a **derived snapshot of the first
    /// <see cref="Procedures"/> row**, not an independent field — see <see cref="SetProcedures"/>.
    /// </summary>
    public Guid? ProcedureTypeId { get; private set; }
    public int? ProcedureDurationMinutes { get; private set; }
    public string? ProcedureColorHex { get; private set; }
    /// <summary>
    /// Optional link to the treatment-plan step this appointment schedules (null for ad-hoc visits).
    /// <para>
    /// With several acts in one séance this is the **first** linked step; the complete set lives on
    /// <see cref="Procedures"/>, and <see cref="LinkedTreatmentPlanItemIds"/> is what a read should ask.
    /// </para>
    /// </summary>
    public Guid? TreatmentPlanItemId { get; private set; }

    private readonly List<AppointmentProcedure> _procedures = new();

    /// <summary>
    /// Every act booked into this séance, in the order the dentist listed them. Empty on a « créneau occupé » or
    /// an appointment booked with no act at all — both legitimate, so an empty list is a real state and not a
    /// missing one.
    /// </summary>
    public IReadOnlyCollection<AppointmentProcedure> Procedures =>
        _procedures.OrderBy(p => p.SequenceNumber).ToList().AsReadOnly();

    /// <summary>
    /// Every treatment-plan act this visit carries out — the child links, plus the parent scalar for rows written
    /// before the collection existed (and never migrated because their appointment carried no procedure).
    /// <para>
    /// This is what the devis read-back must group on. Reading <see cref="TreatmentPlanItemId"/> alone was correct
    /// only while one visit meant one act: group three acts into a séance and two of them would report
    /// « À planifier » forever, offering to book a visit that already exists.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<Guid> LinkedTreatmentPlanItemIds =>
        _procedures.Where(p => p.TreatmentPlanItemId.HasValue)
            .Select(p => p.TreatmentPlanItemId!.Value)
            .Concat(TreatmentPlanItemId.HasValue ? new[] { TreatmentPlanItemId.Value } : Array.Empty<Guid>())
            .Distinct()
            .ToList();

    /// <summary>Sum of the booked acts' own durations — the default a multi-act séance should last.</summary>
    public int TotalProcedureDurationMinutes =>
        _procedures.Sum(p => p.DurationMinutes ?? 0);

    /// <summary>
    /// This booking deliberately overlaps another for the same practitioner, confirmed by the user.
    ///
    /// <para>⚠️ It once had an out-of-hours twin, deleted by <c>adoption-gaps-remediation</c> (AC-25): that flag
    /// was written by four call sites and read by <b>nothing</b> — no query, no DTO, no screen, no constraint —
    /// for its entire life, so the « audited exception » it claimed to record was unauditable. The out-of-hours
    /// <i>permission</i> is unaffected and still travels on the commands as <c>AllowOutsideWorkingHours</c>; only
    /// the write-only column went. This flag is genuinely different, and the paragraph below is the difference:
    /// the database reads it.</para>
    ///
    /// <para>It exists because a
    /// double-booking is sometimes real work, not a mistake. A second chair, an assistant taking the impression
    /// while the dentist starts next door, an emergency squeezed into an occupied slot — a clinic does all three,
    /// and a hard refusal makes the software describe a day the practice is not having.</para>
    ///
    /// <para>It is also <b>load-bearing at the database level</b>: the <c>EX_Appointments_NoDoubleBooking</c>
    /// exclusion constraint excludes acknowledged rows from its predicate, so this flag is what makes the write
    /// possible at all. That is deliberate — the constraint still blocks every <i>accidental</i> double-booking,
    /// and a deliberate one is recorded as deliberate rather than the protection being dropped wholesale.</para>
    /// </summary>
    public bool BookedWithOverlap { get; private set; }

    /// <summary>Record that this booking was an explicitly-confirmed double-booking.</summary>
    public void MarkBookedWithOverlap()
    {
        BookedWithOverlap = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Clear the acknowledged-overlap flag. Called when a booking is moved to a slot that no longer collides, so a
    /// once-deliberate overlap does not keep its database exemption forever — otherwise an appointment rescheduled
    /// into a free slot would remain permanently exempt from the double-booking constraint.
    /// </summary>
    public void ClearOverlapAcknowledgement()
    {
        if (!BookedWithOverlap) return;
        BookedWithOverlap = false;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// When somebody recorded that this visit will raise no document — the escape hatch of « à clôturer ».
    ///
    /// <para><b>It is the LAST resort, not the first.</b> Three legitimate « rien à facturer » cases are derived
    /// and must stay derived: a fiche whose <c>Cost</c> is zero (contrôle gratuit), a séance carrying a
    /// treatment-plan link (the money lives on the échéancier), and a non-cancelled invoice (already billed). A
    /// patient who will pay later is not one of them either — that is an issued note with an outstanding balance,
    /// i.e. a créance, which the product already models. What is left is the case none of those describe, and it
    /// needs somewhere to go: without it a row nothing can satisfy stays flagged for ever, and an alarm that is
    /// always on is one nobody reads.</para>
    ///
    /// <para>Recorded, never inferred — the motif is mandatory precisely so « pourquoi cette séance n'a produit
    /// aucun document ? » stays answerable months later.</para>
    /// </summary>
    public DateTime? NothingToBillAtUtc { get; private set; }

    /// <inheritdoc cref="NothingToBillAtUtc"/>
    public string? NothingToBillReason { get; private set; }

    /// <inheritdoc cref="NothingToBillAtUtc"/>
    public string? NothingToBillByUserId { get; private set; }

    /// <summary>True when this visit has been recorded as raising no document.</summary>
    public bool IsNothingToBill => NothingToBillAtUtc.HasValue;

    /// <summary>
    /// Record that this visit will raise no note d'honoraires. <b>Idempotent</b>: re-marking an already-marked
    /// visit keeps the first motif and the first author, because the second caller is a double-click far more
    /// often than a considered change of mind, and overwriting would erase a colleague's reasoning with no trace.
    /// Changing a motif is <see cref="ClearNothingToBill"/> then this again — both are ordinary audited writes.
    /// </summary>
    /// <exception cref="ArgumentException">The motif is blank.</exception>
    public void MarkNothingToBill(string reason, string userId, DateTime whenUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif est obligatoire.", nameof(reason));
        }

        if (NothingToBillAtUtc.HasValue)
        {
            return;
        }

        NothingToBillAtUtc = whenUtc;
        NothingToBillReason = reason.Trim();
        NothingToBillByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Withdraw the « rien à facturer » note, putting the visit back on the worklist.
    ///
    /// <para>This exists because the mark is a claim about money that can turn out to be wrong — the patient does
    /// owe something after all — and a claim nobody can withdraw is one people stop making. It is <b>not</b> a
    /// counter-example to « record yes, erase no »: that rule is about the clinical record, and nothing clinical
    /// is destroyed here. Idempotent.</para>
    /// </summary>
    public void ClearNothingToBill()
    {
        if (!NothingToBillAtUtc.HasValue)
        {
            return;
        }

        NothingToBillAtUtc = null;
        NothingToBillReason = null;
        NothingToBillByUserId = null;
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
    /// Shape of the lifecycle: a booked visit may be started, closed, cancelled or marked absent; a started
    /// visit can no longer be un-started; and the two terminal-looking states are **not** dead ends —
    /// <c>Cancelled → Scheduled</c> is the existing reactivation path, and <c>NoShow → Scheduled</c> is
    /// rebooking a patient who missed their slot.
    /// </para>
    /// <para>
    /// <b><c>Completed → Cancelled</c> is new (AC-P1.5)</b> and is the point of the table: a visit is
    /// auto-completed merely by saving its fiche de soins, so "completed" is reached by accident often enough
    /// that having no way back made a mis-saved fiche permanent. It is the ONLY exit from
    /// <c>Completed</c> — a closed visit cannot be re-opened, only voided.
    /// </para>
    /// <para>
    /// <b><c>Confirmed</c> is no longer reachable from anywhere</b>: it distinguished « the patient said yes »
    /// from « we put them in the book » and nothing in the product ever acted on the difference — no read
    /// branched on it, no reminder keyed off it, and it was set only by a human picking it out of a list. Its
    /// row stays so the rows already stored in that state can still be moved on; deleting the member instead
    /// would make them unloadable.
    /// </para>
    /// </summary>
    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> AllowedTransitions = new()
    {
        [AppointmentStatus.Scheduled] = new[]
        {
            AppointmentStatus.InProgress, AppointmentStatus.Completed,
            AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
            AppointmentStatus.AwaitingClosure,
        },
        // Legacy-only, per the note above: no transition leads here any more, so this row exists for the rows
        // already stored as Confirmed. Still no Confirmed → Scheduled — "withdrawing a confirmation" has no
        // clinical meaning (the slot is booked either way) and Reschedule *preserves* Confirmed (A-2).
        [AppointmentStatus.Confirmed] = new[]
        {
            AppointmentStatus.InProgress, AppointmentStatus.Completed,
            AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
            AppointmentStatus.AwaitingClosure,
        },
        // A visit that has started cannot be un-started; it ends one of three ways, or its slot simply runs out.
        [AppointmentStatus.InProgress] = new[]
        {
            AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow,
            AppointmentStatus.AwaitingClosure,
        },
        // The presence question, still open. Its three exits are the three answers to it — there is no way back
        // to InProgress, because a slot that has ended cannot start again.
        [AppointmentStatus.AwaitingClosure] = new[]
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
        AppointmentStatus.AwaitingClosure => "Séance passée",
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

    // `Confirm()` is deleted with the transition that reached it — see the table's note on `Confirmed`. Nothing
    // can produce that status any more; a stored one still loads, still renders and still moves on.
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
    /// Record that the slot has ended with the presence still unanswered — « Séance passée ».
    /// <para>
    /// The counterpart to <see cref="Start"/> and, like it, written only by <c>AppointmentProgressJob</c>. It
    /// deliberately does <b>not</b> close the visit: leaving a slot is not evidence the patient came, so the
    /// three real answers stay human (or arrive via <see cref="MarkVisitCompleted"/> when a fiche is filed).
    /// </para>
    /// </summary>
    public void MarkAwaitingClosure()
    {
        EnsureCanTransitionTo(AppointmentStatus.AwaitingClosure);
        if (Status == AppointmentStatus.AwaitingClosure)
        {
            return;
        }

        Status = AppointmentStatus.AwaitingClosure;
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
        // `AwaitingClosure` joins NoShow for the same reason: both are statements about a slot that has passed,
        // and carrying either onto a new date would badge a visit that has not happened yet as one that has.
        if (Status is AppointmentStatus.NoShow or AppointmentStatus.AwaitingClosure)
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

    /// <summary>
    /// Book a <b>single</b> act (or clear it with a null id) — the one-act path, unchanged for its callers.
    /// <para>
    /// Implemented on top of <see cref="SetProcedures"/> so the two can never disagree: leaving this writing only
    /// the scalars would let a one-act edit keep a stale three-act collection, and the collection is what the
    /// agenda and the devis read. The plan link is deliberately <b>preserved</b> — clearing the act does not mean
    /// the patient is no longer coming in for that step, and <see cref="SetTreatmentPlanItem"/> owns that decision.
    /// </para>
    /// </summary>
    public void SetProcedureType(
        Guid? procedureTypeId,
        int? procedureDurationMinutes,
        string? procedureColorHex,
        string? procedureName = null)
    {
        var keptLink = TreatmentPlanItemId;

        SetProcedures(procedureTypeId.HasValue
            ? new[]
            {
                new AppointmentProcedureInput(
                    procedureTypeId, procedureName, procedureDurationMinutes, procedureColorHex, keptLink),
            }
            : Array.Empty<AppointmentProcedureInput>());

        // SetProcedures derives the scalar from the rows; with no rows it clears it. Neither is what "the act was
        // cleared / replaced" should do to the devis link, so restore what the caller had.
        TreatmentPlanItemId = keptLink;
        // Clearing every act must not also erase the snapshot duration the caller passed for a one-act booking's
        // sake — but with no act there is nothing to snapshot, so the null-id branch legitimately clears all three.
        if (!procedureTypeId.HasValue)
        {
            ProcedureDurationMinutes = procedureDurationMinutes;
            ProcedureColorHex = procedureColorHex;
        }
    }

    /// <summary>
    /// Replace the whole list of acts booked into this séance, and **re-derive** the lead-act snapshot
    /// (<see cref="ProcedureTypeId"/> / <see cref="ProcedureDurationMinutes"/> / <see cref="ProcedureColorHex"/>)
    /// plus <see cref="TreatmentPlanItemId"/> from it.
    /// <para>
    /// Replace rather than add/remove: the picker in the booking dialog hands over the list the user is looking at,
    /// and a diffing API would need the caller to know each row's id — which for a brand-new booking it does not.
    /// The ids are regenerated on every save, which is why <see cref="AppointmentProcedure"/> holds no state worth
    /// preserving across one (no état, no money — the fiche de soins is where a performed act is recorded).
    /// </para>
    /// </summary>
    public void SetProcedures(IEnumerable<AppointmentProcedureInput> procedures)
    {
        ArgumentNullException.ThrowIfNull(procedures);

        // Materialise and validate before mutating: a half-applied list would leave the séance with some of the
        // new acts and a lead-act snapshot describing one of the old ones.
        var rows = new List<AppointmentProcedure>();
        var seenProcedureIds = new HashSet<Guid>();
        var seenPlanItemIds = new HashSet<Guid>();
        var index = 0;
        foreach (var input in procedures)
        {
            // Same act twice in one séance is a mis-click, not a quantity: the fiche de soins is what records
            // « deux obturations », per tooth, with its own prices.
            if (input.ProcedureTypeId.HasValue && !seenProcedureIds.Add(input.ProcedureTypeId.Value))
            {
                throw new InvalidOperationException(
                    $"L'acte « {input.ProcedureName ?? input.ProcedureTypeId.ToString()} » est déjà présent dans ce rendez-vous.");
            }
            if (input.TreatmentPlanItemId.HasValue && !seenPlanItemIds.Add(input.TreatmentPlanItemId.Value))
            {
                throw new InvalidOperationException(
                    "Le même acte du devis ne peut pas être planifié deux fois dans le même rendez-vous.");
            }

            rows.Add(new AppointmentProcedure(
                Guid.NewGuid(),
                Id,
                input.ProcedureTypeId,
                input.ProcedureName,
                input.DurationMinutes,
                input.ColorHex,
                input.TreatmentPlanItemId,
                index++));
        }

        _procedures.Clear();
        _procedures.AddRange(rows);

        var lead = rows.FirstOrDefault();
        ProcedureTypeId = lead?.ProcedureTypeId;
        ProcedureDurationMinutes = lead?.DurationMinutes;
        ProcedureColorHex = lead?.ColorHex;
        TreatmentPlanItemId = rows.FirstOrDefault(r => r.TreatmentPlanItemId.HasValue)?.TreatmentPlanItemId;

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Re-snapshot the name/colour of one catalog act everywhere it appears on this visit — the lead-act scalars
    /// **and** the matching child rows. <c>UpdateProcedureTypeCommand</c> used to call
    /// <see cref="SetProcedureType"/> for this, which now means "replace the whole séance with one act": renaming
    /// a procedure would have deleted the other acts of every appointment using it.
    /// </summary>
    public void RefreshProcedureSnapshot(Guid procedureTypeId, string? procedureName, string? colorHex)
    {
        var touched = false;

        foreach (var row in _procedures.Where(p => p.ProcedureTypeId == procedureTypeId))
        {
            row.RefreshSnapshot(procedureName, colorHex);
            touched = true;
        }

        if (ProcedureTypeId == procedureTypeId && !string.IsNullOrWhiteSpace(colorHex))
        {
            ProcedureColorHex = colorHex.Trim();
            touched = true;
        }

        if (touched)
        {
            UpdatedAt = DateTime.UtcNow;
        }
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

