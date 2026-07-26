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
    public DateTime? UpdatedAt { get; set; }

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
    public Guid? DentalActCodeId { get; set; }
    public string? CodeActe { get; set; }
    public string DesignationFr { get; set; } = string.Empty;
    public List<int> ToothNumbers { get; set; } = new();
    public decimal PlannedCost { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? DoneDate { get; set; }
    public Guid? LinkedDentalRecordId { get; set; }

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
}

public class InstallmentDto
{
    public Guid Id { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Outstanding { get; set; }
    public bool IsPaid { get; set; }
    public string? LastMethod { get; set; }
    public DateTime? LastPaidOn { get; set; }
}

/// <summary>One requested act line when creating/updating a treatment plan (catalog act or free-text).</summary>
public class TreatmentPlanItemRequest
{
    public Guid? DentalActCodeId { get; set; }
    public string? CodeActe { get; set; }
    public string DesignationFr { get; set; } = string.Empty;
    public decimal PlannedCost { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
}

/// <summary>One requested installment (échéance) when setting a plan's payment schedule.</summary>
public class InstallmentRequest
{
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
}
