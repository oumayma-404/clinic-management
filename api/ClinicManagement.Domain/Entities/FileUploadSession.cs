using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// An upload that is still arriving — the record of what a client has sent so far, so a connection that drops
/// mid-file can be resumed instead of started again.
///
/// <para>⚠️ <b>It is a table rather than server memory because the point is surviving a restart.</b> A 400 Mo CBCT
/// is minutes of a clinic's uplink; an in-process dictionary would lose every upload in flight on a deploy, which
/// is exactly when a practice is most likely to be sending one.</para>
///
/// <para>⚠️ <b>Parts are sequential, and that is a deliberate limit.</b> <see cref="ReceivedParts"/> is a count,
/// not a set: part <c>n+1</c> is the only one a client may send next, and resuming means asking what arrived and
/// continuing from there. Parallel chunks would use more of the uplink, but they need a set of received parts, a
/// child table to hold it and an ordering decision at assembly — and on the connections this exists for, one
/// stream already saturates the link. Refusing an out-of-order part with a clear sentence beats accepting it into
/// a file with a hole in it.</para>
///
/// <para>⚠️ <b>The row is deleted on completion, not marked done.</b> The <see cref="PatientFile"/> it becomes is
/// the record; a spent session kept alongside it would be a second, staler answer to « does this file exist? ».</para>
/// </summary>
public class FileUploadSession : AggregateRoot<Guid>
{
    /// <summary>
    /// How long an abandoned upload keeps its staging area. Long enough for a practice to come back after lunch
    /// and short enough that a browser closed on Friday is not still holding bytes on Monday.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    // Widths the caller must respect before a row is built; they mirror FileUploadSessionConfiguration.
    public const int MaxFileNameLength = 260;
    public const int MaxContentTypeLength = 200;
    public const int MaxDescriptionLength = 500;
    public const int MaxUploadedByLength = 200;
    public const int MaxStorageReferenceLength = 100;

    public Guid ClinicId { get; private set; }

    public Guid PatientId { get; private set; }

    public Guid? FolderId { get; private set; }

    /// <summary>Sanitized at creation — never the client's string, exactly as an ordinary upload's is.</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>
    /// The catalog's type for the resolved format, decided when the session opens.
    ///
    /// <para>⚠️ Stored rather than re-derived at completion: it is what the assembled blob is written and served
    /// as, and re-resolving it later would let a rename between the two requests change what the file claims to
    /// be.</para>
    /// </summary>
    public string ContentType { get; private set; } = string.Empty;

    /// <summary>
    /// What the client said the whole file weighs. ⚠️ <b>A claim, checked against the bytes that actually
    /// arrived before the file is stored</b> — it is what lets the size cap refuse an oversized upload before a
    /// single chunk crosses the wire, which is the whole point of asking for it.
    /// </summary>
    public long DeclaredLength { get; private set; }

    public string? Description { get; private set; }

    public string? UploadedBy { get; private set; }

    /// <summary>The staging area's opaque handle, from <c>IResumableUploadStore.BeginAsync</c>.</summary>
    public string StorageReference { get; private set; } = string.Empty;

    /// <summary>
    /// The size of every part but the last. Fixed by the server, not chosen by the client: it is what the resume
    /// arithmetic is done in, so a client that picked its own could resume at a boundary the parts do not have.
    /// </summary>
    public int ChunkSize { get; private set; }

    /// <summary>How many contiguous parts have arrived. The next part a client may send is this plus one.</summary>
    public int ReceivedParts { get; private set; }

    /// <summary>Bytes accepted so far, summed as they arrived rather than re-measured from the store.</summary>
    public long ReceivedBytes { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    private FileUploadSession() { } // For EF Core

    public FileUploadSession(
        Guid id,
        Guid clinicId,
        Guid patientId,
        string fileName,
        string contentType,
        long declaredLength,
        string storageReference,
        int chunkSize,
        DateTime nowUtc,
        Guid? folderId = null,
        string? description = null,
        string? uploadedBy = null)
    {
        if (declaredLength <= 0)
        {
            throw new ArgumentException("An upload must declare a length.", nameof(declaredLength));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentException("An upload must have a chunk size.", nameof(chunkSize));
        }

        Id = id;
        ClinicId = clinicId;
        PatientId = patientId;
        FolderId = folderId;
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        ContentType = contentType ?? throw new ArgumentNullException(nameof(contentType));
        DeclaredLength = declaredLength;
        Description = description;
        UploadedBy = uploadedBy;
        StorageReference = storageReference ?? throw new ArgumentNullException(nameof(storageReference));
        ChunkSize = chunkSize;
        ReceivedParts = 0;
        ReceivedBytes = 0;
        CreatedAtUtc = nowUtc;
        ExpiresAtUtc = nowUtc.Add(Lifetime);
    }

    /// <summary>How many parts the whole file comes to, from the length the client declared.</summary>
    public int TotalParts => (int)((DeclaredLength + ChunkSize - 1) / ChunkSize);

    /// <summary>The part a client may send next. Equal to <see cref="TotalParts"/> plus one once all have arrived.</summary>
    public int NextPart => ReceivedParts + 1;

    public bool IsComplete => ReceivedParts >= TotalParts;

    public bool HasExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    /// <summary>
    /// How long the part numbered <paramref name="partNumber"/> must be: the chunk size, or whatever is left for
    /// the last one.
    ///
    /// <para>⚠️ <b>Checked rather than trusted, because a short part is a hole in the middle of a radiograph.</b>
    /// Nothing downstream re-measures a part — the assembly concatenates whatever is staged — so a chunk cut off
    /// by a dropped connection and accepted here would produce a file that is the right length in the row and
    /// corrupt on disk.</para>
    /// </summary>
    public long ExpectedPartLength(int partNumber)
    {
        if (partNumber < TotalParts)
        {
            return ChunkSize;
        }

        var remainder = DeclaredLength % ChunkSize;
        return remainder == 0 ? ChunkSize : remainder;
    }

    /// <summary>
    /// Records that part <see cref="NextPart"/> arrived.
    ///
    /// <para>⚠️ <b>Re-sending the part already accepted is a no-op, not an error.</b> A client that lost the
    /// response cannot tell « stored » from « never arrived », and the honest answer to both is the same: say
    /// what has been received and let it continue.</para>
    /// </summary>
    public void AcceptPart(int partNumber, long length)
    {
        if (partNumber == ReceivedParts)
        {
            return;
        }

        if (partNumber != NextPart)
        {
            throw new InvalidOperationException(
                $"Part {partNumber} cannot be accepted; this upload expects part {NextPart}.");
        }

        ReceivedParts = partNumber;
        ReceivedBytes += length;
    }

    /// <summary>Extends the window, so a slow but live upload does not expire under a client still sending.</summary>
    public void KeepAlive(DateTime nowUtc) => ExpiresAtUtc = nowUtc.Add(Lifetime);
}
