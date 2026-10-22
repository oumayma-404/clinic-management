using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// « Reprendre la main » (<c>clinic-pc-copy</c> US-7, D19, AC-7.1, AC-7.2): the cloud takes a locked cabinet's saves back
/// from its PC de secours — the PC died during a cut, or is out of reach for longer than the cabinet can wait. Admin +
/// authenticator code (the controller's step-up), after AC-7.1's warning: what the cabinet recorded on the PC since the
/// lock will not come back by itself, and that PC's note numbers will be duplicates.
///
/// <para>Refused with <c>relay_not_holding</c> when the cloud is not locked — there is nothing to take back, and an admin
/// pressing it on a healthy cabinet would only disarm a PC that was ready to help.</para>
/// </summary>
public sealed record ReclaimRelayCommand : IRequest<Result<RelayStatusDto>>;

public sealed class ReclaimRelayCommandHandler : IRequestHandler<ReclaimRelayCommand, Result<RelayStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IAuditActorProvider _actor;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReclaimRelayCommandHandler> _logger;

    public ReclaimRelayCommandHandler(
        IClinicContext clinicContext,
        IUserRepository users,
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IAuditActorProvider actor,
        IUnitOfWork unitOfWork,
        ILogger<ReclaimRelayCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _auditEntries = auditEntries;
        _actor = actor;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayStatusDto>> Handle(ReclaimRelayCommand request, CancellationToken cancellationToken)
    {
        var admin = await RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayStatusDto>.FailureFrom(admin);
        }

        var clinicId = admin.Value!.ClinicId;
        try
        {
            var relay = await _relays.GetCurrentForClinicAsync(clinicId, cancellationToken);
            var now = DateTime.UtcNow;
            if (ClinicWriteLease.LockedSinceUtc(relay, now) is not { } lockedSince)
            {
                return Result<RelayStatusDto>.Failure(RelayRefusals.NotHolding, RelayRefusals.NotHoldingCode);
            }

            await RelayTakeBack.StageAsync(relay!, lockedSince, admin.Value.Id, _actor.Current, _auditEntries, now,
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RelayStatusDto>.Success(Queries.GetRelayStatusQueryHandler.ToDto(relay, now));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Taking the cloud back from the PC de secours of clinic {ClinicId} failed", clinicId);
            return Result<RelayStatusDto>.Failure("Le cloud n'a pas pu reprendre la main. Réessayez.");
        }
    }
}

/// <summary>
/// « Reprendre la main », staged: the reclaim and its journal row. Also run by « Retirer ce PC » and « Déclarer perdu ou
/// volé » while the cabinet is locked (AC-8.6, review finding 10) — so the journal says the cut was overruled, and a PC
/// that reconnects before its retirement is read is told « Reclaimed » and keeps the cut's work.
/// </summary>
public static class RelayTakeBack
{
    public static async Task StageIfLockedAsync(
        ClinicRelay relay, string adminId, AuditActor actor, IAuditEntryRepository auditEntries, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (ClinicWriteLease.LockedSinceUtc(relay, nowUtc) is { } lockedSince)
        {
            await StageAsync(relay, lockedSince, adminId, actor, auditEntries, nowUtc, cancellationToken);
        }
    }

    public static async Task StageAsync(
        ClinicRelay relay, DateTime lockedSince, string adminId, AuditActor actor, IAuditEntryRepository auditEntries,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        relay.Reclaim(adminId, nowUtc);

        // AC-7.2: the row names the admin (the actor) and the moment the cabinet's work stopped reaching the cloud.
        await RelayJournal.StageAsync(auditEntries, actor, relay, AuditAction.Update,
            $"{RelayJournal.Reclaimed} (enregistrements refusés depuis {RelayLabels.Moment(lockedSince, nowUtc)})", nowUtc,
            cancellationToken);
    }
}
