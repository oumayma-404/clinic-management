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

    // Snapshotted practitioner/clinic fields (Part C, FR-3.3 / FR-6.1). Populated by both producers — the
    // create command snapshots them into ContentJson so the unauthenticated background PDF job can render
    // the cachet + city without a live doctor/clinic lookup.
    public string? ClinicCity { get; set; }             // "{City}, le …" place line (never a hardcoded "Paris")
    public string? DoctorOrdreNumber { get; set; }      // CNOMDT registration number (snapshot)
    public string? DoctorCachetKey { get; set; }        // IFileStorage key of the practitioner cachet image
    public string? DoctorCachetContentType { get; set; } // persisted MIME type of that image

    // Recipient (for liaison documents)
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    
    // Document Content (varies by type)
    public Dictionary<string, string> Content { get; set; } = new();
}






