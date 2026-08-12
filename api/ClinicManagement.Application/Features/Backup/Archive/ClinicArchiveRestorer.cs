using System.IO.Compression;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Audit;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// Applies an archive to a cabinet — the half both doors share
/// (<c>clinic-data-archive-and-restore</c>: the cabinet's own « Restaurer », and the vendor console's
/// re-provisioning path).
///
/// <para><b>One implementation, deliberately.</b> The two doors differ only in what they do <i>before</i> this
/// runs: one checks the archive belongs to the caller's cabinet, the other creates the cabinet at the archive's
/// own id. What « restaurer » means must not have two answers — that is the <c>fixes-dont-propagate</c> shape, and
/// the console path is the seldom-used copy that would rot.</para>
///
/// <para><b>Additive and keyed on the original ids.</b> Nothing is updated and nothing is deleted: a row that is
/// still there is left alone, a row that differs is skipped and counted, and a row that is gone is re-inserted
/// with its own id and its own document number. That is what makes total loss and partial loss the same
/// operation — total loss is the case where every row is a gap — and it is also why money documents are safe: the
/// gapless <c>AAAA-NNNN</c> sequences break only when a new number is minted, and nothing here mints one.</para>
///
/// <para>⚠️ <b>It does not open the transaction it needs, and that is on purpose.</b> The console path has three
/// more writes to make after the rows land — the administrator, the entitlement and the console's own access
/// ledger row — and those have to be in the <i>same</i> transaction or a fault between them leaves a practice's
/// records committed with nobody able to sign in and every retry answered « ce cabinet existe toujours ». So each
/// caller owns the transaction and this reports a failure rather than throwing out of the middle of one.</para>
/// </summary>
public static class ClinicArchiveRestorer
{
    /// <summary>
    /// Restores every table the manifest names, in the manifest's own order, then writes back the blobs the
    /// restored rows point at.
    ///
    /// <para><b>Tables are saved one at a time</b>, and the order is the manifest's because it is the order the
    /// export resolved: a parent before its children, so an invoice line never reaches the database ahead of its
    /// invoice. Rows are detached after their table commits, for the reason
    /// <see cref="IUnitOfWork.StopTracking"/> exists — EF re-scans every tracked entry on each later save, and a
    /// full-cabinet restore is tens of thousands of them.</para>
    ///
    /// <para>⚠️ <b>A failure names the table it stopped on and is a refusal, not an exception.</b> The caller's
    /// transaction is what makes « aucune donnée n'a été modifiée » true — before it, a fault at table <i>n</i>
    /// left tables 1..<i>n</i>−1 committed, threw out of the handler and reached the owner as a generic 500 with
    /// no account of what had landed.</para>
    /// </summary>
    public static async Task<Result<ClinicArchiveRestoreReport>> ApplyAsync(
        ZipArchive zip,
        ClinicArchiveManifest manifest,
        Guid clinicId,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        IAuditEntryRepository auditEntries,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        // AC-9 — declared once, before anything is staged, so every row this scope writes is attributed to a
        // restore rather than reading as one colleague typing three thousand fiches in an afternoon.
        auditActor.RestoringAnArchive();

        var restored = new Dictionary<string, int>(StringComparer.Ordinal);
        var alreadyPresent = new Dictionary<string, int>(StringComparer.Ordinal);
        var conflicts = new Dictionary<string, int>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var blobKeys = new List<string>();

        foreach (var table in manifest.Tables)
        {
            if (!store.CanRestore(table.Entity))
            {
                // A table this build does not know: the archive is newer, or the table was retired. Named rather
                // than skipped in silence — « 4 tables ignorées » is what tells an owner the copy is not complete.
                warnings.Add($"« {Label(table.Entity)} » ne fait pas partie des données que cette version sait restaurer.");
                continue;
            }

            var entry = zip.GetEntry(ClinicArchiveFormat.DataEntry(table.Entity));
            if (entry is null)
            {
                warnings.Add($"« {Label(table.Entity)} » est annoncée par le manifeste mais absente de l'archive.");
                continue;
            }

            ClinicArchiveTableOutcome outcome;

            try
            {
                var json = await ReadAllTextAsync(entry, cancellationToken);
                outcome = await store.RestoreTableAsync(table.Entity, clinicId, json, cancellationToken);

                if (outcome.Restored > 0)
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);

                    // Committed rows are dropped from the change tracker for the reason IUnitOfWork.StopTracking
                    // exists: EF re-scans every tracked entry on each later save, so a full-cabinet restore across
                    // thirty tables would otherwise be quadratic in exactly the case the feature is written for.
                    store.ForgetRestoredRows();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Restore stopped on table {Table} for clinic {ClinicId}.", table.Entity, clinicId);

                return Result<ClinicArchiveRestoreReport>.Failure(
                    $"La restauration s'est arrêtée sur « {Label(table.Entity)} ». Aucune donnée n'a été modifiée.",
                    ClinicArchiveFormat.InvalidCode);
            }

            Accumulate(restored, table.Entity, outcome.Restored);
            Accumulate(alreadyPresent, table.Entity, outcome.AlreadyPresent);
            Accumulate(conflicts, table.Entity, outcome.Conflicts);

            warnings.AddRange(outcome.Notices);
            blobKeys.AddRange(outcome.BlobKeys);

            await RecordAsync(auditEntries, auditActor, clinicId, manifest, table.Entity, outcome, cancellationToken);
        }

        var blobsRestored = await RestoreBlobsAsync(
            zip, blobKeys, clinicId, fileStorage, logger, warnings, cancellationToken);

        return Result<ClinicArchiveRestoreReport>.Success(new ClinicArchiveRestoreReport
        {
            ArchivedAtUtc = manifest.CreatedAtUtc,
            ClinicId = clinicId,
            Restored = restored,
            AlreadyPresent = alreadyPresent,
            Conflicts = conflicts,
            BlobsRestored = blobsRestored,
            // ⚠️ The uploaded manifest's own warnings are deliberately NOT carried across: whoever supplies the
            // archive controls them, and they render on the vendor's console as the server's own diagnostics.
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToList(),
            EntityLabels = restored.Keys
                .Concat(alreadyPresent.Keys)
                .Concat(conflicts.Keys)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(entity => entity, Label, StringComparer.Ordinal),
        });
    }

    /// <summary>
    /// The entity's French name, from the ledger's own map — the standing convention of an English wire key with a
    /// display map beside it, rather than printing <c>PatientMedicalHistory</c> at a cabinet owner.
    /// </summary>
    private static string Label(string entity) => AuditLabels.Entity(entity);

    /// <summary>
    /// One ledger row per table, summarising what this restore did to it.
    ///
    /// <para><b>The interceptor cannot produce these, and the gap is the sensitive half.</b> It writes one row per
    /// mutated <i>aggregate root</i>, which is right for an ordinary edit and wrong here: a restore inserts
    /// children independently of their parents, so re-inserting four thousand <c>Payment</c> rows into invoices
    /// that still exist wrote <b>zero</b> ledger rows — money reappearing in la caisse, the extrait, the dashboard
    /// and every patient balance with nothing in « Journal d'activité » to say where it came from.</para>
    ///
    /// <para>Best-effort would be the wrong contract too: these rows ride the caller's transaction, so they land
    /// exactly when the rows they describe do.</para>
    /// </summary>
    private static async Task RecordAsync(
        IAuditEntryRepository auditEntries,
        IAuditActorProvider auditActor,
        Guid clinicId,
        ClinicArchiveManifest manifest,
        string entity,
        ClinicArchiveTableOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome.Total == 0)
        {
            return;
        }

        var actor = auditActor.Current;

        await auditEntries.AddRangeAsync(new[]
        {
            new AuditEntry(
                clinicId,
                actor.UserId,
                actor.Email,
                entity,
                clinicId.ToString(),
                AuditAction.Insert,
                $"Restauration de l'archive du {manifest.CreatedAtUtc:yyyy-MM-dd} : "
                + $"{outcome.Restored} remis en place, {outcome.AlreadyPresent} déjà présents, "
                + $"{outcome.Conflicts} ignorés",
                DateTime.UtcNow),
        }, cancellationToken);
    }

    /// <summary>
    /// Writes back the blobs the <b>re-inserted</b> rows point at, each at its own storage key (AC-5) and only
    /// when nothing is there already.
    ///
    /// <para>⚠️ <b>Verbatim, never re-composed</b> (EC-4): a key written before <c>multi-tenant-cloud</c> US-5 is
    /// flat, and <c>IFileStorage.DownloadAsync</c> resolves a stored key verbatim by contract — so prefixing on the
    /// way in would put the bytes where the restored row does not look, and the file would read as
    /// « introuvable » on a row that looks perfectly healthy.</para>
    ///
    /// <para>⚠️ <b>Verbatim is not the same as unchecked.</b> A key naming <i>another</i> cabinet's prefix is
    /// refused: <c>RestoreAtKeyAsync</c> is the one door around the US-5 invariant that an unprefixed key is not
    /// something a caller can write, and an archive listing <c>clinics/&lt;victim&gt;/…</c> would otherwise create
    /// objects inside that tenant's prefix in the shared bucket. A key with no <c>clinics/</c> prefix at all is a
    /// genuine pre-US-5 one and is allowed — that is what EC-4 is about.</para>
    ///
    /// <para>⚠️ <b>Existing bytes are left alone</b>, which is the blob half of the row rule above: re-uploading
    /// would overwrite a file the practice has replaced since the archive was taken.</para>
    ///
    /// <para>A blob that will not write is a warning rather than a failure — the row it belongs to is already
    /// back, and losing the whole restore over one unreadable file would be the wrong trade.</para>
    /// </summary>
    private static async Task<int> RestoreBlobsAsync(
        ZipArchive zip,
        IEnumerable<string> storageKeys,
        Guid clinicId,
        IFileStorage fileStorage,
        ILogger logger,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var written = 0;
        var refusedForeign = 0;

        foreach (var storageKey in storageKeys.Distinct(StringComparer.Ordinal))
        {
            if (!BelongsToClinic(storageKey, clinicId))
            {
                refusedForeign++;
                continue;
            }

            var entry = zip.GetEntry(ClinicArchiveFormat.BlobEntry(storageKey));
            if (entry is null)
            {
                continue;
            }

            try
            {
                if (await fileStorage.ExistsAsync(storageKey, cancellationToken))
                {
                    continue;
                }

                await using var source = entry.Open();
                await fileStorage.RestoreAtKeyAsync(
                    source, ContentTypeFor(storageKey), storageKey, cancellationToken);

                written++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Restore: blob {StorageKey} could not be written back.", storageKey);
                warnings.Add($"Le fichier « {storageKey} » n'a pas pu être restauré.");
            }
        }

        if (refusedForeign > 0)
        {
            warnings.Add(
                $"{refusedForeign} fichier(s) de l'archive désignent un autre cabinet et n'ont pas été restaurés.");
        }

        return written;
    }

    /// <summary>
    /// Whether a stored key is one this cabinet may be handed. A <c>clinics/</c>-prefixed key must name this
    /// clinic; anything else is a flat pre-US-5 key, which carries no clinic and belongs to whichever row holds it.
    /// </summary>
    private static bool BelongsToClinic(string storageKey, Guid clinicId) =>
        !storageKey.StartsWith(ClinicStorageKeyPrefix, StringComparison.Ordinal)
        || storageKey.StartsWith($"{ClinicStorageKeyPrefix}{clinicId}/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The folder every key composed since <c>multi-tenant-cloud</c> US-5 starts with. Stated here rather than
    /// referenced from <c>ClinicStorageKey</c> because that type lives in Infrastructure, which this project does
    /// not reference; <c>ClinicStorageKeyTests</c> pins the two against each other.
    /// </summary>
    private const string ClinicStorageKeyPrefix = "clinics/";

    /// <summary>
    /// The content type a restored blob is stored under, from its own extension.
    ///
    /// <para>The archive does not carry one, and it does not need to: the <b>row</b> holds the content type the
    /// application serves the file with (<c>PatientFile.ContentType</c>), so this only labels the object in the
    /// store. It reads the same catalog every upload door reads, so a format the product accepts is not one this
    /// relabels <c>application/octet-stream</c> on the way back — a private four-case switch beside the single
    /// authority on exactly that mapping is the <c>fixes-dont-propagate</c> shape.</para>
    /// </summary>
    private static string ContentTypeFor(string storageKey) =>
        FileTypeCatalog.TryGet(Path.GetExtension(storageKey).TrimStart('.').ToLowerInvariant())?.ContentType
        ?? "application/octet-stream";

    private static void Accumulate(IDictionary<string, int> counts, string entity, int value)
    {
        if (value <= 0)
        {
            return;
        }

        counts[entity] = counts.TryGetValue(entity, out var running) ? running + value : value;
    }

    private static async Task<string> ReadAllTextAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);

        return await reader.ReadToEndAsync(cancellationToken);
    }

    /// <summary>Reads an uploaded archive into memory so the zip can seek — a form file's stream cannot.</summary>
    public static async Task<MemoryStream> BufferAsync(Stream upload, CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        return buffer;
    }
}
