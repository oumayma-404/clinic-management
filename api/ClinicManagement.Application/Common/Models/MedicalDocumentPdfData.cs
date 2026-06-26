namespace ClinicManagement.Application.Common.Models;

public class MedicalDocumentPdfData
{
    public string DocumentType { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    
    // Patient Info
    public string PatientName { get; set; } = string.Empty;
    public string? PatientAge { get; set; }
    public string? PatientId { get; set; }
    
    // Clinic Info
    public string ClinicName { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string ClinicPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = string.Empty;
    
    // Recipient (for liaison documents)
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    
    // Document Content (varies by type)
    public Dictionary<string, string> Content { get; set; } = new();
}






