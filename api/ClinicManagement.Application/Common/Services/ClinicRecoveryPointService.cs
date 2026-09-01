using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Backup.Archive;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.Services;

/// <inheritdoc cref="IClinicRecoveryPointService"/>
public class ClinicRecoveryPointService : IClinicRecoveryPointService
{
    private readonly IClinicRepository _clinics;
    private readonly IClinicRecoveryPointRepository _points;
    private readonly IClinicArchiveStore _store;
    private readonly IFileStorage _fileStorage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ClinicRecoveryPointService> _logger;

    public ClinicRecoveryPointService(
        IClinicRepository clinics,
        IClinicRecoveryPointRepository points,
        IClinicArchiveStore store,
        IFileStorage fileStorage,
        IUnitOfWork unitOfWork,
        ILogger<ClinicRecoveryPointService> logger)
    {
        _clinics = clinics;
        _points = points;
        _store = store;
        _fileStorage = fileStorage;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<bool> TryTakeAsync(Guid clinicId, CancellationToken cancellationToken = default)
    {
        try
        {
            var clinic = await _clinics.GetByIdAsync(clinicId, cancellationToken);

            if (clinic is null)
            {
                _logger.LogWarning(
                    "Cannot take a recovery point for clinic {ClinicId}: the clinic was not found.", clinicId);
                return false;
            }

            // The writer records the attempt whatever happens and does not throw; `IsRestorable` is its verdict.
            var point = await ClinicRecoveryPointWriter.TakeAsync(
                clinic, _points, _store, _fileStorage, _unitOfWork, _logger, cancellationToken);

            return point.IsRestorable;
        }
        catch (Exception ex)
        {
            // The writer swallows its own build failures, so reaching here means something outside it went wrong
            // — the clinic read, or a save. Still `false` rather than a throw: the caller's decision is « do I
            // proceed without a net? », and an exception would turn that into « une erreur est survenue » on an
            // operation that has not started.
            _logger.LogError(ex, "Failed to take a recovery point for clinic {ClinicId}", clinicId);
            return false;
        }
    }
}
