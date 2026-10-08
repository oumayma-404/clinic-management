using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>Starts a PC de secours setup (AC-1.4): one code, valid 10 minutes, once (D8). The controller checks the step-up.</summary>
public sealed record IssueRelayPairingCodeCommand(string Label) : IRequest<Result<RelayPairingCodeDto>>;

public sealed class IssueRelayPairingCodeCommandHandler
    : IRequestHandler<IssueRelayPairingCodeCommand, Result<RelayPairingCodeDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _actor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<IssueRelayPairingCodeCommandHandler> _logger;

    public IssueRelayPairingCodeCommandHandler(
        IClinicContext clinicContext,
        IUserRepository users,
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider actor,
        IUnitOfWork unitOfWork,
        ILogger<IssueRelayPairingCodeCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _auditEntries = auditEntries;
        _actor = actor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayPairingCodeDto>> Handle(
        IssueRelayPairingCodeCommand request, CancellationToken cancellationToken)
    {
        var admin = await RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayPairingCodeDto>.FailureFrom(admin);
        }

        return await RelayPairingIssuance.IssueAsync(admin.Value!, request.Label, _actor.Current, _relays,
            _auditEntries, _unitOfWork, _logger, cancellationToken);
    }
}

/// <summary>
/// The one place a setup attempt begins, whichever door it came through: the signed-in admin's offer (AC-1.4) or an
/// admin's email, password and code on any PC (AC-1.5).
/// </summary>
internal static class RelayPairingIssuance
{
    public static async Task<Result<RelayPairingCodeDto>> IssueAsync(
        User admin,
        string label,
        AuditActor actor,
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var current = await relays.GetCurrentForClinicAsync(admin.ClinicId, cancellationToken);
            if (current is not null)
            {
                if (current.OccupiesTheClinic(now))
                {
                    return Result<RelayPairingCodeDto>.Failure(
                        RelayRefusals.AlreadyPaired(current.Label), RelayRefusals.AlreadyPairedCode);
                }

                // An expired, unused code or an abandoned setup frees the clinic's one place (AC-1.11, EC-9).
                current.Retire(ClinicRelayRetirement.Abandoned, admin.Id, now);
                await RelayJournal.StageAsync(auditEntries, actor, current, AuditAction.Update,
                    RelayJournal.Abandoned, now, cancellationToken);

                // ⚠️ Saved on its own: in one SaveChanges EF may send the INSERT before this UPDATE (the index's
                // column does not change), and the « one non-retired PC per clinic » index would refuse it.
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var needBytes = RelayFootprint.NeedBytes(
                await relays.GetHostedFileBytesAsync(admin.ClinicId, cancellationToken));

            var (relay, code) = ClinicRelay.BeginPairing(admin.ClinicId, label, admin.Id, now);
            await relays.AddAsync(relay, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RelayPairingCodeDto>.Success(
                new RelayPairingCodeDto(relay.Id, code, relay.PairingCodeExpiresAtUtc!.Value, needBytes));
        }
        catch (DbUpdateException)
        {
            // Two admins pressed « Oui » on two PCs at once: the filtered unique index refused the second (EC-8).
            var winner = await relays.GetCurrentForClinicAsync(admin.ClinicId, cancellationToken);
            return Result<RelayPairingCodeDto>.Failure(
                RelayRefusals.AlreadyPaired(winner?.Label ?? "un autre PC"), RelayRefusals.AlreadyPairedCode);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            logger.LogError(ex, "Pairing code could not be issued for clinic {ClinicId}", admin.ClinicId);
            return Result<RelayPairingCodeDto>.Failure("L'installation du PC de secours n'a pas pu démarrer. Réessayez.");
        }
    }
}

/// <summary>The caller, re-checked as an administrator from the database (the token's role is not authoritative).</summary>
public static class RelayAdmin
{
    public static async Task<Result<User>> ResolveAsync(
        IClinicContext clinicContext, IUserRepository users, CancellationToken cancellationToken)
    {
        var callerId = clinicContext.GetUserId();
        var user = string.IsNullOrEmpty(callerId) ? null : await users.GetByAuth0SubAsync(callerId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result<User>.Failure("Session invalide, veuillez vous reconnecter.");
        }

        return user.IsAdmin()
            ? Result<User>.Success(user)
            : Result<User>.Failure(RelayRefusals.NotAdmin, RelayRefusals.NotAdminCode);
    }
}
