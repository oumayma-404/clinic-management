using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Relay.Commands;

/// <summary>The PC's report every few seconds (FR-2). Never refused on version (D10b): the ack says « update needed ».</summary>
public sealed record RelayHeartbeatCommand(RelayHeartbeatRequest Report) : IRequest<Result<RelayHeartbeatAck>>;

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

            var seededNow = relay.RecordHeartbeat(new RelayHeartbeat(
                report.AppliedSeq, report.SeedPercent, report.SeedComplete, report.FilesTotal, report.FilesCopied,
                report.DiskFreeBytes, report.IsUpdating, report.Build, report.PcClockUtc, report.LanAddresses,
                report.MismatchTables, report.LastError, report.CertificateFingerprint), highWater, now);

            if (seededNow)
            {
                await RelayJournal.StageAsync(_auditEntries, new AuditActor(relay.Subject, null), relay,
                    AuditAction.Update, RelayJournal.FirstCopy, now, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var cloudBuild = _build.Current;
            return Result<RelayHeartbeatAck>.Success(new RelayHeartbeatAck(
                now, highWater, epoch, Retired: false,
                UpdateNeeded: !string.Equals(report.Build, cloudBuild, StringComparison.Ordinal), cloudBuild));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "Heartbeat from PC de secours {RelayId} failed", relay.Id);
            return Result<RelayHeartbeatAck>.Failure("Le cloud n'a pas pu enregistrer l'état du PC de secours.");
        }
    }
}
