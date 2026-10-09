using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>
/// The PC de secours is being uninstalled (<c>clinic-pc-copy</c> AC-8.3, AC-8.5): it counts as retiring it, and it is one
/// row in « Journal d'activité ». Sent by the uninstaller's <c>uninstall-relay</c> verb before anything is erased.
///
/// <para>⚠️ <b>Authenticated by the PC's own secret, like <see cref="ReportRelayErasedCommand"/></b>, and accepted from a PC
/// already retired — an admin often retires the PC first and uninstalls it the next day.</para>
/// </summary>
public sealed record ReportRelayUninstalledCommand(Guid RelayId, string Secret) : IRequest<Result>;

public sealed class ReportRelayUninstalledCommandHandler : IRequestHandler<ReportRelayUninstalledCommand, Result>
{
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReportRelayUninstalledCommandHandler> _logger;

    public ReportRelayUninstalledCommandHandler(
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ReportRelayUninstalledCommandHandler> logger)
    {
        _relays = relays;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result> Handle(ReportRelayUninstalledCommand request, CancellationToken cancellationToken)
    {
        var relay = await _relays.GetByIdAcrossClinicsAsync(request.RelayId, cancellationToken);
        if (relay is null || string.IsNullOrEmpty(request.Secret) || !relay.SecretMatches(request.Secret))
        {
            return Result.Failure(RelayRefusals.UnknownRelay, RelayRefusals.UnknownRelayCode);
        }

        try
        {
            var now = DateTime.UtcNow;
            if (relay.MarkUninstalled(now))
            {
                // The PC is the actor: whoever ran the uninstaller signed in to Windows, not to the cabinet.
                await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay,
                    AuditAction.Update, RelayJournal.Uninstalled, now, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result.Success();
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Recording the uninstall of PC de secours {RelayId} failed", request.RelayId);
            return Result.Failure("La désinstallation n'a pas pu être enregistrée sur le cloud.");
        }
    }
}
