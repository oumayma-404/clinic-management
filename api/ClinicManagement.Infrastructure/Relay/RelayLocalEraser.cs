using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Relay;

/// <summary>
/// « Effacer la copie » on a retired PC de secours (<c>clinic-pc-copy</c> AC-8.2): every record and every file of the
/// cabinet leaves this PC, and the cloud is told.
///
/// <para>⚠️ <b>The same purge as the vendor's « supprimer définitivement ce cabinet »</b> (<see cref="IClinicPurge"/>):
/// its plan is derived from the model and it verifies itself, so a table added next year is erased here too and a
/// half-emptied copy throws rather than reading as erased. Rows first, in one transaction; files after the commit,
/// since a disk cannot be rolled back.</para>
///
/// <para>The cloud learns it through the copy loop (<see cref="RelayFollower"/>), which reports it until the cloud
/// answers — a retired PC is often unplugged from the cabinet's network the same day.</para>
/// </summary>
public sealed class RelayLocalEraser
{
    private readonly IClinicPurge _purge;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _files;
    private readonly IUserRepository _users;
    private readonly RelayFollowerStateStore _state;
    private readonly RelayLease _lease;
    private readonly IBackupService _backups;
    private readonly ILogger<RelayLocalEraser> _logger;

    public RelayLocalEraser(
        IClinicPurge purge,
        IUnitOfWork unitOfWork,
        IFileStorage files,
        IUserRepository users,
        RelayFollowerStateStore state,
        RelayLease lease,
        IBackupService backups,
        ILogger<RelayLocalEraser> logger)
    {
        _purge = purge;
        _unitOfWork = unitOfWork;
        _files = files;
        _users = users;
        _state = state;
        _lease = lease;
        _backups = backups;
        _logger = logger;
    }

    /// <summary>Why this copy must stay (AC-7.3, review finding 9) — null when it may be erased.</summary>
    public (string Error, string Code)? Refusal => RelayCopyKeep.Refusal(_state.Load(), _lease.HoldsUnreturnedWork);

    public bool MayErase => Refusal is null;

    /// <returns>How many files were removed.</returns>
    public async Task<int> EraseAsync(Guid clinicId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Refusal is { } refused)
        {
            throw new InvalidOperationException(refused.Error);
        }

        var addresses = await ClinicDeletionAddresses.FreedByDeletingAsync(_users, clinicId, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _purge.PurgeAsync(clinicId, addresses, cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        // Recorded as soon as the rows are gone: from here the copy is no longer a copy, whatever the files do.
        _state.Save(_state.Load() with { ErasedAtUtc = nowUtc, ErasureReported = false });

        var files = await _files.DeleteByClinicAsync(clinicId, cancellationToken);
        var backups = EraseBackups();
        _logger.LogWarning("The PC de secours's copy of clinic {ClinicId} was erased: rows, {Files} file(s) and {Backups} "
                           + "backup folder(s)", clinicId, files, backups);
        return files;
    }

    /// <summary>
    /// Review finding 17: every pre-migration backup on this PC is a full copy of the cabinet (<c>pg_dump</c> + files), so
    /// « Copie effacée » must remove them too. Only <c>clinic-backup-*</c> folders, the backup service's own.
    /// </summary>
    private int EraseBackups()
    {
        var root = _backups.ResolveDestinationRoot(null);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var erased = 0;
        foreach (var folder in Directory.EnumerateDirectories(root, "clinic-backup-*"))
        {
            Directory.Delete(folder, recursive: true);
            erased++;
        }

        return erased;
    }
}
