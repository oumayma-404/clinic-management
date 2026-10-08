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
/// The PC's report every few seconds (FR-2). Never refused on version (D10b): the ack says « update needed ».
/// <paramref name="CallerAddress"/> is where the cloud saw it come from — the cabinet's internet line (AC-6.2).
/// </summary>
public sealed record RelayHeartbeatCommand(RelayHeartbeatRequest Report, string? CallerAddress = null)
    : IRequest<Result<RelayHeartbeatAck>>;

public sealed class RelayHeartbeatCommandHandler : IRequestHandler<RelayHeartbeatCommand, Result<RelayHeartbeatAck>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly ITenantScope _tenantScope;
    private readonly IClinicRelayRowStore _rows;
    private readonly IRelayBuildInfo _build;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RelayHeartbeatCommandHandler> _logger;

    public RelayHeartbeatCommandHandler(
        IClinicContext clinicContext,
        IClinicRelayRepository relays,
        ITenantScope tenantScope,
        IClinicRelayRowStore rows,
        IRelayBuildInfo build,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<RelayHeartbeatCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _tenantScope = tenantScope;
        _rows = rows;
        _build = build;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayHeartbeatAck>> Handle(RelayHeartbeatCommand request, CancellationToken cancellationToken)
    {
        var resolved = await RelayPrincipal.ResolveAsync(_clinicContext, _relays, _tenantScope, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result<RelayHeartbeatAck>.FailureFrom(resolved);
        }

        var relay = resolved.Value!;
        try
        {
            var now = DateTime.UtcNow;
            var report = request.Report;
            var highWater = await _rows.HighWaterAsync(relay.ClinicId, cancellationToken);
            var epoch = await _rows.FeedEpochAsync(cancellationToken);

            // D14: what the PC says it holds first, then the state, then the new ack — the order the PC lived them in.
            relay.RecordAckConfirmation(report.ConfirmedAckSeq, report.ConfirmedAckArmed);

            // D18 phase 2: the PC stopped holding after the cloud applied its handback — the cloud takes the saves back.
            // Never on a heartbeat that says it holds: that is a new cut, which its own return will release.
            if (!report.Holding && report.ReturnedHandbackId is { } returned && relay.ConfirmReturn(returned, now))
            {
                await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay,
                    AuditAction.Update, RelayJournal.Returned, now, cancellationToken);
            }

            var beat = new RelayHeartbeat(
                report.AppliedSeq, report.SeedPercent, report.SeedComplete, report.FilesTotal, report.FilesCopied,
                report.DiskFreeBytes, report.IsUpdating, report.Build, report.PcClockUtc, report.LanAddresses,
                report.MismatchTables, report.LastError, report.CertificateFingerprint, report.CopyStopped,
                report.Holding, report.HoldingSinceUtc, report.HoldingUnderAckSeq,
                report.HttpsPort, report.GatewayAddress, request.CallerAddress, report.ReturnStuckSinceUtc);

            // D19: a takeover an admin's « Reprendre la main » overruled — this answer stops that PC, and never arms it.
            var overruled = relay.IsOverruledHolding(beat);
            var seededNow = relay.RecordHeartbeat(beat, highWater, now);

            if (seededNow)
            {
                await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay,
                    AuditAction.Update, RelayJournal.FirstCopy, now, cancellationToken);
            }

            var cloudBuild = _build.Current;
            var sameBuild = string.Equals(report.Build, cloudBuild, StringComparison.Ordinal);
            var armed = !overruled && ClinicWriteLease.ShouldArm(relay, sameBuild, report.WantsToStandDown, now);
            // Saved with the heartbeat, before the answer leaves: an ack the cloud did not record could arm a PC the
            // cloud would then never fence for.
            var ackSeq = relay.IssueAck(armed, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<RelayHeartbeatAck>.Success(new RelayHeartbeatAck(
                now, highWater, epoch, Retired: false, UpdateNeeded: !sameBuild, cloudBuild, ackSeq, armed, overruled,
                ReturnReleased: relay.HasReleasedReturn(report.ReturnedHandbackId)));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Heartbeat from PC de secours {RelayId} failed", relay.Id);
            return Result<RelayHeartbeatAck>.Failure("Le cloud n'a pas pu enregistrer l'état du PC de secours.");
        }
    }
}
