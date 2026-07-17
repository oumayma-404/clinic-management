namespace ClinicManagement.Application.DTOs;

public class DentalRecordDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTime InterventionDate { get; set; }
    public string ProcedureType { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Balance { get; set; } // derived: Cost − AmountPaid (read-only, set in handler mappings)
    public List<string> Notes { get; set; } = new();
    public List<string> ImportantNotes { get; set; } = new();
    public bool IsAdultTeeth { get; set; }
    public List<int> ToothNumbers { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}




