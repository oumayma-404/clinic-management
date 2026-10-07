using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// « Déclarer perdu ou volé » (<c>clinic-pc-copy</c> AC-8.4, EC-14): the PC is retired, and every account of the
/// cabinet must choose a new password — and a new authenticator if it had one — because that PC held their password
/// hashes and their sealed authenticator secrets. Admin + authenticator code (the controller's step-up).
///
/// <para>⚠️ <b>Every account, the inactive ones included</b>: a switched-off colleague's hash is on that disk too, and
/// being switched back on later must not revive it. ⚠️ <b>Each account is re-read with its recovery codes</b>: the
/// clinic list does not load them, an unloaded collection is empty, and removing an authenticator over it would
/// leave every recovery code spendable.</para>
///
/// <para>Works on a PC already retired: one put in a cupboard and then stolen is the ordinary case.</para>
/// </summary>
public sealed record DeclareRelayLostCommand : IRequest<Result<RelayStatusDto>>;

public sealed class DeclareRelayLostCommandHandler : IRequestHandler<DeclareRelayLostCommand, Result<RelayStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IClinicRelayRowStore _rows;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _actor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<DeclareRelayLostCommandHandler> _logger;

    public DeclareRelayLostCommandHandler(
        IClinicContext clinicContext,
        IUserRepository users,
        IClinicRelayRepository relays,
        IClinicRelayRowStore rows,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider actor,
        IUnitOfWork unitOfWork,
        ILogger<DeclareRelayLostCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _rows = rows;
        _auditEntries = auditEntries;
        _actor = actor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayStatusDto>> Handle(DeclareRelayLostCommand request, CancellationToken cancellationToken)
    {
        var admin = await RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayStatusDto>.FailureFrom(admin);
        }

        var clinicId = admin.Value!.ClinicId;
        try
        {
            var relay = await _relays.GetLatestForClinicAsync(clinicId, cancellationToken);
            if (relay is null || relay.PairedAtUtc is null)
            {
                // A code nobody used never put anything on a PC: there is nothing to have stolen.
                return Result<RelayStatusDto>.Failure(RelayRefusals.NoRelay, RelayRefusals.NoRelayCode);
            }

            var now = DateTime.UtcNow;
            // The journal row is written before the accounts are touched, so it carries the declaring admin's own
            // identity — the reset below ends that admin's session like everybody else's.
            var actor = _actor.Current;
            relay.DeclareLost(admin.Value.Id, now);

            var members = await _users.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
            var reset = 0;
            foreach (var member in members.Items)
            {
                var account = await _users.GetByAuth0SubAsync(member.Id, cancellationToken);
                if (account is null || account.ClinicId != clinicId)
                {
                    continue;
                }

                account.RequireNewCredentials();
                reset++;
            }

            await RelayJournal.StageAsync(_auditEntries, actor, relay, AuditAction.Update,
                $"{RelayJournal.Lost} ({reset} compte(s) : nouveau mot de passe obligatoire)", now, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _rows.DropCursorAsync(clinicId, cancellationToken);

            return Result<RelayStatusDto>.Success(Queries.GetRelayStatusQueryHandler.ToDto(relay, now));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Declaring the PC de secours of clinic {ClinicId} lost failed", clinicId);
            return Result<RelayStatusDto>.Failure("Le PC n'a pas pu être déclaré perdu ou volé. Réessayez.");
        }
    }
}
