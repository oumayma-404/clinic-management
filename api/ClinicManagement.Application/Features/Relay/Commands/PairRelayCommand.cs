using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>The installer on the cabinet PC presents its one-time code and becomes the clinic's PC de secours (D8).</summary>
public sealed record PairRelayCommand(
    string Code,
    string Label,
    string PublicKey,
    string? CertificateFingerprint,
    string? LanAddresses,
    string? Build) : IRequest<Result<RelayPairingDto>>;

public sealed class PairRelayCommandHandler : IRequestHandler<PairRelayCommand, Result<RelayPairingDto>>
{
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRepository _clinics;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRowStore _rows;
    private readonly IRelayKeyValidator _keys;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PairRelayCommandHandler> _logger;

    public PairRelayCommandHandler(
        IClinicRelayRepository relays,
        IClinicRepository clinics,
        IUserRepository users,
        IClinicRelayRowStore rows,
        IRelayKeyValidator keys,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<PairRelayCommandHandler> logger)
    {
        _relays = relays;
        _clinics = clinics;
        _users = users;
        _rows = rows;
        _keys = keys;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayPairingDto>> Handle(PairRelayCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var relay = string.IsNullOrWhiteSpace(request.Code)
                ? null
                : await _relays.FindByPairingCodeAcrossClinicsAsync(ClinicRelay.Hash(request.Code.Trim()), cancellationToken);
            if (relay is null || !relay.CodeMatches(request.Code.Trim(), now))
            {
                return Result<RelayPairingDto>.Failure(RelayRefusals.PairingCodeExpired, RelayRefusals.PairingCodeExpiredCode);
            }

            if (!_keys.IsValidPublicKey(request.PublicKey))
            {
                return Result<RelayPairingDto>.Failure(RelayRefusals.InvalidKey, RelayRefusals.InvalidRequestCode);
            }

            var clinic = await _clinics.GetByIdAsync(relay.ClinicId, cancellationToken);
            var secret = relay.Pair(request.Label, request.PublicKey, request.CertificateFingerprint,
                request.LanAddresses, request.Build, now);

            var issuer = await _users.GetByAuth0SubAsync(relay.CreatedByUserId, cancellationToken);
            await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.CreatedByUserId, issuer?.Email), relay,
                AuditAction.Insert, RelayJournal.Setup, now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Capture opens only once the pairing is saved — a refused save must not leave a clinic logging changes
            // for a PC that does not exist — and before the first snapshot, whose handler opens it again (idempotent).
            await _rows.EnsureCursorAsync(relay.ClinicId, cancellationToken);

            return Result<RelayPairingDto>.Success(
                new RelayPairingDto(relay.Id, relay.ClinicId, clinic?.Name ?? string.Empty, secret));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "PC de secours pairing failed");
            return Result<RelayPairingDto>.Failure("Le jumelage du PC de secours a échoué. Réessayez.");
        }
    }
}

/// <summary>Whether a PC's public key is one the cloud can seal secrets for (implemented beside the envelope).</summary>
public interface IRelayKeyValidator
{
    bool IsValidPublicKey(string? publicKey);
}
