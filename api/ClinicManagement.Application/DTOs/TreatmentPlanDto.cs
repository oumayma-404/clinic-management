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
