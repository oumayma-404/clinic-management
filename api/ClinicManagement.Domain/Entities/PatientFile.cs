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

    /// <summary>How the file reached the record. <see cref="FileOrigin.Upload"/> for every row older than camera import.</summary>
    public FileOrigin Origin { get; private set; }

    /// <summary>
    /// When a camera photo was taken (UTC), as its EXIF said once the import corrected the camera clock. Null when the
    /// camera wrote no usable time — and for every file that is not a camera photo.
    /// </summary>
    public DateTime? CapturedAt { get; private set; }

    /// <summary>
    /// Identifies the <b>shot</b>, so re-inserting a card files nothing twice. Unique per cabinet; see
    /// <see cref="PhotoCaptureFacts"/> for why it is not a hash of these bytes.
    /// </summary>
    public string? CaptureKey { get; private set; }

    /// <summary>« Canon EOS 250D » — a display snapshot of the camera, never a link to the registry.</summary>
    public string? CameraModel { get; private set; }

    /// <summary>
    /// SHA-256 of the full-size original a clinic PC kept, when one did. ⚠️ The server never holds those bytes: on a
    /// hosted cabinet this row's own bytes are a reduced copy, and the four <c>Original*</c> fields only record that a
    /// fuller one exists, where, and how to recognise it.
    /// </summary>
    public string? OriginalSha256 { get; private set; }

    public long? OriginalSizeBytes { get; private set; }

    /// <summary>The name of the PC holding the original — what « Original : PC ACCUEIL » prints.</summary>
    public string? OriginalHeldBy { get; private set; }

    public DateTime? OriginalKeptAt { get; private set; }

    public const int MaxOriginalHolderLength = 100;

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

    /// <summary>
    /// Marks this file as a camera photo and records what the import knew about the shot. Called once, right after
    /// construction, by the two doors that store a file.
    /// </summary>
    public void RecordCameraCapture(PhotoCaptureFacts capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        if (Residency != FileResidency.Hosted)
        {
            throw new InvalidOperationException("Une photo de l'appareil est toujours conservée par le serveur.");
        }

        Origin = FileOrigin.Camera;
        CapturedAt = capture.CapturedAtUtc;
        CaptureKey = capture.CaptureKey;
        CameraModel = capture.CameraModel;
    }

    /// <summary>
    /// Records that a clinic PC kept this photo's full-size original. Returns false when it already said exactly
    /// this — the Windows app re-reports after a restart, and an identical report must not age the row's version.
    /// </summary>
    public bool RecordOriginal(string sha256, long sizeBytes, string heldBy, DateTime keptAtUtc)
    {
        if (Origin != FileOrigin.Camera)
        {
            throw new InvalidOperationException("Seule une photo de l'appareil peut avoir un original au cabinet.");
        }

        if (!PhotoCaptureFacts.IsCaptureKey(sha256))
        {
            throw new ArgumentException("L'empreinte de l'original doit être un SHA-256.", nameof(sha256));
        }

        if (sizeBytes <= 0)
        {
            throw new ArgumentException("L'original doit avoir une taille.", nameof(sizeBytes));
        }

        var holder = heldBy?.Trim();
        if (string.IsNullOrEmpty(holder) || holder.Length > MaxOriginalHolderLength)
        {
            throw new ArgumentException("Le nom du poste qui garde l'original est requis.", nameof(heldBy));
        }

        var hash = sha256.Trim().ToLowerInvariant();
        if (OriginalSha256 == hash && OriginalSizeBytes == sizeBytes && OriginalHeldBy == holder)
        {
            return false;
        }

        OriginalSha256 = hash;
        OriginalSizeBytes = sizeBytes;
        OriginalHeldBy = holder;
        OriginalKeptAt = keptAtUtc;
        return true;
    }

    /// <summary>
    /// Re-files this document under another patient of the same cabinet — how a photo filed to the wrong person is
    /// corrected without deleting it (delete is doctor/admin only, and loses the capture facts).
    ///
    /// <para>⚠️ The folder is the target patient's, chosen by the caller: a folder belongs to one patient, so keeping
    /// the old <c>FolderId</c> would leave the file pointing into somebody else's tree. Annotations are keyed on the
    /// file, not the patient, so they follow without being touched.</para>
    /// </summary>
    public void MoveToPatient(Guid patientId, Guid? folderId)
    {
        if (patientId == Guid.Empty)
        {
            throw new ArgumentException("Le patient de destination est requis.", nameof(patientId));
        }

        if (patientId == PatientId)
        {
            throw new InvalidOperationException("Ce fichier appartient déjà à ce patient.");
        }

        PatientId = patientId;
        FolderId = folderId;
    }
}



