using System.IO.Compression;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Backup.Commands;

/// <summary>
/// Puts a cabinet's own archive back: missing records are re-inserted with their original ids, records still
/// present are left untouched, and the result is reported per entity
/// (<c>clinic-data-archive-and-restore</c>).
///
/// <para><b>Additive, and that is the whole safety argument.</b> Nothing is updated and nothing is deleted, so
/// « j'ai perdu une semaine » and « j'ai tout perdu » are the same operation — total loss is the case where every
/// row is a gap — and a restore run twice by a nervous owner does nothing the second time (AC-2).</para>
///
/// <para>⚠️ <b>An archive belonging to another cabinet is refused</b> (AC-6), by comparing the manifest's clinic id
/// against the caller's own. It is not a theoretical mix-up: a practice with two installations, or an owner
/// helping a colleague, has two files in one Downloads folder with names that differ by a date.</para>
/// </summary>
public class RestoreClinicArchiveCommand : IRequest<Result<ClinicArchiveRestoreReport>>
{
    /// <summary>The uploaded <c>.zip</c>. Read into memory by the handler — a zip needs to seek.</summary>
    public Stream? Archive { get; set; }
}

public class RestoreClinicArchiveCommandHandler
    : IRequestHandler<RestoreClinicArchiveCommand, Result<ClinicArchiveRestoreReport>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly ILogger<RestoreClinicArchiveCommandHandler> _logger;

    public RestoreClinicArchiveCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        IAuditEntryRepository auditEntries,
        ILogger<RestoreClinicArchiveCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _store = store;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _auditEntries = auditEntries;
        _logger = logger;
    }

    public async Task<Result<ClinicArchiveRestoreReport>> Handle(
        RestoreClinicArchiveCommand request, CancellationToken cancellationToken)
    {
        if (request.Archive == null)
        {
            return Result<ClinicArchiveRestoreReport>.Failure(
                "Aucun fichier n'a été envoyé.", ClinicArchiveFormat.InvalidCode);
        }

        var callerId = _clinicContext.GetUserId();
        if (string.IsNullOrEmpty(callerId))
        {
            return Result<ClinicArchiveRestoreReport>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        var caller = await _userRepository.GetByAuth0SubAsync(callerId, cancellationToken);
        if (caller == null)
        {
            return Result<ClinicArchiveRestoreReport>.Failure("Utilisateur introuvable.");
        }

        if (!caller.IsAdmin())
        {
            return Result<ClinicArchiveRestoreReport>.Failure(
                "Seuls les administrateurs peuvent restaurer une archive.");
        }

        using var buffer = await ClinicArchiveRestorer.BufferAsync(request.Archive, cancellationToken);

        ZipArchive zip;

        try
        {
            zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            // Not a zip at all — a truncated download, or the wrong file picked. Named as such, because
            // « échec de la restauration » would send an owner looking for a fault in their data.
            return Result<ClinicArchiveRestoreReport>.Failure(
                "Ce fichier n'est pas une archive lisible. Vérifiez que le téléchargement s'est terminé.",
                ClinicArchiveFormat.InvalidCode);
        }

        using (zip)
        {
            var read = ClinicArchivePackager.ReadManifest(zip);
            if (read.IsRefused)
            {
                return Result<ClinicArchiveRestoreReport>.Failure(read.Error!, read.Code!);
            }

            var manifest = read.Manifest!;

            // AC-6 — named on both sides, because « archive incompatible » leaves the reader unable to tell a
            // corrupted file from someone else's cabinet, and only one of those has an obvious next step.
            if (manifest.ClinicId != caller.ClinicId)
            {
                return Result<ClinicArchiveRestoreReport>.Failure(
                    $"Cette archive appartient au cabinet « {manifest.ClinicName} », pas au vôtre. "
                    + "Aucune donnée n'a été modifiée.",
                    ClinicArchiveFormat.ClinicMismatchCode);
            }

            // ⚠️ One transaction over every table's save. The restore commits per table so EF can order the
            // inserts within one, and without this a fault at table n left tables 1..n−1 in the practice's
            // database with no per-entity account of what had landed and « Aucune donnée n'a été modifiée » on
            // the refusal that reached them.
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                var applied = await ClinicArchiveRestorer.ApplyAsync(
                    zip, manifest, caller.ClinicId, _store, _fileStorage, _unitOfWork, _auditActor, _auditEntries,
                    _logger, cancellationToken);

                if (applied.IsFailure)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    return applied;
                }

                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                var report = applied.Value!;

                _logger.LogInformation(
                    "Archive restored into clinic {ClinicId}: {Restored} rows re-inserted, {Present} already "
                    + "present, {Conflicts} skipped as different, {Blobs} blobs.",
                    caller.ClinicId, report.TotalRestored, report.TotalAlreadyPresent, report.TotalConflicts,
                    report.BlobsRestored);

                return Result<ClinicArchiveRestoreReport>.Success(report);
            }
            catch (Exception ex) when (ex is not ConflictException)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                _logger.LogError(ex, "Archive restore failed for clinic {ClinicId}.", caller.ClinicId);

                return Result<ClinicArchiveRestoreReport>.Failure(
                    "La restauration a échoué. Aucune donnée n'a été modifiée.", ClinicArchiveFormat.InvalidCode);
            }
        }
    }
}
