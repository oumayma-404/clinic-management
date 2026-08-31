using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// The single authority on what may be uploaded anywhere in the product, and on what each format costs.
///
/// <para>It replaces <c>FileContentValidation</c>, whose allow-list was keyed on the declared content type and
/// whose signature switch had a <c>_ =&gt; false</c> default — so any format with no magic bytes was refused by
/// construction, and adding one was impossible rather than merely undone.</para>
///
/// <para>⚠️ Office formats are accepted here, reversing that file's stated « deliberately excludes Office formats
/// (macro vector) ». The app never executes an uploaded byte: downloads are <c>attachment</c> + <c>nosniff</c> and
/// behind a bearer token, so a macro can only run where the operator has already saved the file and opened Word —
/// exactly as it would from the mail the file arrived in. What a clinic actually receives from a laboratory is a
/// <c>.docx</c> or an <c>.xlsx</c>, and refusing them only moves the file to a USB stick.</para>
/// </summary>
public static class FileTypeCatalog
{
    /// <summary>Documents, text and raster images — big enough for a full-mouth series, small enough to stream.</summary>
    public const long DocumentBytes = 25L * 1024 * 1024;

    /// <summary>DICOM studies, meshes and lab archives. A CBCT export is routinely over 100 MB.</summary>
    public const long LargeBytes = 150L * 1024 * 1024;

    /// <summary>
    /// The largest cap any entry carries — the ceiling an upload action's <c>[RequestSizeLimit]</c> must be sized
    /// from. A <c>const</c> because an attribute argument has to be one; <c>FileTypeCatalogTests</c> pins it
    /// against the entries so it cannot fall behind a widened one.
    /// </summary>
    public const long MaxBytesAcrossCatalog = LargeBytes;

    /// <summary>
    /// The largest file the cabinet's coffre will take — a raw scanner export, with room to spare. It bounds a
    /// runaway rather than anyone's disk bill: these bytes never leave the practice's own hardware.
    /// </summary>
    public const long VaultBytes = 64L * 1024 * 1024 * 1024;

    /// <summary>
    /// The ceiling on the small image standing in for a vault original off-site. A preview above it is
    /// <b>dropped</b> and the file still registered — previews are the one part of a coffre file the deployment
    /// does store, so an unbounded one would rebuild the problem the residency exists to remove.
    /// </summary>
    public const long PreviewBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Above this, imaging and lab archives are filed in the cabinet's coffre instead of hosted. It is
    /// <see cref="DocumentBytes"/> and not a number of its own: the line already drawn between « a document » and
    /// « a study » is the same line, and a second constant beside it would be the one to drift.
    /// </summary>
    private static readonly ResidencyRule LargeStaysAtTheCabinet = ResidencyRule.HostedUpTo(DocumentBytes);

    private static readonly byte[] Zip = { 0x50, 0x4B, 0x03, 0x04 };
    private static readonly byte[] ZipEmpty = { 0x50, 0x4B, 0x05, 0x06 };
    private static readonly byte[] ZipSpanned = { 0x50, 0x4B, 0x07, 0x08 };
    private static readonly byte[] OleCompound = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

    public static readonly FileTypeEntry Pdf = new(
        new[] { "pdf" }, "application/pdf", FileType.MedicalRecord, DocumentBytes,
        SignatureRule.Required(0, "%PDF-"), true, "PDF");

    public static readonly FileTypeEntry Png = new(
        new[] { "png" }, "image/png", FileType.Scan, DocumentBytes,
        SignatureRule.Required(0, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), true, "PNG");

    public static readonly FileTypeEntry Jpeg = new(
        new[] { "jpg", "jpeg" }, "image/jpeg", FileType.Scan, DocumentBytes,
        SignatureRule.Required(0, new byte[] { 0xFF, 0xD8, 0xFF }), true, "JPEG");

    private static readonly FileTypeEntry[] Entries =
    {
        Pdf,
        Png,
        Jpeg,

        // AC-3.1 — imaging and photos. HEIC is here because an iPhone photographing a case is the normal path.
        new(new[] { "webp" }, "image/webp", FileType.Scan, DocumentBytes,
            // "WEBP" at offset 8 rather than "RIFF" at 0: a WAV file opens with RIFF too.
            SignatureRule.Required(8, "WEBP"), true, "WebP"),
        new(new[] { "gif" }, "image/gif", FileType.Scan, DocumentBytes,
            SignatureRule.Required(0, "GIF87a", "GIF89a"), true, "GIF"),
        new(new[] { "tiff", "tif" }, "image/tiff", FileType.Scan, DocumentBytes,
            SignatureRule.Required(0, new byte[] { 0x49, 0x49, 0x2A, 0x00 }, new byte[] { 0x4D, 0x4D, 0x00, 0x2A }),
            false, "TIFF"),
        new(new[] { "bmp" }, "image/bmp", FileType.Scan, DocumentBytes,
            SignatureRule.Required(0, "BM"), true, "BMP"),
        new(new[] { "heic", "heif" }, "image/heic", FileType.Scan, DocumentBytes,
            // The ISO-BMFF `ftyp` box at offset 4; the brand that follows it varies by device and iOS version.
            SignatureRule.Required(4, "ftyp"), false, "HEIC"),

        // AC-3.2 — dental 3D and CBCT. These are the six formats a study arrives in, and the only ones the
        // coffre takes: above DocumentBytes their bytes stay on the cabinet's own hardware.
        new(new[] { "dcm", "dicom" }, "application/dicom", FileType.Scan, LargeBytes,
            // AC-2.4: DICM sits behind a 128-byte preamble, and preamble-less exports from real scanners exist.
            SignatureRule.Advisory(128, "DICM"), false, "DICOM",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),
        new(new[] { "stl" }, "model/stl", FileType.Other, LargeBytes,
            SignatureRule.None("un STL ASCII commence par du texte libre et un STL binaire par un en-tête de 80 octets sans marqueur"),
            false, "STL",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),
        new(new[] { "ply" }, "model/ply", FileType.Other, LargeBytes,
            SignatureRule.Required(0, "ply"), false, "PLY",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),
        new(new[] { "obj" }, "model/obj", FileType.Other, LargeBytes,
            SignatureRule.None("Wavefront OBJ est un format texte sans marqueur d'en-tête"), false, "OBJ",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),
        new(new[] { "3mf" }, "model/3mf", FileType.Other, LargeBytes,
            SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "3MF",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),
        new(new[] { "zip" }, "application/zip", FileType.Other, LargeBytes,
            SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "ZIP",
            residency: LargeStaysAtTheCabinet, vaultMaxBytes: VaultBytes),

        // AC-3.3 — office and text.
        new(new[] { "docx" }, "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            FileType.MedicalRecord, DocumentBytes, SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "Word (docx)"),
        new(new[] { "xlsx" }, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileType.MedicalRecord, DocumentBytes, SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "Excel (xlsx)"),
        new(new[] { "doc" }, "application/msword", FileType.MedicalRecord, DocumentBytes,
            SignatureRule.Required(0, OleCompound), false, "Word (doc)"),
        new(new[] { "xls" }, "application/vnd.ms-excel", FileType.MedicalRecord, DocumentBytes,
            SignatureRule.Required(0, OleCompound), false, "Excel (xls)"),
        new(new[] { "odt" }, "application/vnd.oasis.opendocument.text", FileType.MedicalRecord, DocumentBytes,
            SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "OpenDocument (odt)"),
        new(new[] { "ods" }, "application/vnd.oasis.opendocument.spreadsheet", FileType.MedicalRecord, DocumentBytes,
            SignatureRule.Required(0, Zip, ZipEmpty, ZipSpanned), false, "OpenDocument (ods)"),
        new(new[] { "rtf" }, "application/rtf", FileType.MedicalRecord, DocumentBytes,
            SignatureRule.Required(0, "{\\rtf"), false, "RTF"),
        new(new[] { "txt" }, "text/plain", FileType.LabResult, DocumentBytes,
            SignatureRule.None("un fichier texte brut n'a pas de marqueur d'en-tête"), false, "Texte"),
        new(new[] { "csv" }, "text/csv", FileType.LabResult, DocumentBytes,
            SignatureRule.None("un CSV est du texte brut, sans marqueur d'en-tête"), false, "CSV"),
    };

    /// <summary>
    /// Refused <b>before</b> the allow-list, with their own message. Two families: what an operating system will
    /// execute, and what renders as markup in the app's own origin — an SVG opened through a <c>blob:</c> URL
    /// inherits the creating document's origin, so the attachment-only download that makes it harmless today
    /// stops being the only protection the moment a thumbnail or a preview shows one.
    /// </summary>
    public static readonly IReadOnlySet<string> DeniedExtensions = new HashSet<string>(StringComparer.Ordinal)
    {
        "exe", "dll", "com", "bat", "cmd", "msi", "msp", "scr", "pif", "cpl", "jar", "app", "apk", "deb", "rpm",
        "js", "mjs", "cjs", "vbs", "vbe", "wsf", "wsh", "ps1", "psm1", "sh", "bash", "py", "pl", "php", "rb",
        "hta", "lnk", "reg", "url", "scf", "swf",
        "svg", "svgz", "html", "htm", "xhtml", "xht", "shtml", "mht", "mhtml"
    };

    private static readonly Dictionary<string, FileTypeEntry> ByExtension = Entries
        .SelectMany(entry => entry.Extensions.Select(extension => (extension, entry)))
        .ToDictionary(pair => pair.extension, pair => pair.entry, StringComparer.Ordinal);

    public static IReadOnlyList<FileTypeEntry> All => Entries;

    /// <summary>Looks up a lower-case, dot-less extension. Unknown ones are simply not accepted.</summary>
    public static FileTypeEntry? TryGet(string extension) =>
        ByExtension.TryGetValue(extension, out var entry) ? entry : null;
}
