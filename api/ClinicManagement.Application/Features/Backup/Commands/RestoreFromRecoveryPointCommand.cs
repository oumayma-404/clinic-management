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
/// Restores the cabinet from one of its own retained recovery points — « restaurer depuis le point du 12/08 »
/// (<c>clinic-recovery-points</c>).
///
/// <para><b>The third caller of <see cref="ClinicArchiveRestorer.ApplyAsync"/>, and it adds no restore semantics of
/// its own.</b> The upload door and the vendor console door already share that implementation; a second definition of
/// « missing rows come back, present rows are left alone, different rows are skipped » is the one thing this must not
/// introduce. What is genuinely new is only *where the bytes come from*: a storage key this cabinet already owns
/// rather than a file somebody uploaded.</para>
///
/// <para>⚠️ <b>Why the mismatch check still runs.</b> The point was written for this cabinet, so the manifest's clinic
/// id « cannot » differ — and it is compared anyway, because the alternative is trusting that no future code path,
/// no restored row and no mis-set storage key can put another practice's archive at that key. A guarantee that costs
/// one comparison is not worth reasoning about.</para>
///
/// <para>⚠️ <b>A rows-only point restores no files, and the report says so</b> rather than reporting « 0 fichier » and
/// leaving an owner to conclude the radiographs are gone. That is what <c>ClinicArchiveManifest.Contents</c> is for.
/// </para>
/// </summary>
public class RestoreFromRecoveryPointCommand : IRequest<Result<ClinicArchiveRestoreReport>>
{
    public Guid RecoveryPointId { get; set; }
}

public class RestoreFromRecoveryPointCommandHandler
    : IRequestHandler<RestoreFromRecoveryPointCommand, Result<ClinicArchiveRestoreReport>>
{
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRecoveryPointRepository _points;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditActorProvider _auditActor;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly ILogger<RestoreFromRecoveryPointCommandHandler> _logger;

    public RestoreFromRecoveryPointCommandHandler(
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IClinicRecoveryPointRepository points,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        IAuditActorProvider auditActor,
        IAuditEntryRepository auditEntries,
        ILogger<RestoreFromRecoveryPointCommandHandler> logger)
    {
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _points = points;
        _store = store;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _auditActor = auditActor;
        _auditEntries = auditEntries;
        _logger = logger;
    }

    public async Task<Result<ClinicArchiveRestoreReport>> Handle(
        RestoreFromRecoveryPointCommand request, CancellationToken cancellationToken)
    {
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
                "Seuls les administrateurs peuvent restaurer un point de restauration.");
        }

        // Tenant-checked at the read, not afterwards: this resolves a storage key that is about to be read and
        // applied to a practice's records.
        var point = await _points.GetByIdAsync(caller.ClinicId, request.RecoveryPointId, cancellationToken);
        if (point == null)
        {
            return Result<ClinicArchiveRestoreReport>.Failure(
                "Ce point de restauration est introuvable.", ClinicArchiveFormat.InvalidCode);
        }

        // ⚠️ One refusal for « failed » and for « crashed while running », because they are the same fact to the
        // reader: there is nothing behind this row to restore from. The list already shows which it was.
        if (!point.IsRestorable)
        {
            return Result<ClinicArchiveRestoreReport>.Failure(
                "Ce point de restauration n'a pas abouti : il n'y a rien à remettre en place. "
                + "Choisissez-en un autre dans la liste.",
                ClinicArchiveFormat.InvalidCode);
        }

        Stream source;

        try
        {
            source = await _fileStorage.DownloadAsync(point.StorageKey!, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The row survived and its object did not — retention pruned it, or the object store lost it. Named as
            // such, because « la restauration a échoué » would send an owner looking for a fault in their data.
            _logger.LogError(
                ex, "Recovery point {PointId} for clinic {ClinicId} names a storage key that could not be read.",
                point.Id, caller.ClinicId);

            return Result<ClinicArchiveRestoreReport>.Failure(
                "Le fichier de ce point de restauration est introuvable sur le serveur. Aucune donnée n'a été "
                + "modifiée. Utilisez une archive téléchargée sur votre poste, ou contactez votre hébergeur.",
                ClinicArchiveFormat.InvalidCode);
        }

        await using (source)
        {
            using var buffer = await ClinicArchiveRestorer.BufferAsync(source, cancellationToken);

            ZipArchive zip;

            try
            {
                zip = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: true);
            }
            catch (InvalidDataException)
            {
                return Result<ClinicArchiveRestoreReport>.Failure(
                    "Le fichier de ce point de restauration n'est pas lisible. Aucune donnée n'a été modifiée.",
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

                // Compared even though this point was written for this cabinet — see the class note.
                if (manifest.ClinicId != caller.ClinicId)
                {
                    return Result<ClinicArchiveRestoreReport>.Failure(
                        "Ce point de restauration n'appartient pas à votre cabinet. Aucune donnée n'a été modifiée.",
                        ClinicArchiveFormat.ClinicMismatchCode);
                }

                await _unitOfWork.BeginTransactionAsync(cancellationToken);

                try
                {
                    var applied = await ClinicArchiveRestorer.ApplyAsync(
                        zip, manifest, caller.ClinicId, _store, _fileStorage, _unitOfWork, _auditActor,
                        _auditEntries, _logger, cancellationToken);

                    if (applied.IsFailure)
                    {
                        await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                        return applied;
                    }

                    await _unitOfWork.CommitTransactionAsync(cancellationToken);

                    var report = applied.Value!;

                    _logger.LogInformation(
                        "Recovery point {PointId} restored into clinic {ClinicId}: {Restored} rows re-inserted, "
                        + "{Present} already present, {Conflicts} skipped as different.",
                        point.Id, caller.ClinicId, report.TotalRestored, report.TotalAlreadyPresent,
                        report.TotalConflicts);

                    return Result<ClinicArchiveRestoreReport>.Success(report);
                }
                catch (Exception ex) when (ex is not ConflictException)
                {
                    await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                    _logger.LogError(
                        ex, "Restore from recovery point {PointId} failed for clinic {ClinicId}.",
                        point.Id, caller.ClinicId);

                    return Result<ClinicArchiveRestoreReport>.Failure(
                        "La restauration a échoué. Aucune donnée n'a été modifiée.",
                        ClinicArchiveFormat.InvalidCode);
                }
            }
        }
    }
}
