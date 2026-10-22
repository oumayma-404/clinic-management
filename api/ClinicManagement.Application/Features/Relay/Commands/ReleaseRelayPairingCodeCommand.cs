using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// The Windows app gives back a code its installer never presented — Windows' prompt refused, too little room, a failed
/// download (AC-1.11): the clinic's one place is free again at once rather than after an hour. Whoever holds the
/// code may release it, so it needs no session (the credentials door has none). Every outcome answers the same: an
/// unknown, used or already-released code tells the caller nothing about any clinic.
/// </summary>
public sealed record ReleaseRelayPairingCodeCommand(string Code) : IRequest<Result>;

public sealed class ReleaseRelayPairingCodeCommandHandler : IRequestHandler<ReleaseRelayPairingCodeCommand, Result>
{
    private readonly IClinicRelayRepository _relays;
    private readonly IUserRepository _users;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReleaseRelayPairingCodeCommandHandler> _logger;

    public ReleaseRelayPairingCodeCommandHandler(
        IClinicRelayRepository relays,
        IUserRepository users,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ReleaseRelayPairingCodeCommandHandler> logger)
    {
        _relays = relays;
        _users = users;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ReleaseRelayPairingCodeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var code = request.Code?.Trim() ?? string.Empty;
            if (code.Length == 0)
            {
                return Result.Success();
            }

            var now = DateTime.UtcNow;
            var relay = await _relays.FindByPairingCodeAcrossClinicsAsync(ClinicRelay.Hash(code), cancellationToken);
            if (relay is null || !relay.CodeMatches(code, now) || !relay.ReleaseUnusedCode(now))
            {
                return Result.Success();
            }

            // The journal names the admin who started the setup: the release is the end of their attempt.
            var issuer = await _users.GetByAuth0SubAsync(relay.CreatedByUserId, cancellationToken);
            await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.CreatedByUserId, issuer?.Email), relay,
                AuditAction.Update, RelayJournal.Abandoned, now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            // The code still lapses on its own within the hour: nothing here is worth a refusal the app could not act on.
            _logger.LogWarning(ex, "PC de secours pairing code could not be released");
            return Result.Success();
        }
    }
}
