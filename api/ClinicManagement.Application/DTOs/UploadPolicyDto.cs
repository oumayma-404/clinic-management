namespace ClinicManagement.Application.DTOs;

/// <summary>
/// What one upload door accepts, as the browser needs it — the <c>accept</c> attribute, the per-format caps and
/// the refusal sentences, all projected from <c>FileTypeCatalog</c>.
///
/// <para>It exists so the client stops mirroring the allow-list by hand: the picker's <c>accept</c> used to be a
/// literal <c>application/pdf,image/png,image/jpeg</c> beside a comment claiming it mirrored the server, which was
/// true when written and false the moment the catalog widened.</para>
/// </summary>
public class UploadPolicyDto
{
    /// <summary>
    /// The door this describes: <c>patient-file</c>, <c>profile-image</c>, <c>medical-document-pdf</c> or
    /// <c>csv</c>. It is echoed back so a client that asked for one cannot render another's ceiling.
    /// </summary>
    public string Profile { get; set; } = string.Empty;

    /// <summary>The largest file any format of this door accepts.</summary>
    public long MaxBytes { get; set; }

    /// <summary>Ready for the picker's <c>accept</c> attribute: « .pdf,.png,… ».</summary>
    public string Accept { get; set; } = string.Empty;

    public List<UploadPolicyFormatDto> Formats { get; set; } = new();

    /// <summary>Extensions refused before the allow-list is even consulted.</summary>
    public List<string> DeniedExtensions { get; set; } = new();

    /// <summary>The server's own wording for an extension this door does not accept.</summary>
    public string UnsupportedMessage { get; set; } = string.Empty;

    /// <summary>The server's own wording for a deny-listed extension.</summary>
    public string DeniedMessage { get; set; } = string.Empty;

    /// <summary>
    /// Whether this deployment files large studies in the cabinet's own coffre. False where the clinic's machine
    /// already holds every blob, and there every format reads as always-hosted.
    /// </summary>
    public bool VaultAvailable { get; set; }

    /// <summary>The server's own wording for « this one belongs at the cabinet and you have no coffre here ».</summary>
    public string VaultUnavailableMessage { get; set; } = string.Empty;

    /// <summary>
    /// The size of every part but the last, for an upload sent in pieces — or <b>zero where this door has no
    /// resumable endpoints</b>, which is what a browser reads to know the single POST is its only option.
    ///
    /// <para>⚠️ It is published rather than agreed by constant because it is the browser's own threshold: a file
    /// that fits in one part gains nothing from three extra round trips, and its progress bar would go from
    /// « 0 % » to « 100 % » with nothing in between — an animation, not a measurement. So « is this file worth
    /// chunking? » is exactly « is it bigger than one part? », and asking the server means the answer cannot
    /// drift the way a second copy of the number would.</para>
    /// </summary>
    public long ResumableChunkBytes { get; set; }

    /// <summary>
    /// The cabinet's storage ceiling in bytes, or <b>0 where nothing is enforced</b> — which is the case on a
    /// deployment where the clinic's own machine holds the blobs (<c>large-file-transfer</c> Part 4).
    /// </summary>
    public long StorageQuotaBytes { get; set; }

    /// <summary>
    /// What the cabinet is already using of it. Always 0 when <see cref="StorageQuotaBytes"/> is 0.
    ///
    /// <para>⚠️ It rides on the upload policy rather than an endpoint of its own because it answers the same
    /// question every other field here answers — « what will be refused, and why » — and the picker already
    /// reads this on the one screen where files are added. A second request would be a second moment at which
    /// the browser's idea of the ceiling could differ from the server's.</para>
    /// </summary>
    public long StorageUsedBytes { get; set; }

    /// <summary>
    /// The server's own « plein » sentence, so the instant refusal and the one the server would give are the
    /// same words. Empty where nothing is enforced.
    ///
    /// <para>⚠️ Composed here rather than in the browser for `TooLargeMessage`'s reason: the numbers are on this
    /// DTO, so a client could assemble the sentence — and then two places would decide how a size is written in
    /// French, which is how « 9.8 GB » ends up in the middle of a French refusal.</para>
    /// </summary>
    public string StorageFullMessage { get; set; } = string.Empty;
}

public class UploadPolicyFormatDto
{
    /// <summary>Lower-case, without the dot.</summary>
    public List<string> Extensions { get; set; } = new();

    public string ContentType { get; set; } = string.Empty;

    /// <summary>French, for a refusal that names the format rather than its MIME type.</summary>
    public string Label { get; set; } = string.Empty;

    public long MaxBytes { get; set; }

    /// <summary>Whether a browser can render it inline — a HEIC cannot, and must show an icon, not a broken image.</summary>
    public bool IsBrowserPreviewable { get; set; }

    /// <summary>The server's own « trop volumineux » sentence for this format's cap, so the two agree word for word.</summary>
    public string TooLargeMessage { get; set; } = string.Empty;

    /// <summary>
    /// <c>hosted</c> — every file of this format is stored on the server — or <c>hostedUpTo</c>, where files past
    /// <see cref="HostedMaxBytes"/> are kept in the cabinet's coffre instead.
    /// </summary>
    public string Residency { get; set; } = string.Empty;

    /// <summary>
    /// The largest file of this format the <b>server</b> will hold. Distinct from <see cref="MaxBytes"/>: that is
    /// the door's ceiling, this is where the coffre takes over — <c>FileTypeCatalog.StudyStaysAtTheCabinetAbove</c>
    /// for a study format on a hosted deployment, and the door's own ceiling everywhere else.
    /// </summary>
    public long HostedMaxBytes { get; set; }

    /// <summary>The largest file the coffre will take, or zero where this format never goes there.</summary>
    public long VaultMaxBytes { get; set; }

    /// <summary>
    /// The server's own sentence for a file past even the coffre's ceiling, so the instant refusal and the one
    /// the server would give are the same words. Empty where this format never goes to the coffre.
    /// </summary>
    public string VaultTooLargeMessage { get; set; } = string.Empty;
}
