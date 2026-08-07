using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientFile : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public Guid? FolderId { get; private set; } // Null means root folder
    public string FileName { get; private set; }
    public string StorageKey { get; private set; } // MinIO storage key
    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public FileType FileType { get; private set; }
    public string? Description { get; private set; }
    public DateTime UploadedAt { get; private set; }
    public string? UploadedBy { get; private set; }

    // Navigation properties
    public Patient Patient { get; private set; } = null!;
    public PatientFolder? Folder { get; private set; }

    private PatientFile() { } // For EF Core

    public PatientFile(
        Guid id,
        Guid patientId,
        string fileName,
        string storageKey,
        string contentType,
        long fileSize,
        FileType fileType,
        Guid? folderId = null,
        string? description = null,
        string? uploadedBy = null)
    {
        Id = id;
        PatientId = patientId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        StorageKey = storageKey ?? throw new ArgumentNullException(nameof(storageKey));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        FileSize = fileSize;
        FileType = fileType;
        FolderId = folderId;
        Description = description;
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// AC-4.1 — renames the file by recomposing the name from the <b>stored</b> extension, so changing the
    /// format through a rename is unrepresentable rather than merely refused. The stored extension is what the
    /// validated <see cref="ContentType"/> was decided from, and nothing re-reads the blob on a rename.
    /// </summary>
    public void Rename(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            throw new ArgumentException("A file name cannot be empty", nameof(baseName));

        var trimmed = baseName.Trim();
        var dot = FileName.LastIndexOf('.');
        var extension = dot > 0 ? FileName[dot..] : string.Empty;

        // A base name that already carries the extension is kept as typed — the editor shows the suffix beside
        // the field, so anyone who types it too gets « scan.pdf », never « scan.pdf.pdf ».
        FileName = trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : trimmed + extension;
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
    }

    public void MoveToFolder(Guid? folderId)
    {
        FolderId = folderId;
    }
}



