namespace ClinicManagement.Application.DTOs;

public class TreatmentPlanDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? Number { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public DateTime? AcceptedDate { get; set; }
    public string? CancellationReason { get; set; }
    public decimal TotalPlanned { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (PostgreSQL <c>xmin</c>). Send it back on the matching update command so
    /// the save is checked against the copy the user actually edited; a peer's change in between then yields
    /// a 409 instead of a silent overwrite.
    /// </summary>
    public uint Version { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Post-acceptance amendments so far (0 = never amended). Printed as « · révision N » on the devis and
    /// the workspace header only when &gt; 0, so a patient holding an earlier printout can tell which version
    /// they signed. Persisted.
    /// </summary>
    public int RevisionNumber { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    // Clinical progress, always populated.
    public int ItemsDone { get; set; }
    public int ItemsTotal { get; set; }

    /// <summary>
    /// Earliest still-upcoming appointment across the plan's acts (« prochaine séance »), or null. Derived,
    /// so a cancelled appointment stops counting immediately. Populated on the query paths only.
    /// </summary>
    public DateTime? NextAppointmentAt { get; set; }

    /// <summary>
    /// The non-cancelled invoice this devis was billed into, when one exists — the plan is then represented by
    /// that invoice in « Solde patient ». Populated on the query paths only.
    /// </summary>
    public Guid? LinkedInvoiceId { get; set; }
    public string? LinkedInvoiceNumber { get; set; }
    public string? LinkedInvoiceStatus { get; set; }

    public List<TreatmentPlanItemDto> Items { get; set; } = new();
    public List<InstallmentDto> Installments { get; set; } = new();
}

public class TreatmentPlanItemDto
{
    public Guid Id { get; set; }

    /// <summary>
    /// The clinic's own procedure this act is performed as, when the line was chosen from that menu. Lets
    /// booking the act preselect the procedure (colour + default duration on the appointment, and the act
    /// proposal in the dental-record modal). Null on CNAM-only, hand-typed, and pre-migration lines.
    /// </summary>
    public Guid? ProcedureTypeId { get; set; }

    public string DesignationFr { get; set; } = string.Empty;
    public List<int> ToothNumbers { get; set; } = new();
    public decimal PlannedCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? DoneDate { get; set; }
    public Guid? LinkedDentalRecordId { get; set; }

    /// <summary>Clinical order within the plan (0-based). Persisted; acts are returned already sorted.</summary>
    public int SequenceNumber { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    /// <summary>
    /// The appointment that currently speaks for this act — the earliest upcoming live one, else the most
    /// recent past live one. Null when nothing is booked, including when the only linked appointment was
    /// cancelled or a no-show (so the act returns to « À planifier » and can be booked again).
    /// Populated on the query paths only.
    /// </summary>
    public Guid? ScheduledAppointmentId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ScheduledAppointmentStatus { get; set; }

    /// <summary>
    /// The act's clinical steps in order — « Préparation, Empreinte, Scellement ». <b>Empty for an act done in
    /// one séance</b>, which is every line written before steps existed and most written after, so a client that
    /// ignores this field behaves exactly as it did.
    /// </summary>
    public List<TreatmentPlanItemStepDto> Steps { get; set; } = new();

    /// <summary>How many of <see cref="Steps"/> are carried out. Derived from the rows, always populated.</summary>
    public int StepsDone { get; set; }

    /// <summary>
    /// The next step still to carry out, or null when there is none (or the act has no steps at all) — what the
    /// row's single primary action names: « Planifier le scellement ».
    /// </summary>
    public Guid? NextStepId { get; set; }
}

/// <summary>One clinical step of a planned act. Carries no money — the fee lives once on the act.</summary>
public class TreatmentPlanItemStepDto
{
    public Guid Id { get; set; }
    public string Label { get; set; } = string.Empty;

    /// <summary>Clinical order within the act (0-based, dense). Steps are returned already sorted.</summary>
    public int SequenceNumber { get; set; }

    public DateTime? DoneDate { get; set; }

    /// <summary>The fiche de soins that evidences this step. <b>Per step</b> — which is what lets one devis act
    /// be recorded across several fiches.</summary>
    public Guid? LinkedDentalRecordId { get; set; }

    public int? EstimatedDurationMinutes { get; set; }

    // ---- Derived (never persisted) -------------------------------------------------------------------
    /// <summary>
    /// The appointment that currently speaks for <b>this step</b>, by the same rule the act uses. Null when the
    /// step is not booked — including when its only linked visit was cancelled, so it becomes bookable again.
    /// <para>
    /// Separate from the act's own <c>ScheduledAppointmentId</c> on purpose: an act with one of three séances
    /// booked is « planifié » as an act and has two unbooked steps, and one field cannot say both.
    /// </para>
    /// </summary>
    public Guid? ScheduledAppointmentId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public string? ScheduledAppointmentStatus { get; set; }
}

public class InstallmentDto
{
    public Guid Id { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }

    /// <summary>Derived from the payment ledger. No longer monotonic — a payment can be voided.</summary>
    public decimal AmountPaid { get; set; }

    public decimal Outstanding { get; set; }
    public bool IsPaid { get; set; }

    /// <summary>Derived: the most recent LIVE payment's method/date.</summary>
    public string? LastMethod { get; set; }
    public DateTime? LastPaidOn { get; set; }

    /// <summary>Every payment received against this échéance, each on its own date. Oldest first.</summary>
    public List<InstallmentPaymentDto> Payments { get; set; } = new();
}

/// <summary>One payment received against an échéance. Voidable; a voided row is kept and marked.</summary>
public class InstallmentPaymentDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public DateTime PaidOn { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsVoided { get; set; }
    public DateTime? VoidedAt { get; set; }
    public string? VoidReason { get; set; }
    public string? VoidedByName { get; set; }
}

/// <summary>One requested act line when creating/updating a treatment plan (catalog act or free-text).</summary>
public class TreatmentPlanItemRequest
{
    /// <summary>
    /// The existing act this line stands for, echoed back by the client. When it matches a line already on
    /// the plan, that line keeps its id — so an appointment or dental-record link to the act survives the
    /// edit. Unknown ids are treated as a new line, never an error (a stale client must not fail the save).
    /// </summary>
    public Guid? Id { get; set; }


    /// <summary>
    /// The clinic's own procedure this act will be performed as, when the caller picked one. Persisted so
    /// booking the act later can preselect it. The only catalog a devis line comes from — a procedure is a
    /// service you schedule and sell, a DCH code is the regulatory code for one clinical situation, and several
    /// codes can bill as the same procedure. An unknown or cross-clinic id is stored as sent and simply fails
    /// to resolve at booking time; it is never trusted for pricing.
    /// </summary>
    public Guid? ProcedureTypeId { get; set; }

    public string DesignationFr { get; set; } = string.Empty;
    public decimal PlannedCost { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
}

/// <summary>One requested installment (échéance) when setting a plan's payment schedule.</summary>
public class InstallmentRequest
{
    /// <summary>
    /// The existing échéance this line revises, echoed back by the client. A row carrying collected money
    /// MUST be echoed back — dropping it would erase that cash from the plan's balance, and the domain
    /// refuses it.
    /// </summary>
    public Guid? Id { get; set; }

    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
}
