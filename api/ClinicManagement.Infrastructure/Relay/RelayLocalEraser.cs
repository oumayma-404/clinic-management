using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
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
    private readonly ILogger<RelayLocalEraser> _logger;

    public RelayLocalEraser(
        IClinicPurge purge,
        IUnitOfWork unitOfWork,
        IFileStorage files,
        IUserRepository users,
        RelayFollowerStateStore state,
        ILogger<RelayLocalEraser> logger)
    {
        _purge = purge;
        _unitOfWork = unitOfWork;
        _files = files;
        _users = users;
        _state = state;
        _logger = logger;
    }

    /// <returns>How many files were removed.</returns>
    public async Task<int> EraseAsync(Guid clinicId, DateTime nowUtc, CancellationToken cancellationToken)
    {
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
        _logger.LogWarning("The PC de secours's copy of clinic {ClinicId} was erased: rows and {Files} file(s)", clinicId, files);
        return files;
    }
}
