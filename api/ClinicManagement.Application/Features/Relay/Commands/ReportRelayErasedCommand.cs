using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// A retired PC de secours reports that « Effacer la copie » is done (<c>clinic-pc-copy</c> AC-8.2, AC-8.5): one row in
/// « Journal d'activité », and the cloud's card says « Copie effacée le … ».
///
/// <para>⚠️ <b>Authenticated by the PC's own secret, and accepted from a RETIRED PC</b> — which is the only kind that can
/// send it: the token exchange refuses a retired PC by design, so this door cannot go through it. It does nothing but
/// record a date and a journal row, and only for the PC whose secret is presented.</para>
/// </summary>
public sealed record ReportRelayErasedCommand(Guid RelayId, string Secret) : IRequest<Result>;

public sealed class ReportRelayErasedCommandHandler : IRequestHandler<ReportRelayErasedCommand, Result>
{
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReportRelayErasedCommandHandler> _logger;

    public ReportRelayErasedCommandHandler(
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ReportRelayErasedCommandHandler> logger)
    {
        _relays = relays;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ReportRelayErasedCommand request, CancellationToken cancellationToken)
    {
        var relay = await _relays.GetByIdAcrossClinicsAsync(request.RelayId, cancellationToken);
        if (relay is null || string.IsNullOrEmpty(request.Secret) || !relay.SecretMatches(request.Secret))
        {
            return Result.Failure(RelayRefusals.UnknownRelay, RelayRefusals.UnknownRelayCode);
        }

        if (relay.Status != ClinicRelayStatus.Retired)
        {
            return Result.Failure(RelayRefusals.NotRetired, RelayRefusals.NotRetiredCode);
        }

        try
        {
            var now = DateTime.UtcNow;
            if (relay.MarkErased(now))
            {
                // The PC is the actor: an admin pressed the button on it, but that account no longer exists anywhere
                // the cloud can see once the copy is gone.
                await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay,
                    AuditAction.Update, RelayJournal.Erased, now, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Recording the erasure of PC de secours {RelayId} failed", request.RelayId);
            return Result.Failure("L'effacement n'a pas pu être enregistré sur le cloud. Il sera renvoyé.");
        }
    }
}
