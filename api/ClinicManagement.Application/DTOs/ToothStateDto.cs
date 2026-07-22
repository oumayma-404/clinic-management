namespace ClinicManagement.Application.DTOs;

/// <summary>One recorded treatment/condition on a tooth (a patient's odontogram is a list of these).</summary>
public class ToothStateDto
{
    public Guid Id { get; set; }
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
    public DateTime TreatmentDate { get; set; }
    public Guid? DentalRecordId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One tooth's condition captured while adding/editing a dental record (feeds the odontogram).</summary>
public class ToothConditionInput
{
    public int ToothNumber { get; set; }
    public string Condition { get; set; } = string.Empty;
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}
