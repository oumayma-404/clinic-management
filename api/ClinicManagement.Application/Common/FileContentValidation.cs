namespace ClinicManagement.Application.Common;

/// <summary>
/// Content-type and magic-byte validation for uploads (security-hardening US-11, audit § 2 finding 12).
///
/// <para>Patient-file upload accepted <b>any</b> client-declared content type — no allow-list, no signature
/// check, no size cap — and the stored type was echoed back on download. The doctor-cachet path already did
/// all three correctly, so this extracts that logic into one place rather than reimplementing it.</para>
///
/// <para>A declared <c>Content-Type</c> is trivially spoofable, so the bytes must agree with it. Rejecting
/// <c>image/svg+xml</c> and <c>text/html</c> matters specifically because these files are served back from the
/// app's own origin, where markup would execute.</para>
/// </summary>
public static class FileContentValidation
{
    /// <summary>Patient files: scans and referrals (PDF) plus intra-oral photos and radiographs.</summary>
    public const long MaxPatientFileBytes = 25L * 1024 * 1024;

    /// <summary>The cachet is read fully into memory on every document render, so it stays small.</summary>
    public const long MaxCachetBytes = 2L * 1024 * 1024;

    public const string Pdf = "application/pdf";
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    /// <summary>Accepted for a patient file. Deliberately excludes Office formats (macro vector).</summary>
    public static readonly string[] PatientFileTypes = { Pdf, Png, Jpeg };

    /// <summary>Accepted for the practitioner cachet — raster images only.</summary>
    public static readonly string[] ImageTypes = { Png, Jpeg };

    /// <summary>French, and names the accepted formats so the message is actionable.</summary>
    public const string UnsupportedPatientFileMessage =
        "Format de fichier non pris en charge. Seuls les fichiers PDF, PNG et JPEG sont acceptés.";

    public const string SignatureMismatchMessage =
        "Le contenu du fichier ne correspond pas à son format déclaré. Le fichier a peut-être été renommé.";

    public const string EmptyFileMessage = "Le fichier est vide.";

    public static string TooLargeMessage(long maxBytes) =>
        $"Fichier trop volumineux ({maxBytes / (1024 * 1024)} Mo maximum).";

    /// <summary>
    /// Canonicalises a declared type, or returns <c>null</c> when it is not in <paramref name="accepted"/>.
    /// <c>image/jpg</c> is folded to <c>image/jpeg</c> — browsers send both.
    /// </summary>
    public static string? Normalize(string? declaredContentType, string[] accepted)
    {
        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return null;
        }

        // Strip any charset/boundary parameters before comparing.
        var type = declaredContentType.Split(';')[0].Trim().ToLowerInvariant();
        if (type == "image/jpg")
        {
            type = Jpeg;
        }

        return accepted.Contains(type) ? type : null;
    }

    /// <summary>True when the leading bytes match the signature for <paramref name="contentType"/>.</summary>
    public static bool MatchesSignature(string contentType, byte[] bytes) => contentType switch
    {
        Png => IsPng(bytes),
        Jpeg => IsJpeg(bytes),
        Pdf => IsPdf(bytes),
        _ => false
    };

    public static bool IsPng(byte[] b) =>
        b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
        && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A;

    public static bool IsJpeg(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    public static bool IsPdf(byte[] b) =>
        b.Length >= 5 && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D; // %PDF-
}
