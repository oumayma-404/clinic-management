namespace ClinicManagement.API.Models;

public class CreateMedicalDocumentRequest
{
    public Guid PatientId { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public DateTime DocumentDate { get; set; }
    public string? RecipientDoctorName { get; set; }
    public string? RecipientDoctorSpecialty { get; set; }
    public string ContentJson { get; set; } = string.Empty;
    public string ClinicName { get; set; } = string.Empty;
    public string ClinicAddress { get; set; } = string.Empty;
    public string ClinicPhone { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string DoctorSpecialty { get; set; } = string.Empty;
    public IFormFile? PdfFile { get; set; }
    public Guid? AppointmentId { get; set; }

    /// <summary>
    /// The practitioner the document is issued in the name of — the id behind <see cref="DoctorName"/>. Resolves
    /// the cachet + n° d'ordre CNOMDT server-side; see
    /// <c>CreateMedicalDocumentCommand.IssuingDoctorId</c>.
    /// </summary>
    public Guid? IssuingDoctorId { get; set; }
}




