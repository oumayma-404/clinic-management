using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientFile : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public string FileName { get; private set; }
    public string FilePath { get; private set; }
    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public FileType FileType { get; private set; }
    public string? Description { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public string? UploadedBy { get; private set; }

    // Navigation property
    public Patient Patient { get; private set; } = null!;

    private PatientFile() { } // For EF Core

    public PatientFile(
        Guid id,
        Guid patientId,
        string fileName,
        string filePath,
        string contentType,
        long fileSize,
        FileType fileType,
        string? description = null,
        string? uploadedBy = null)
    {
        Id = id;
        PatientId = patientId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        FileSize = fileSize;
        FileType = fileType;
        Description = description;
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
    }
}



