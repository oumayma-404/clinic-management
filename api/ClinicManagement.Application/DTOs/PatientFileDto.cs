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









