using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Application.Common.Files;

/// <summary>An upload that passed every check, ready to be handed to storage.</summary>
public sealed class ValidatedUpload
{
    internal ValidatedUpload(string fileName, FileTypeEntry entry, Stream content, long byteLength)
    {
        FileName = fileName;
        Entry = entry;
        Content = content;
        ByteLength = byteLength;
    }

    /// <summary>Sanitized (AC-2.10) — never the client's string.</summary>
    public string FileName { get; }

    public FileTypeEntry Entry { get; }

    /// <summary>Positioned at the first byte, header included.</summary>
    public Stream Content { get; }

    public long ByteLength { get; }

    /// <summary>The type the blob is stored and served as — the catalog's, never the declared one.</summary>
    public string ContentType => Entry.ContentType;
}

/// <summary>
/// The one place an upload is judged, for all six doors.
///
/// <para><b>It never buffers the file.</b> Only a bounded header is read (AC-2.8); the remainder is handed on as a
/// stream, so a 150 MB CBCT study is validated without a 150 MB <c>MemoryStream</c> — and the judgement still
/// happens <b>before</b> any blob is written, so a refusal leaves nothing to clean up.</para>
/// </summary>
public static class FileUploadValidator
{
    /// <summary>Enough for every marker in the catalog — DICOM's sits deepest, at byte 128.</summary>
    public const int HeaderBytes = 4096;

    public const string SignatureMismatchMessage =
        "Le contenu du fichier ne correspond pas à son format déclaré. Le fichier a peut-être été renommé.";

    public const string EmptyFileMessage = "Le fichier est vide.";

    public const string MissingExtensionMessage =
        "Le nom du fichier n'a pas d'extension, le format ne peut pas être déterminé.";

    public const string DeniedMessage =
        "Ce type de fichier n'est pas autorisé pour des raisons de sécurité (programmes, scripts et pages web).";

    public static string TooLargeMessage(long maxBytes) =>
        $"Fichier trop volumineux ({maxBytes / (1024 * 1024)} Mo maximum).";

    /// <summary>
    /// Validates <paramref name="content"/> against <paramref name="profile"/> and returns it ready to store.
    ///
    /// <para><paramref name="declaredLength"/> is ASP.NET's own count of the parsed body part, not a client claim,
    /// so it can refuse an oversized upload before a byte is read. The length that gets persisted is measured from
    /// the stream itself whenever it can be.</para>
    /// </summary>
    public static async Task<Result<ValidatedUpload>> ValidateAsync(
        FileUploadProfile profile,
        string? fileName,
        long declaredLength,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var name = FileNameSanitizer.Sanitize(fileName);
        var resolved = ResolveEntry(profile, name);
        if (resolved.IsFailure)
        {
            return Result<ValidatedUpload>.FailureFrom(resolved);
        }

        var entry = resolved.Value!;

        if (declaredLength <= 0)
        {
            return Result<ValidatedUpload>.Failure(EmptyFileMessage);
        }

        // The DOOR's cap, not the entry's: a cachet and a panoramique are both JPEG, and only one of them may be
        // fifty megabytes. `CapFor` is the entry's own unless the profile is tighter.
        var maxBytes = profile.CapFor(entry);
        if (declaredLength > maxBytes)
        {
            return Result<ValidatedUpload>.Failure(TooLargeMessage(maxBytes));
        }

        var header = new byte[HeaderBytes];
        var read = await ReadHeaderAsync(content, header, cancellationToken);
        if (read == 0)
        {
            return Result<ValidatedUpload>.Failure(EmptyFileMessage);
        }

        if (!SignatureAgrees(entry, header, read))
        {
            return Result<ValidatedUpload>.Failure(SignatureMismatchMessage);
        }

        var body = Rewind(content, header, read);
        var byteLength = body.CanSeek ? body.Length - body.Position : declaredLength;
        return Result<ValidatedUpload>.Success(new ValidatedUpload(name, entry, body, byteLength));
    }

    /// <summary>
    /// The half of the judgement that needs only a name: extension present, not deny-listed, and known to this
    /// door. Extracted so the coffre's registration — which has no original stream to inspect, because the bytes
    /// never left the cabinet — asks the same three questions in the same order and refuses in the same words.
    /// </summary>
    public static Result<FileTypeEntry> ResolveEntry(FileUploadProfile profile, string sanitizedName)
    {
        var extension = FileNameSanitizer.ExtensionOf(sanitizedName);
        if (extension.Length == 0)
        {
            return Result<FileTypeEntry>.Failure(MissingExtensionMessage);
        }

        // AC-2.5: the deny-list is asked first, and answers with its own reason — « non pris en charge » on a
        // .exe would read as a gap in the catalog rather than as a refusal.
        if (FileTypeCatalog.DeniedExtensions.Contains(extension))
        {
            return Result<FileTypeEntry>.Failure(DeniedMessage);
        }

        var entry = profile.TryGet(extension);

        return entry is null
            ? Result<FileTypeEntry>.Failure(profile.UnsupportedMessage)
            : Result<FileTypeEntry>.Success(entry);
    }

    /// <summary>
    /// Synchronous because a <c>Span</c> cannot live in an async method — and a span is what keeps the
    /// comparison allocation-free on every upload.
    /// </summary>
    private static bool SignatureAgrees(FileTypeEntry entry, byte[] header, int read)
    {
        var inspected = header.AsSpan(0, read);
        if (entry.Signature.Kind == SignatureKind.Required)
        {
            return entry.Signature.Matches(inspected);
        }

        // ⚠️ **The entry's OWN marker outranks any other format's claim, and its absence here was a real
        // refusal of real files.** DICOM is the only advisory entry, and the standard leaves its 128-byte
        // preamble entirely unspecified — exporters are free to put another format's header in it, and some put
        // a TIFF one, so `II*\0` at offset 0 followed by `DICM` at offset 128 is a perfectly ordinary DICOM.
        // Asking « does this claim to be something else? » first meant those were refused with « le fichier a
        // peut-être été renommé » about a file nobody had renamed. A marker at the offset its own format
        // declares is affirmative evidence; another format's marker somewhere the standard says is free space
        // is not. (Measured on two of pydicom's own test files, which carry exactly that preamble.)
        if (entry.Signature.Matches(inspected))
        {
            return true;
        }

        // AC-2.3: silence proves nothing, but a positive claim to be some other format does — this is what keeps
        // the reported .txt→.pdf refused while accepting an ASCII STL, which has no signature at all.
        var claimed = SignatureIndex.IdentifyOrNull(inspected);
        return claimed is null || claimed.ContentType == entry.ContentType;
    }

    private static async Task<int> ReadHeaderAsync(Stream content, byte[] header, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < header.Length)
        {
            var got = await content.ReadAsync(header.AsMemory(read, header.Length - read), cancellationToken);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read;
    }

    /// <summary>
    /// Puts the header back in front of the body. A seekable source — which is what
    /// <c>IFormFile.OpenReadStream()</c> gives — is simply rewound; that matters because
    /// <c>MinioFileStorage</c> buffers a non-seekable stream whole to learn its size, which would undo AC-2.8.
    /// </summary>
    private static Stream Rewind(Stream content, byte[] header, int read)
    {
        if (content.CanSeek)
        {
            content.Position = 0;
            return content;
        }

        return new PrefixedStream(header.AsMemory(0, read), content);
    }
}
