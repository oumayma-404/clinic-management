using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// What a cabinet's archive <b>is</b> on disk: the schema version, the entry layout inside the zip, and the
/// French refusals for a file this build cannot accept.
///
/// <para><b>Why an archive exists at all.</b> On the hosted deployment the clinic's data lives in a database it
/// does not administer, and <c>pg_dump</c> cannot serve it — the tool takes <c>--dbname</c> and has no tenant
/// predicate, so one cabinet's « sauvegarde » would be every other cabinet's patients. This is the per-clinic
/// answer: the same rows, through the same tenant filter every read in the product goes through, as one file the
/// practice keeps on its own PC.</para>
///
/// <para>⚠️ <b>It is not encrypted, deliberately and visibly.</b> A full copy of a cabinet's medical records in a
/// file on a laptop is exactly what it looks like; the screen says so in French and the operator guidance says
/// where to keep it. Encrypting it is a separate decision with its own key-management question, and shipping a
/// password box that protects nothing would be worse than the plain statement.</para>
/// </summary>
public static class ClinicArchiveFormat
{
    /// <summary>
    /// The manifest schema this build writes and is the only one it reads.
    ///
    /// <para><b>Bumped whenever the meaning of an entry changes</b>, never for a new entity type: adding a table
    /// leaves every older archive readable, because the restore walks the entries the file actually carries and an
    /// absent one is simply a table with nothing to put back. What forces a bump is a change to how a row is
    /// written — a different value encoding, a renamed entry, a split file.</para>
    /// </summary>
    public const int SchemaVersion = 1;

    /// <summary>The manifest, read before a single row is.</summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>
    /// The entity whose single row <i>is</i> the cabinet. Named here rather than as a <c>nameof(Clinic)</c> at
    /// each reader, because the entry names in the archive are a wire format: the day the CLR type is renamed,
    /// every archive already on a practice's laptop still says « Clinic ».
    /// </summary>
    public const string ClinicEntity = "Clinic";

    /// <summary>Folder holding one <c>&lt;EntityType&gt;.json</c> per archived table.</summary>
    public const string DataFolder = "data/";

    /// <summary>Folder holding the blobs, each at its own <b>storage key</b> — see <see cref="BlobEntry"/>.</summary>
    public const string BlobFolder = "blobs/";

    /// <summary>Media type of the download.</summary>
    public const string ContentType = "application/zip";

    /// <summary>The refusal codes the client branches on. Never its French sentence — that is prose, and prose is reworded.</summary>
    public const string InvalidCode = "archive_invalid";
    public const string ClinicMismatchCode = "archive_clinic_mismatch";
    public const string SchemaUnsupportedCode = "archive_schema_unsupported";
    public const string ClinicExistsCode = "clinic_exists";

    /// <summary>
    /// How rows are written and read back. Indented so the file is legible to whoever the practice hands it to —
    /// an archive nobody can open is a promise nobody can check — and unescaped so French accents survive as
    /// characters rather than as <c>é</c>.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The entry a table's rows live at.</summary>
    public static string DataEntry(string entityName) => $"{DataFolder}{entityName}.json";

    /// <summary>
    /// The entry a blob lives at: <c>blobs/</c> then the blob's <b>own storage key, verbatim</b>.
    ///
    /// <para>⚠️ Verbatim is the whole rule, and it is what makes a historical file restorable. A key written
    /// before <c>multi-tenant-cloud</c> US-5 is flat (<c>{guid}-{timestamp}</c>, no <c>clinics/{id}/</c> prefix)
    /// and <c>IFileStorage.DownloadAsync</c> resolves it verbatim by contract — so re-prefixing on the way back in
    /// would write the bytes where the restored row does not point, and the file would download as « introuvable »
    /// on a row that looks perfectly healthy.</para>
    /// </summary>
    public static string BlobEntry(string storageKey) => $"{BlobFolder}{storageKey}";

    /// <summary>The storage key an entry under <see cref="BlobFolder"/> belongs to, or null if it is not one.</summary>
    public static string? StorageKeyOf(string entryName) =>
        entryName.StartsWith(BlobFolder, StringComparison.Ordinal) && entryName.Length > BlobFolder.Length
            ? entryName[BlobFolder.Length..]
            : null;

    /// <summary>
    /// The file name the download is offered under — cabinet + <b>clinic-local</b> day, never UTC.
    ///
    /// <para>The day comes from <c>ClinicClock.ClinicToday()</c>, as every dated export in this product does: an
    /// owner archives repeatedly, and an archive taken at 00:30 Tunis filed under the previous day is how the
    /// wrong file gets restored.</para>
    /// </summary>
    public static string FileName(string clinicName, DateTime clinicDay)
    {
        var slug = new string(clinicName
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray())
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return $"archive-{(slug.Length == 0 ? "cabinet" : slug)}-{clinicDay:yyyy-MM-dd}.zip";
    }
}

/// <summary>
/// The archive's own description of itself, read <b>before</b> anything is written back (AC-7).
///
/// <para>It carries the clinic id so a file cannot be restored into the wrong cabinet by accident (AC-6), the
/// schema version so an unreadable file is refused rather than half-applied, and the per-table counts so a
/// truncated download is visible as a disagreement rather than as a smaller practice.</para>
/// </summary>
public sealed record ClinicArchiveManifest
{
    /// <summary>The schema the writer used. Refused when it is not <see cref="ClinicArchiveFormat.SchemaVersion"/>.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>The cabinet these rows belong to — and, on the console path, the id the cabinet is re-created at.</summary>
    public Guid ClinicId { get; init; }

    /// <summary>The cabinet's name when the archive was taken, so a file found later can be identified without opening it.</summary>
    public string ClinicName { get; init; } = string.Empty;

    /// <summary>When it was taken.</summary>
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Rows written per table, in the order the restore must apply them (parents before children).</summary>
    public IReadOnlyList<ClinicArchiveTableCount> Tables { get; init; } = Array.Empty<ClinicArchiveTableCount>();

    /// <summary>Blobs written, and how many the writer could not read — stated rather than silently dropped.</summary>
    public int BlobCount { get; init; }

    /// <summary>What the writer could not include, in French, so the file explains its own gaps.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>One table's row count in the manifest.</summary>
public sealed record ClinicArchiveTableCount(string Entity, int Rows);
