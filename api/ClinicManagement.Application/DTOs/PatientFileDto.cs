namespace ClinicManagement.Application.DTOs;

public class PatientFileDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? FolderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }

    /// <summary>
    /// <c>Hosted</c> or <c>Vault</c> — whether the server can serve these bytes at all, or only the record of
    /// them. The list renders a « conservé au cabinet » state from this rather than inferring it from a null.
    /// </summary>
    public string Residency { get; set; } = string.Empty;

    /// <summary>Lower-case hex SHA-256, for a coffre file. Null for a hosted one, whose store vouches for itself.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Whether a stand-in image exists for a coffre original that is out of reach on this machine.</summary>
    public bool HasPreview { get; set; }

    /// <summary>Round-tripped by the rename/move form so a concurrent change is a 409, not a silent overwrite.</summary>
    public uint Version { get; set; }
}

public class PatientFolderDto
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
    public int SubFolderCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}









