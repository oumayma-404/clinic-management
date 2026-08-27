using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.PushDevices.Commands;

/// <summary>
/// Registers, refreshes or <b>rebinds</b> one device token to the caller — deliberately one command for all
/// three, because the shell cannot tell them apart and should not have to. It sends the token it was given at
/// every sign-in and whenever the OS rotates one (AC-40); which of the three writes that turns out to be is a
/// fact about our table, not about the client.
/// </summary>
public class RegisterPushDeviceCommand : IRequest<Result<PushDeviceDto>>
{
    public DevicePlatform Platform { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? ShellVersion { get; set; }
}

public class RegisterPushDeviceCommandHandler
    : IRequestHandler<RegisterPushDeviceCommand, Result<PushDeviceDto>>
{
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IDeviceRegistrationRepository _devices;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOsPushAvailability _availability;
    private readonly ILogger<RegisterPushDeviceCommandHandler> _logger;

    public RegisterPushDeviceCommandHandler(
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IDeviceRegistrationRepository devices,
        IUnitOfWork unitOfWork,
        IOsPushAvailability availability,
        ILogger<RegisterPushDeviceCommandHandler> logger)
    {
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _devices = devices;
        _unitOfWork = unitOfWork;
        _availability = availability;
        _logger = logger;
    }

    public async Task<Result<PushDeviceDto>> Handle(
        RegisterPushDeviceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinicResult = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinicResult.IsFailure)
            {
                return Result<PushDeviceDto>.Failure(clinicResult.Error ?? "Cabinet introuvable.");
            }

            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Result<PushDeviceDto>.Failure("Utilisateur introuvable.");
            }

            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Result<PushDeviceDto>.Failure("Le jeton de l'appareil est obligatoire.");
            }

            if (!Enum.IsDefined(request.Platform))
            {
                return Result<PushDeviceDto>.Failure("Plateforme inconnue.");
            }

            // AC-42 — refuse rather than queue. A registration that lands in a queue nothing can drain is worse
            // than a refusal: the shell believes it is subscribed, the operator sees rows accumulating, and the
            // dentist's only symptom is that notifications never arrive.
            if (!_availability.SupportsPush(request.Platform))
            {
                return Result<PushDeviceDto>.Failure(
                    _availability.UnavailableReason(request.Platform)
                    ?? "Les notifications système ne sont pas disponibles sur cette installation.");
            }

            var token = request.Token.Trim();
            var clinicId = clinicResult.Value;
            var now = DateTime.UtcNow;

            // Cross-clinic on purpose — the token is globally unique, so a clinic-scoped lookup would miss a row
            // and turn this into a unique-index violation. See the repository's own note.
            var existing = await _devices.GetByTokenAcrossClinicsAsync(token, cancellationToken);

            if (existing == null)
            {
                var created = DeviceRegistration.Create(
                    clinicId, userId, request.Platform, token, request.ShellVersion, now);
                await _devices.AddAsync(created, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                return Result<PushDeviceDto>.Success(ToDto(created, reboundFromAnotherUser: false));
            }

            var isSameBinding = existing.ClinicId == clinicId
                                && string.Equals(existing.UserId, userId, StringComparison.Ordinal);

            if (isSameBinding)
            {
                existing.Refresh(request.Platform, request.ShellVersion, now);
            }
            else
            {
                // AC-41 — never a 409. A shared reception tablet hands the same token to whoever signs in, and
                // refusing would mean the colleague who left keeps receiving notifications on a device somebody
                // else is holding. One row, so the previous binding is gone rather than outranked.
                _logger.LogInformation(
                    "Rebinding a push device registration to another user in clinic {ClinicId}", clinicId);
                existing.RebindTo(clinicId, userId, request.Platform, request.ShellVersion, now);
            }

            await _devices.UpdateAsync(existing, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<PushDeviceDto>.Success(ToDto(existing, reboundFromAnotherUser: !isSameBinding));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Failed to register a push device");
            return Result<PushDeviceDto>.Failure("Impossible d'enregistrer cet appareil.");
        }
    }

    private static PushDeviceDto ToDto(DeviceRegistration registration, bool reboundFromAnotherUser) => new()
    {
        Id = registration.Id,
        Platform = registration.Platform,
        ShellVersion = registration.ShellVersion,
        LastSeenAt = registration.LastSeenAt,
        ReboundFromAnotherUser = reboundFromAnotherUser
    };
}
