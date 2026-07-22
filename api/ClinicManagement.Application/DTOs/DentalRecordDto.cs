namespace ClinicManagement.Application.DTOs;

public class DentalRecordDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    /// <summary>Derived summary of the acts' procedure names (read-only).</summary>
    public string ProcedureType { get; set; } = string.Empty;
    /// <summary>Derived total = sum of act costs (read-only).</summary>
    public decimal Cost { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; } // derived: Cost − AmountPaid
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    public bool IsAdultTeeth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public List<DentalRecordActDto> Acts { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>One act on a dental record (procedure + teeth + cost + resulting odontogram state).</summary>
public class DentalRecordActDto
{
    public Guid Id { get; set; }
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}

/// <summary>One requested act when creating/updating a dental record.</summary>
public class DentalActInput
{
    public Guid? ProcedureTypeId { get; set; }
    public string ProcedureName { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    /// <summary>Resulting odontogram state (ToothCondition name); null/empty/"Sain" = no odontogram entry.</summary>
    public string? ResultingCondition { get; set; }
    public string? Surfaces { get; set; }
    public string? Note { get; set; }
}
