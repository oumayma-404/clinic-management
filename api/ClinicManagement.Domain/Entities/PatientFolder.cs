using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

public class PatientFolder : Entity<Guid>
{
    public Guid PatientId { get; private set; }
    public string Name { get; private set; }
    public Guid? ParentFolderId { get; private set; } // For nested folders support
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Navigation properties
    public Patient Patient { get; private set; } = null!;
    public PatientFolder? ParentFolder { get; private set; }
    private readonly List<PatientFolder> _subFolders = new();
    public IReadOnlyCollection<PatientFolder> SubFolders => _subFolders.AsReadOnly();
    private readonly List<PatientFile> _files = new();
    public IReadOnlyCollection<PatientFile> Files => _files.AsReadOnly();

    private PatientFolder() { } // For EF Core

    public PatientFolder(
        Guid id,
        Guid patientId,
        string name,
        Guid? parentFolderId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name cannot be null or empty", nameof(name));

        Id = id;
        PatientId = patientId;
        Name = name.Trim();
        ParentFolderId = parentFolderId;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name cannot be null or empty", nameof(name));

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddSubFolder(PatientFolder subFolder)
    {
        if (subFolder == null)
            throw new ArgumentNullException(nameof(subFolder));

        if (subFolder.PatientId != PatientId)
            throw new InvalidOperationException("Subfolder must belong to the same patient");

        if (!_subFolders.Any(f => f.Id == subFolder.Id))
        {
            _subFolders.Add(subFolder);
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void AddFile(PatientFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        if (file.PatientId != PatientId)
            throw new InvalidOperationException("File must belong to the same patient");

        if (!_files.Any(f => f.Id == file.Id))
        {
            _files.Add(file);
            UpdatedAt = DateTime.UtcNow;
        }
    }
}









