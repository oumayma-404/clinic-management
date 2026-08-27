using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// Writes a cabinet's archive and reads one back — the zip half, with no knowledge of EF and no knowledge of
/// HTTP. <see cref="IClinicArchiveStore"/> supplies the rows; <see cref="IFileStorage"/> supplies the bytes.
/// </summary>
public static class ClinicArchivePackager
{
    /// <summary>
    /// Packs <paramref name="clinicId"/>'s rows and blobs into <paramref name="output"/>.
    ///
    /// <para><b>A blob that cannot be read is a warning, not a failure</b>, and the manifest carries it. An object
    /// store that has lost one file must not cost the practice the other twenty thousand rows — and « l'archive
    /// est incomplète, voici ce qui manque » is a statement an owner can act on, where a refusal is not. The
    /// restore is additive, so a file that reappears is picked up by the next archive.</para>
    /// </summary>
    /// <param name="contents">
    /// Whether the blobs travel with the rows (<c>clinic-recovery-points</c>). It is written into the manifest, and
    /// that is load-bearing rather than bookkeeping: an unreadable blob is a *warning* here, so a rows-only archive
    /// and a full archive whose every blob failed both produce <c>BlobCount = 0</c> — « cette archive ne contient pas
    /// les fichiers » and « les fichiers n'ont pas pu être lus » are opposite facts with the same picture, and only
    /// the second should send somebody to look at the object store.
    /// </param>
    public static async Task<ClinicArchiveManifest> WriteAsync(
        Stream output,
        Guid clinicId,
        string clinicName,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        ILogger logger,
        ClinicArchiveContents contents = ClinicArchiveContents.RowsAndFiles,
        CancellationToken cancellationToken = default)
    {
        var export = await store.ExportAsync(clinicId, cancellationToken);
        var warnings = export.Warnings.ToList();

        // `leaveOpen` so the caller keeps ownership of the stream it handed us — the API streams it to the client.
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        foreach (var table in export.Tables)
        {
            await WriteTextEntryAsync(zip, ClinicArchiveFormat.DataEntry(table.Table), table.Json, cancellationToken);
        }

        var blobsWritten = 0;

        // ⚠️ The keys are skipped rather than the download failing per key: a rows-only archive must carry no
        // `blobs/` entry at all, so the restore's own blob loop finds nothing to look for and reports honestly.
        var blobKeys = contents == ClinicArchiveContents.RowsOnly
            ? Array.Empty<string>()
            : export.StorageKeys.ToArray();

        foreach (var storageKey in blobKeys)
        {
            try
            {
                await using var blob = await fileStorage.DownloadAsync(storageKey, cancellationToken);

                var entry = zip.CreateEntry(ClinicArchiveFormat.BlobEntry(storageKey), CompressionLevel.Fastest);
                await using var target = entry.Open();
                await blob.CopyToAsync(target, cancellationToken);

                blobsWritten++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Archive: blob {StorageKey} could not be read and is omitted.", storageKey);
                warnings.Add($"Le fichier « {storageKey} » n'a pas pu être lu et n'est pas inclus dans l'archive.");
            }
        }

        var manifest = new ClinicArchiveManifest
        {
            SchemaVersion = ClinicArchiveFormat.SchemaVersion,
            ClinicId = clinicId,
            ClinicName = clinicName,
            CreatedAtUtc = DateTime.UtcNow,
            Tables = export.Tables.Select(t => new ClinicArchiveTableCount(t.Table, t.RowCount)).ToList(),
            BlobCount = blobsWritten,
            Contents = contents,
            Warnings = warnings,
        };

        // Written last so its counts describe what actually landed — a blob that failed above is already
        // reflected in BlobCount rather than promised by a manifest written before the copy was attempted.
        await WriteTextEntryAsync(
            zip,
            ClinicArchiveFormat.ManifestEntry,
            JsonSerializer.Serialize(manifest, ClinicArchiveFormat.Json),
            cancellationToken);

        return manifest;
    }

    /// <summary>
    /// Reads the manifest out of an uploaded archive, refusing anything this build cannot apply — <b>before</b> a
    /// single row is written (AC-7).
    ///
    /// <para>Every refusal carries a machine-readable code beside its French sentence, so the client branches on
    /// the code and the user reads the prose. Recovering an outcome by matching the prose is the defect this
    /// repository deleted in <c>adoption-gaps-remediation</c>.</para>
    /// </summary>
    public static ClinicArchiveReadResult ReadManifest(ZipArchive zip)
    {
        var bomb = ClinicArchiveLimits.Refuse(zip);
        if (bomb is not null)
        {
            return ClinicArchiveReadResult.Refused(ClinicArchiveFormat.InvalidCode, bomb);
        }

        var entry = zip.GetEntry(ClinicArchiveFormat.ManifestEntry);
        if (entry is null)
        {
            return ClinicArchiveReadResult.Refused(
                ClinicArchiveFormat.InvalidCode,
                "Ce fichier n'est pas une archive de cabinet : le manifeste est absent.");
        }

        ClinicArchiveManifest? manifest;

        try
        {
            using var stream = entry.Open();
            manifest = JsonSerializer.Deserialize<ClinicArchiveManifest>(stream, ClinicArchiveFormat.Json);
        }
        catch (JsonException)
        {
            manifest = null;
        }

        if (manifest is null || manifest.ClinicId == Guid.Empty)
        {
            return ClinicArchiveReadResult.Refused(
                ClinicArchiveFormat.InvalidCode,
                "Le manifeste de cette archive est illisible. Le fichier est probablement incomplet ou corrompu.");
        }

        if (manifest.SchemaVersion != ClinicArchiveFormat.SchemaVersion)
        {
            // Both versions are named, because « incompatible » alone leaves the reader unable to tell whether the
            // file is too old for this application or the application too old for the file — opposite actions.
            return ClinicArchiveReadResult.Refused(
                ClinicArchiveFormat.SchemaUnsupportedCode,
                $"Cette archive est au format version {manifest.SchemaVersion}, et cette version de l'application "
                + $"lit le format version {ClinicArchiveFormat.SchemaVersion}. Aucune donnée n'a été modifiée.");
        }

        return ClinicArchiveReadResult.Accepted(manifest);
    }

    /// <summary>
    /// Whether the archive really carries the cabinet's own record, at the id its manifest claims.
    ///
    /// <para><b>The two are separate assertions and nothing tied them together.</b> Both restore doors gate on the
    /// manifest's <c>ClinicId</c> — the cabinet path checks it against the caller's clinic, the console path
    /// against « is this practice still live? » — while the <c>Clinic</c> row that actually lands comes from
    /// <c>data/Clinic.json</c>'s own <c>Id</c>. The archive is an unencrypted zip the practice holds, so by the
    /// time it comes back it is untrusted input: a hand-edited manifest drove the guard on one id and the insert
    /// on another.</para>
    ///
    /// <para>Read on the console path, which is the one where the mismatch is unrecoverable — the cabinet is
    /// created at the file's id, every child is re-stamped to the manifest's, and the administrator is never
    /// minted. The cabinet path needs it less (its own clinic already exists) and is covered by the same check
    /// inside the restore, which refuses a <c>Self</c> row that is not the target cabinet.</para>
    /// </summary>
    public static bool CarriesCabinetRecord(ZipArchive zip, Guid clinicId)
    {
        var entry = zip.GetEntry(ClinicArchiveFormat.DataEntry(ClinicArchiveFormat.ClinicEntity));
        if (entry is null)
        {
            return false;
        }

        try
        {
            using var stream = entry.Open();
            var rows = JsonSerializer.Deserialize<List<JsonObject>>(stream, ClinicArchiveFormat.Json);

            return rows is not null
                   && rows.Any(row => row.TryGetPropertyValue("Id", out var id)
                                      && id is not null
                                      && Guid.TryParse(id.GetValue<string>(), out var parsed)
                                      && parsed == clinicId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task WriteTextEntryAsync(
        ZipArchive zip, string name, string content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);

        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }
}

/// <summary>A manifest that was accepted, or the coded refusal that stopped the restore before it began.</summary>
public sealed record ClinicArchiveReadResult
{
    private ClinicArchiveReadResult(ClinicArchiveManifest? manifest, string? code, string? error)
    {
        Manifest = manifest;
        Code = code;
        Error = error;
    }

    public ClinicArchiveManifest? Manifest { get; }

    public string? Code { get; }

    public string? Error { get; }

    public bool IsRefused => Code is not null;

    public static ClinicArchiveReadResult Accepted(ClinicArchiveManifest manifest) => new(manifest, null, null);

    public static ClinicArchiveReadResult Refused(string code, string error) => new(null, code, error);
}
