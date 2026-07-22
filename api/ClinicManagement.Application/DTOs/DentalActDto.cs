namespace ClinicManagement.Application.DTOs;

/// <summary>A dental act catalog entry (chapitre DCH). Global reference data.</summary>
public class DentalActDto
{
    public Guid Id { get; set; }
    public string CodeActe { get; set; } = string.Empty;
    public string DesignationFr { get; set; } = string.Empty;
    public string LettreCle { get; set; } = "D";
    public decimal? Coefficient { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal? DefaultFee { get; set; }
    public bool RequiresAccordPrealable { get; set; }
    public bool IsActive { get; set; }
    public bool IsProvisional { get; set; }
}
