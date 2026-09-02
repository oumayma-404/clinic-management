using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

public class PatientFile : Entity<Guid>, IAuditable
{
    public Guid PatientId { get; private set; }

    /// <summary>The owning clinic, denormalised from <see cref="Patient"/>. See <see cref="PatientMedicalHistory.ClinicId"/>.</summary>
    public Guid ClinicId { get; private set; }

    public Guid? FolderId { get; private set; } // Null means root folder
    public string FileName { get; private set; }

    /// <summary>
    /// Where the bytes are in the object store — <b>null exactly when <see cref="Residency"/> is
    /// <see cref="FileResidency.Vault"/></b>, because a vault file's bytes never reached the deployment and its
    /// path in the cabinet's coffre is derived by <see cref="Services.VaultPath"/> rather than stored.
    ///
    /// <para>⚠️ The nullability is the point, not a concession. A caller that hands this straight to
    /// <c>IFileStorage</c> without branching on the residency throws immediately instead of quietly deleting or
    /// downloading nothing against a key the store never held.</para>
    /// </summary>
    public string? StorageKey { get; private set; }

    public string ContentType { get; private set; }
    public long FileSize { get; private set; }
    public FileType FileType { get; private set; }

    /// <summary>Whether the deployment holds these bytes, or only the record of them.</summary>
    public FileResidency Residency { get; private set; }

    /// <summary>
    /// Lower-case hex SHA-256 of the original, computed by whoever wrote it into the coffre. Null for a hosted
    /// file, where the object store is the authority on its own content.
    /// </summary>
    public string? ContentHash { get; private set; }

    /// <summary>
    /// A small derived image, painted by the file list in place of the original and standing in for a vault
    /// original wherever the coffre is out of reach. Null is ordinary — nothing renders a preview of an STL, a
    /// preview that came out too big is dropped rather than allowed to become the storage problem the vault
    /// residency exists to avoid, and every file stored before previews existed has none.
    /// </summary>
    public string? PreviewStorageKey { get; private set; }

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
        Guid clinicId,
        string fileName,
        string storageKey,
        string contentType,
        long fileSize,
        FileType fileType,
        Guid? folderId = null,
        string? description = null,
        string? uploadedBy = null,
        string? previewStorageKey = null)
    {
        Id = id;
        PatientId = patientId;
        ClinicId = clinicId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        StorageKey = storageKey ?? throw new ArgumentNullException(nameof(storageKey));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        FileSize = fileSize;
        FileType = fileType;
        Residency = FileResidency.Hosted;
        PreviewStorageKey = previewStorageKey;
        FolderId = folderId;
        Description = description;
        UploadedBy = uploadedBy;
        UploadedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Records a file whose bytes stay in the cabinet's coffre. A named factory rather than an overload with a
    /// null storage key, because the two are different acts: one stores bytes and then describes them, this one
    /// only ever describes bytes somebody else holds.
    /// </summary>
    public static PatientFile RegisterInVault(
        Guid id,
        Guid patientId,
        Guid clinicId,
        string fileName,
        string contentType,
        long fileSize,
        FileType fileType,
        string contentHash,
        string? previewStorageKey = null,
        Guid? folderId = null,
        string? description = null,
        string? uploadedBy = null)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException("Un fichier du coffre doit porter son empreinte.", nameof(contentHash));
        }

        if (fileSize <= 0)
        {
            throw new ArgumentException("Un fichier du coffre doit avoir une taille.", nameof(fileSize));
        }

        return new PatientFile
        {
            Id = id,
            PatientId = patientId,
            ClinicId = clinicId,
            FileName = fileName ?? throw new ArgumentNullException(nameof(fileName)),
            ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType)),
            FileSize = fileSize,
            FileType = fileType,
            Residency = FileResidency.Vault,
            ContentHash = contentHash.Trim().ToLowerInvariant(),
            PreviewStorageKey = previewStorageKey,
            StorageKey = null,
            FolderId = folderId,
            Description = description,
            UploadedBy = uploadedBy,
            UploadedAt = DateTime.UtcNow
        };
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



