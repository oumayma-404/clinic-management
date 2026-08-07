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

    private FileUploadProfile(string name, IReadOnlyList<FileTypeEntry> entries)
    {
        Name = name;
        Entries = entries;
        _byExtension = entries
            .SelectMany(entry => entry.Extensions.Select(extension => (extension, entry)))
            .ToDictionary(pair => pair.extension, pair => pair.entry, StringComparer.Ordinal);
    }

    public string Name { get; }

    public IReadOnlyList<FileTypeEntry> Entries { get; }

    /// <summary>Everything the catalog knows — a patient's file drawer is where a clinic's real formats land.</summary>
    public static readonly FileUploadProfile PatientFile = new("patient-file", FileTypeCatalog.All);

    /// <summary>
    /// The practitioner cachet and the clinic logo. Raster only, and small: both are read fully into memory on
    /// every document render, and both are served back inline from the app's own origin.
    /// </summary>
    public static readonly FileUploadProfile ProfileImage = new(
        "profile-image", new[] { FileTypeCatalog.Png, FileTypeCatalog.Jpeg });

    /// <summary>The PDF a medical document renders to before it is filed in the patient's « documents » folder.</summary>
    public static readonly FileUploadProfile MedicalDocumentPdf = new(
        "medical-document-pdf", new[] { FileTypeCatalog.Pdf });

    /// <summary>The patient import's spreadsheet export.</summary>
    public static readonly FileUploadProfile Csv = new(
        "csv", FileTypeCatalog.All.Where(entry => entry.Extensions.Contains("csv") || entry.Extensions.Contains("txt")).ToList());

    public FileTypeEntry? TryGet(string extension) =>
        _byExtension.TryGetValue(extension, out var entry) ? entry : null;

    /// <summary>The largest file this door accepts — what a client-side pre-check and a size message quote.</summary>
    public long MaxBytes => Entries.Max(entry => entry.MaxBytes);

    /// <summary>« .pdf, .png, .jpg … » — every extension this door accepts, in catalog order.</summary>
    public string AcceptedExtensionList =>
        string.Join(", ", Entries.SelectMany(entry => entry.Extensions).Select(extension => $".{extension}"));

    public string UnsupportedMessage =>
        $"Format de fichier non pris en charge. Formats acceptés : {AcceptedExtensionList}.";
}
