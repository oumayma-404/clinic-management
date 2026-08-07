using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.PushDevices.Commands;

/// <summary>
/// Deregisters a device on sign-out (AC-40) — delivery to it stops.
///
/// <para>Deactivates rather than deletes: the row keeps its token, so a later sign-in on the same device
/// reactivates one row instead of creating a second for the same physical phone, and « why did this device stop
/// receiving? » stays answerable.</para>
///
/// <para><b>Idempotent, and scoped to the caller's own clinic.</b> A token this clinic does not hold reads as
/// success, not as a 404: sign-out is a best-effort courtesy the shell fires while the session is already being
/// torn down, so a refusal it cannot act on would only produce a French error on the way out of the app. Note the
/// asymmetry with registration — that one looks across clinics because the write must not collide; this one must
/// not let a caller deactivate another clinic's device (AC-53).</para>
/// </summary>
public class DeletePushDeviceCommand : IRequest<Result>
{
    public string Token { get; set; } = string.Empty;
}

public class DeletePushDeviceCommandHandler : IRequestHandler<DeletePushDeviceCommand, Result>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IDeviceRegistrationRepository _devices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeletePushDeviceCommandHandler> _logger;

    public DeletePushDeviceCommandHandler(
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IDeviceRegistrationRepository devices,
        IUnitOfWork unitOfWork,
        ILogger<DeletePushDeviceCommandHandler> logger)
    {
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _devices = devices;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(DeletePushDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Result.Failure("Le jeton de l'appareil est obligatoire.");
            }

            var userId = _clinicContext.GetUserId();
            var registration = await _devices.GetByTokenAcrossClinicsAsync(request.Token.Trim(), cancellationToken);

            // Another clinic's row, or none at all: nothing to do, and nothing to disclose either.
            if (registration == null || registration.ClinicId != clinicResult.Value)
            {
                return Result.Success();
            }

            // Only the account the token is currently bound to may retire it. A colleague who has since signed in
            // on the same shared tablet owns that registration now, and the previous user's delayed sign-out call
            // must not silently unsubscribe them.
            if (!string.Equals(registration.UserId, userId, StringComparison.Ordinal))
            {
                return Result.Success();
            }

            if (registration.Deactivate(DateTime.UtcNow))
            {
                await _devices.UpdateAsync(registration, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to deregister a push device");
            return Result.Failure("Impossible de désinscrire cet appareil.");
        }
    }
}
