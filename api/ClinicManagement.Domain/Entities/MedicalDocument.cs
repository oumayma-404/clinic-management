using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class MedicalDocument : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public string DocumentType { get; private set; } // prescription, liaison, honoraires, certificat
    public DateTime DocumentDate { get; private set; }
    
    // Patient info (snapshot at time of creation)
    public string PatientName { get; private set; }
    public string? PatientAge { get; private set; }
    
    // Recipient (for liaison letters)
    public string? RecipientDoctorName { get; private set; }
    public string? RecipientDoctorSpecialty { get; private set; }
    
    // Document content (stored as JSON for flexibility)
    public string ContentJson { get; private set; }
    
    // Clinic/Doctor info (snapshot at time of creation)
    public string ClinicName { get; private set; }
    public string ClinicAddress { get; private set; }
    public string ClinicPhone { get; private set; }
    public string DoctorName { get; private set; }
    public string DoctorSpecialty { get; private set; }
    
    public bool IsDraft { get; private set; }
    public Guid? FileId { get; private set; } // Reference to PatientFile if saved as file
    
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    
    // Navigation properties
    public Patient Patient { get; private set; } = null!;
    
    private MedicalDocument() { } // For EF Core
    
    public MedicalDocument(
        Guid id,
        Guid patientId,
        string documentType,
        DateTime documentDate,
        string patientName,
        string? patientAge,
        string contentJson,
        string clinicName,
        string clinicAddress,
        string clinicPhone,
        string doctorName,
        string doctorSpecialty,
        bool isDraft = false,
        string? recipientDoctorName = null,
        string? recipientDoctorSpecialty = null,
        Guid? fileId = null)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            throw new ArgumentException("Document type cannot be null or empty", nameof(documentType));
        
        if (string.IsNullOrWhiteSpace(patientName))
            throw new ArgumentException("Patient name cannot be null or empty", nameof(patientName));
        
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new ArgumentException("Content JSON cannot be null or empty", nameof(contentJson));
        
        Id = id;
        PatientId = patientId;
        DocumentType = documentType;
        DocumentDate = documentDate;
        PatientName = patientName;
        PatientAge = patientAge;
        ContentJson = contentJson;
        ClinicName = clinicName;
        ClinicAddress = clinicAddress;
        ClinicPhone = clinicPhone;
        DoctorName = doctorName;
        DoctorSpecialty = doctorSpecialty;
        RecipientDoctorName = recipientDoctorName;
        RecipientDoctorSpecialty = recipientDoctorSpecialty;
        IsDraft = isDraft;
        FileId = fileId;
        CreatedAt = DateTime.UtcNow;
    }
    
    public void Update(
        DateTime documentDate,
        string contentJson,
        string? recipientDoctorName = null,
        string? recipientDoctorSpecialty = null,
        bool? isDraft = null,
        Guid? fileId = null)
    {
        if (string.IsNullOrWhiteSpace(contentJson))
            throw new ArgumentException("Content JSON cannot be null or empty", nameof(contentJson));
        
        DocumentDate = documentDate;
        ContentJson = contentJson;
        RecipientDoctorName = recipientDoctorName;
        RecipientDoctorSpecialty = recipientDoctorSpecialty;
        if (isDraft.HasValue)
            IsDraft = isDraft.Value;
        if (fileId.HasValue)
            FileId = fileId;
        UpdatedAt = DateTime.UtcNow;
    }
}

