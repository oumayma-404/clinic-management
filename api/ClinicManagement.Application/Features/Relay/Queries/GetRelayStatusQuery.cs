using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Relay.Queries;

/// <summary>« Paramètres → PC de secours » (AC-2.1). A failed read is a failure, never « aucun PC de secours » (AC-2.4).</summary>
public sealed record GetRelayStatusQuery : IRequest<Result<RelayStatusDto>>;

public sealed class GetRelayStatusQueryHandler : IRequestHandler<GetRelayStatusQuery, Result<RelayStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRelayRepository _relays;
    private readonly IRelayReviewItemRepository? _reviews;

    public GetRelayStatusQueryHandler(
        IClinicContext clinicContext, IUserRepository users, IClinicRelayRepository relays,
        IRelayReviewItemRepository? reviews = null)
    {
        _clinicContext = clinicContext;
        _users = users;
        _relays = relays;
        _reviews = reviews;
    }

    public async Task<Result<RelayStatusDto>> Handle(GetRelayStatusQuery request, CancellationToken cancellationToken)
    {
        var admin = await Commands.RelayAdmin.ResolveAsync(_clinicContext, _users, cancellationToken);
        if (admin.IsFailure)
        {
            return Result<RelayStatusDto>.FailureFrom(admin);
        }

        var relay = await _relays.GetLatestForClinicAsync(admin.Value!.ClinicId, cancellationToken);
        var status = ToDto(relay, DateTime.UtcNow);
        if (_reviews is not null)
        {
            status = status with
            {
                ReviewPending = await _reviews.CountPendingAsync(admin.Value!.ClinicId, reEnter: false, cancellationToken),
                ReEnterPending = await _reviews.CountPendingAsync(admin.Value.ClinicId, reEnter: true, cancellationToken),
            };
        }

        // The offer's room check (AC-1.8) needs the figure before « Oui », so only when a setup may start.
        return Result<RelayStatusDto>.Success(status.CanInstall
            ? status with
            {
                NeedBytes = RelayFootprint.NeedBytes(
                    await _relays.GetHostedFileBytesAsync(admin.Value.ClinicId, cancellationToken)),
            }
            : status);
    }

    public static RelayStatusDto ToDto(ClinicRelay? relay, DateTime nowUtc)
    {
        var reading = ClinicRelayHealth.Read(relay, nowUtc);
        var lockedSince = ClinicWriteLease.LockedSinceUtc(relay, nowUtc);
        return new RelayStatusDto(
            reading.State != ClinicRelayState.None,
            relay?.Id,
            relay?.Label,
            RelayLabels.Key(reading.State),
            RelayLabels.Sentence(reading, relay, nowUtc),
            reading.IsProblem,
            reading.Since,
            reading.SeedPercent,
            relay?.LanAddressList ?? Array.Empty<string>(),
            relay?.CertificateFingerprint,
            relay?.PairedAtUtc,
            relay?.SeededAtUtc,
            relay?.RetiredAtUtc,
            relay?.FilesTotal ?? 0,
            relay?.FilesCopied ?? 0,
            relay?.DiskFreeBytes,
            relay?.RetiredReason == ClinicRelayRetirement.LostOrStolen,
            // The issuance's own test (AC-1.10): retired, expired-unused and abandoned rows leave the place free.
            CanInstall: relay is null || !relay.OccupiesTheClinic(nowUtc),
            CloudLocked: lockedSince is not null,
            LockedSinceUtc: lockedSince,
            PcHolding: lockedSince is not null && relay!.PcHoldingSinceUtc is not null,
            ReclaimWarning: lockedSince is { } since ? RelayLabels.ReclaimWarning(relay!.Label, since, nowUtc) : null,
            LockSentence: lockedSince is { } at
                ? RelayLabels.Lock(relay!.PcHoldingSinceUtc is not null, at, nowUtc)
                : null,
            ReclaimSentence: RelayLabels.Reclaimed(relay, nowUtc));
    }
}
