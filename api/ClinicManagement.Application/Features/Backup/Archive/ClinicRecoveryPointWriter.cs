using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Archive;

/// <summary>
/// Takes one recovery point for a cabinet — the rows-only archive, the object, and the row that records it.
///
/// <para><b>Shared rather than written twice.</b> Two callers need it now: the nightly
/// <c>ClinicRecoveryPointJob</c> and « Annuler cet import », which takes one before deleting anything. The
/// sequence has three things that are easy to get subtly wrong — the <c>Running</c> row is committed
/// <b>before</b> the build so a crash leaves a visible row rather than none, the clinic id is a parameter rather
/// than read off the ambient scope (a job uploads under <c>UseSystemWide</c> and would otherwise write an
/// unattributed key), and the buffer is a self-deleting temp file rather than a <c>MemoryStream</c> because
/// <c>ZipArchive</c> seeks back to write each entry's directory record. A second copy of that is the
/// <c>fixes-dont-propagate</c> shape this repository keeps finding.</para>
///
/// <para><b>Static, taking its dependencies as parameters</b>, on <c>VisitClosureReader</c>'s and
/// <c>ClinicArchivePackager</c>'s precedent: it holds no state, and leaving it out of the container keeps each
/// caller's dependency list honest about what it actually touches.</para>
/// </summary>
public static class ClinicRecoveryPointWriter
{
    /// <summary>
    /// Build and store one point. Returns the recorded row — <see cref="ClinicRecoveryPoint.IsRestorable"/> tells
    /// the caller whether it can be leaned on.
    ///
    /// <para>⚠️ <b>It does not throw on failure</b>, and the row it returns is the report: the nightly pass wants
    /// to record the attempt and move to the next cabinet, while the undo wants to refuse. Those are opposite
    /// reactions to the same fact, so the fact is returned rather than the reaction imposed.</para>
    /// </summary>
    public static async Task<ClinicRecoveryPoint> TakeAsync(
        Clinic clinic,
        IClinicRecoveryPointRepository points,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var point = new ClinicRecoveryPoint(
            Guid.NewGuid(), clinic.Id, ClinicArchiveContents.RowsOnly, DateTime.UtcNow);

        // ⚠️ Committed BEFORE the archive is built. A crash mid-build then leaves a visible row instead of no row
        // at all, and « rien cette nuit-là » is the reading that loses a practice its data.
        await points.AddAsync(point, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            // A self-deleting temp file rather than a MemoryStream: `ZipArchive` in Create mode seeks back to
            // write each entry's directory record, so it needs somewhere to seek — but that is an argument for a
            // seekable stream, not for the large-object heap, in a process shared with every other cabinet.
            await using var buffer = new FileStream(
                Path.Combine(Path.GetTempPath(), $"clinic-recovery-{Guid.NewGuid():N}.zip"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            var manifest = await ClinicArchivePackager.WriteAsync(
                buffer, clinic.Id, clinic.Name, store, fileStorage, logger,
                ClinicArchiveContents.RowsOnly);

            var sizeBytes = buffer.Length;
            buffer.Position = 0;

            // The clinic id is a parameter and not the ambient scope — the case US-5 names: a caller uploading
            // with no clinic in scope writes an unattributed key, silently.
            var storageKey = await fileStorage.UploadAsync(
                buffer,
                ClinicArchiveFormat.ContentType,
                clinic.Id,
                $"recovery-points/{ClinicClock.ClinicToday():yyyy-MM-dd}-{point.Id:N}.zip");

            var rowCount = manifest.Tables.Sum(t => t.Rows);

            point.MarkSucceeded(storageKey, sizeBytes, manifest.Tables.Count, rowCount, DateTime.UtcNow);
            await points.UpdateAsync(point, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Recovery point for clinic {ClinicId}: {Tables} tables, {Rows} rows, {Bytes} bytes at {Key}.",
                clinic.Id, manifest.Tables.Count, rowCount, sizeBytes, storageKey);
        }
        catch (Exception ex)
        {
            // An `InvalidOperationException` from the storage layer already carries an operator-facing French
            // sentence; anything else does not, and putting a raw .NET message on a screen a practice reads is
            // how « something went wrong » becomes unactionable.
            var reason = ex is InvalidOperationException
                ? ex.Message
                : $"Échec inattendu du point de restauration ({ex.GetType().Name}).";

            logger.LogError(ex, "Recovery point for clinic {ClinicId} failed.", clinic.Id);

            point.MarkFailed(reason, DateTime.UtcNow);
            await points.UpdateAsync(point, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return point;
    }
}
