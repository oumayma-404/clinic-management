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
/// AC-6.2, EC-17, EC-23: a Windows or Android app of the cabinet says it reaches the cloud (this call) and whether it
/// reached the PC de secours. It counts only during a lock the PC said nothing about, and only from the cabinet's network
/// (its internet line AND the PC's box). Two « PC no » reports at least 30 s apart, with no device reaching the PC during
/// this lock, end it: the cloud takes the cabinet's saves back exactly as « Reprendre la main » does (D19), so a PC that
/// was in fact holding stops and keeps its work when it comes back.
///
/// <para>⚠️ One device reaching the PC blocks the unlock for the whole lock, never just a streak: a tablet on an isolated
/// Wi-Fi interleaving with one that reaches the PC must not unlock a cloud the PC is about to take over. The rarer case
/// — the PC dying after it was reached — is the admin's « Reprendre la main ».</para>
/// </summary>
public sealed record ReportRelayDeviceCommand(bool ReachesPc, IReadOnlyList<string>? Gateways, string? CallerAddress)
    : IRequest<Result<RelayDeviceReportDto>>;

public sealed class ReportRelayDeviceCommandHandler : IRequestHandler<ReportRelayDeviceCommand, Result<RelayDeviceReportDto>>
{
    private static readonly RelayDeviceReportDto NotCounted = new(false, false);

    private readonly IClinicContext _clinicContext;
    private readonly IClinicRelayRepository _relays;
    private readonly IAuditEntryRepository _auditEntries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ReportRelayDeviceCommandHandler> _logger;

    public ReportRelayDeviceCommandHandler(
        IClinicContext clinicContext,
        IClinicRelayRepository relays,
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        ILogger<ReportRelayDeviceCommandHandler> logger)
    {
        _clinicContext = clinicContext;
        _relays = relays;
        _auditEntries = auditEntries;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<Result<RelayDeviceReportDto>> Handle(ReportRelayDeviceCommand request, CancellationToken cancellationToken)
    {
        if (_clinicContext.GetClinicId() is not { } clinicId)
        {
            return Result<RelayDeviceReportDto>.Success(NotCounted);
        }

        try
        {
            var relay = await _relays.GetCurrentForClinicAsync(clinicId, cancellationToken);
            var now = DateTime.UtcNow;
            if (relay is null
                || relay.PcHoldingSinceUtc is not null
                || ClinicWriteLease.LockedSinceUtc(relay, now) is not { } lockedSince
                || !relay.IsOnCabinetNetwork(request.CallerAddress, request.Gateways))
            {
                return Result<RelayDeviceReportDto>.Success(NotCounted);
            }

            var unlocks = relay.RecordDeviceReport(request.ReachesPc, lockedSince, now);
            if (unlocks)
            {
                relay.Reclaim(ClinicRelay.ReclaimedByDevices, now);
                await RelayJournal.StageAsync(_auditEntries, AuditActor.Process(ClinicRelay.ReclaimedByDevices), relay,
                    AuditAction.Update,
                    $"{RelayJournal.ReclaimedByDevices} (enregistrements refusés depuis {RelayLabels.Moment(lockedSince, now)})",
                    now, cancellationToken);
                _logger.LogWarning(
                    "The cabinet's devices reach the cloud but not PC de secours {RelayId}: the cloud takes clinic {ClinicId}'s saves back.",
                    relay.Id, clinicId);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<RelayDeviceReportDto>.Success(new RelayDeviceReportDto(true, unlocks));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            _logger.LogError(ex, "A device report about the PC de secours of clinic {ClinicId} failed", clinicId);
            return Result<RelayDeviceReportDto>.Failure("Le cloud n'a pas pu enregistrer ce signalement.");
        }
    }
}
