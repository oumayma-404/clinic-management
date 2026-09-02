using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// Whether a file has a picture the drawer can paint — <b>one answer, asked in two places</b>.
///
/// <para>⚠️ It exists because those two places must agree or the feature is invisible. The preview route decides
/// what to serve; <c>PatientFileDto.HasPreview</c> decides whether the browser asks at all. A row that the route
/// would happily serve but whose DTO says « no preview » is never requested, and one whose DTO says « yes » to a
/// route that refuses shows a broken tile. Neither is visible in a type, and the second half of the pair — the
/// mapping — is the easy one to forget.</para>
/// </summary>
public static class PatientFilePreviewPolicy
{
    /// <summary>Whether a stand-in image was stored for this file when it arrived.</summary>
    public static bool HasStoredPreview(PatientFile file) =>
        !string.IsNullOrEmpty(file.PreviewStorageKey);

    /// <summary>
    /// Whether this file's own bytes may be served where a stand-in should have been.
    ///
    /// <para>⚠️ For the files already in every clinic's drawer. Previews are built by the browser on the way up,
    /// so nothing stored before that existed has one — on a real database that is most of them. Backfilling
    /// means a server-side image pipeline; serving a <i>small</i> original costs a few hundred kilobytes.</para>
    ///
    /// <para>Three conditions, each doing work: <b>hosted</b> (a coffre original never reached this deployment),
    /// <b>browser-paintable</b> (the tile is an <c>&lt;img&gt;</c>), and <b>small</b> — this route is called once
    /// per tile in a list, so the ceiling is « cheap forty times over on a clinic's uplink », not « a reasonable
    /// file ». Anything larger keeps showing its icon, exactly as it did before.</para>
    /// </summary>
    public static bool CanStandInForItsOwnPreview(PatientFile file)
    {
        if (file.Residency != FileResidency.Hosted) return false;
        if (string.IsNullOrEmpty(file.StorageKey)) return false;
        if (file.FileSize <= 0 || file.FileSize > FileTypeCatalog.PreviewFallbackBytes) return false;

        // The catalog's own answer, keyed on the stored name — not a second list, and not the content type,
        // which a row predating the catalog may carry from the client's claim rather than from a validated read.
        var entry = FileTypeCatalog.TryGet(FileNameSanitizer.ExtensionOf(file.FileName));

        return entry is { IsBrowserPreviewable: true }
            && entry.ContentType.StartsWith("image/", StringComparison.Ordinal);
    }

    /// <summary>What <c>PatientFileDto.HasPreview</c> reports: whether asking the preview route is worth it.</summary>
    public static bool HasSomethingToShow(PatientFile file) =>
        HasStoredPreview(file) || CanStandInForItsOwnPreview(file);
}
