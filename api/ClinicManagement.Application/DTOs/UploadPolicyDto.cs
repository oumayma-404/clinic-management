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
    /// <summary>The door this describes — <c>patient-file</c> today, and the only one served.</summary>
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
}
