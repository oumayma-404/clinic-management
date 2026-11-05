using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Removes one demo cabinet the way the console deletes a practice (<c>DeleteClinicFromConsoleCommand</c>): rows in
/// one transaction through <see cref="IClinicPurge"/>, then the files — after the commit, since a store cannot roll back.
/// </summary>
public sealed class DemoCabinetPurge
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClinicPurge _purge;
    private readonly IUserRepository _users;
    private readonly IFileStorage _storage;
    private readonly ILogger<DemoCabinetPurge> _logger;

    public DemoCabinetPurge(
        IUnitOfWork unitOfWork, IClinicPurge purge, IUserRepository users, IFileStorage storage, ILogger<DemoCabinetPurge> logger)
    {
        _unitOfWork = unitOfWork;
        _purge = purge;
        _users = users;
        _storage = storage;
        _logger = logger;
    }

    public async Task PurgeAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var addresses = await ClinicDeletionAddresses.FreedByDeletingAsync(_users, clinicId, cancellationToken);

        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await _purge.PurgeAsync(clinicId, addresses, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }

        try
        {
            await _storage.DeleteByClinicAsync(clinicId, cancellationToken);
        }
        catch (Exception ex)
        {
            // The rows are gone, so a blob left behind is wasted bytes, never a wrong record.
            _logger.LogError(ex, "Demo: cabinet {ClinicId} was removed but its files could not be cleared", clinicId);
        }
    }
}
