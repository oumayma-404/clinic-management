namespace ClinicManagement.Application.Common.Files;

/// <summary>
/// What one upload door accepts. Every upload site names a profile, so « what may I send here? » has one answer
/// per door and none of them carries its own copy of the rule.
///
/// <para>The refusal message is <b>derived</b> (AC-2.9): a hardcoded sentence naming PDF/PNG/JPEG is how the old
/// validator came to describe an allow-list it no longer had, and a widened catalog with a stale sentence tells
/// the user the file they are holding is refused when it is not.</para>
/// </summary>
public sealed class FileUploadProfile
{
    private readonly Dictionary<string, FileTypeEntry> _byExtension;
    private readonly long? _maxBytesOverride;

    private FileUploadProfile(string name, IReadOnlyList<FileTypeEntry> entries, long? maxBytesOverride = null)
    {
        Name = name;
        Entries = entries;
        _maxBytesOverride = maxBytesOverride;
        _byExtension = entries
            .SelectMany(entry => entry.Extensions.Select(extension => (extension, entry)))
            .ToDictionary(pair => pair.extension, pair => pair.entry, StringComparer.Ordinal);
    }

    public string Name { get; }

    public IReadOnlyList<FileTypeEntry> Entries { get; }

    /// <summary>
    /// This door's cap on a single file, which is the entry's own unless the door is tighter.
    ///
    /// <para>⚠️ <b>A door may be smaller than the format it accepts, and one of them has to be.</b> PNG and JPEG
    /// are shared between a patient's file drawer — where a 40 Mo panoramique is ordinary — and the cachet and
    /// clinic logo, which are read fully into memory on every rendered document. Without a per-door cap those two
    /// numbers are one number, and raising it for the radiograph raises it for the letterhead.</para>
    /// </summary>
    public long CapFor(FileTypeEntry entry) =>
        _maxBytesOverride is { } cap && cap < entry.MaxBytes ? cap : entry.MaxBytes;

    /// <summary>Everything the catalog knows — a patient's file drawer is where a clinic's real formats land.</summary>
    public static readonly FileUploadProfile PatientFile = new("patient-file", FileTypeCatalog.All);

    /// <summary>
    /// The practitioner cachet and the clinic logo. Raster only, and small: both are read fully into memory on
    /// every document render, and both are served back inline from the app's own origin.
    /// </summary>
    public static readonly FileUploadProfile ProfileImage = new(
        "profile-image", new[] { FileTypeCatalog.Png, FileTypeCatalog.Jpeg },
        maxBytesOverride: FileTypeCatalog.ProfileImageBytes);

    /// <summary>The PDF a medical document renders to before it is filed in the patient's « documents » folder.</summary>
    public static readonly FileUploadProfile MedicalDocumentPdf = new(
        "medical-document-pdf", new[] { FileTypeCatalog.Pdf });

    /// <summary>The patient import's spreadsheet export.</summary>
    public static readonly FileUploadProfile Csv = new(
        "csv", FileTypeCatalog.All.Where(entry => entry.Extensions.Contains("csv") || entry.Extensions.Contains("txt")).ToList());

    /// <summary>
    /// Every door, by the name it publishes. It exists so <c>GET /api/meta/upload-policy</c> can serve any of them
    /// rather than only the patient's file drawer: the cachet, the clinic logo and the CSV import each had a
    /// hand-written <c>accept</c> in the browser, and all three disagreed with this file — <c>image/*</c> against a
    /// PNG-and-JPEG door, and <c>.csv</c> against a door that also takes <c>.txt</c>.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, FileUploadProfile> ByName =
        new Dictionary<string, FileUploadProfile>(StringComparer.Ordinal)
        {
            [PatientFile.Name] = PatientFile,
            [ProfileImage.Name] = ProfileImage,
            [MedicalDocumentPdf.Name] = MedicalDocumentPdf,
            [Csv.Name] = Csv,
        };

    public static FileUploadProfile? TryByName(string? name) =>
        name is not null && ByName.TryGetValue(name, out var profile) ? profile : null;

    public FileTypeEntry? TryGet(string extension) =>
        _byExtension.TryGetValue(extension, out var entry) ? entry : null;

    /// <summary>The largest file this door accepts — what a client-side pre-check and a size message quote.</summary>
    public long MaxBytes => Entries.Max(CapFor);

    /// <summary>« .pdf, .png, .jpg … » — every extension this door accepts, in catalog order.</summary>
    public string AcceptedExtensionList =>
        string.Join(", ", Entries.SelectMany(entry => entry.Extensions).Select(extension => $".{extension}"));

    public string UnsupportedMessage =>
        $"Format de fichier non pris en charge. Formats acceptés : {AcceptedExtensionList}.";
}
