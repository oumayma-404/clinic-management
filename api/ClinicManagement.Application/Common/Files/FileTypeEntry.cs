using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// One accepted format. Keyed on <see cref="Extensions"/>, never on the declared content type: a browser derives
/// that header from the extension through the OS registry, so Windows — which registers nothing for <c>.stl</c>,
/// <c>.dcm</c>, <c>.ply</c> or <c>.obj</c> — sends <c>application/octet-stream</c> for every one of them. An
/// allow-list keyed on the header could not admit a single STL file however many types were added to it.
/// </summary>
public sealed class FileTypeEntry
{
    public FileTypeEntry(
        string[] extensions,
        string contentType,
        FileType category,
        long maxBytes,
        SignatureRule signature,
        bool isBrowserPreviewable,
        string label,
        ResidencyRule? residency = null,
        long vaultMaxBytes = 0)
    {
        Extensions = extensions;
        ContentType = contentType;
        Category = category;
        MaxBytes = maxBytes;
        Signature = signature;
        IsBrowserPreviewable = isBrowserPreviewable;
        Label = label;
        Residency = residency ?? ResidencyRule.AlwaysHosted;
        VaultMaxBytes = vaultMaxBytes;
    }

    /// <summary>Lower-case, without the dot. The first one is the canonical spelling.</summary>
    public string[] Extensions { get; }

    /// <summary>What the blob is stored and served as — derived here, never taken from the client.</summary>
    public string ContentType { get; }

    public FileType Category { get; }

    public long MaxBytes { get; }

    public SignatureRule Signature { get; }

    /// <summary>Whether a browser can render it inline — a HEIC cannot, and must show an icon, not a broken image.</summary>
    public bool IsBrowserPreviewable { get; }

    /// <summary>French, for the refusal messages and the upload policy the client renders.</summary>
    public string Label { get; }

    /// <summary>
    /// Where this format's files belong. <see cref="MaxBytes"/> is the cap on what the deployment will hold; a
    /// file above it is not refused, it is filed in the cabinet's coffre instead — which is why widening this
    /// costs the upload door nothing.
    /// </summary>
    public ResidencyRule Residency { get; }

    /// <summary>
    /// The largest file of this format the coffre will take. Zero for a format that never goes there. It bounds a
    /// runaway rather than the deployment's disk — the bytes are the cabinet's own — so it is generous.
    /// </summary>
    public long VaultMaxBytes { get; }
}
