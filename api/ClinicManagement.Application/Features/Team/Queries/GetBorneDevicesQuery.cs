using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Relay.Commands;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// « Ajouter une borne »: the cabinet's signed-in devices, most recently used first, so an admin picks the reception PC
/// from her own computer instead of walking to it. The demo and the PC de secours' set-up session are not devices
/// anyone works on, so they are left out.
/// </summary>
public class GetBorneDevicesQuery : IRequest<Result<List<BorneDeviceDto>>>
{
}

public class GetBorneDevicesQueryHandler : IRequestHandler<GetBorneDevicesQuery, Result<List<BorneDeviceDto>>>
{
    /// <summary>A cabinet's live sessions are a handful; this bounds a list nobody scrolls.</summary>
    public const int MaxDevices = 30;

    private static readonly HashSet<string> NotDevices = new(StringComparer.Ordinal)
    {
        StartDemoSessionCommandHandler.DeviceLabel,
        IssueRelayPairingCodeWithCredentialsCommandHandler.DeviceLabel,
    };

    private readonly ISessionFamilyRepository _sessions;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetBorneDevicesQueryHandler(
        ISessionFamilyRepository sessions, IUserRepository users, IClinicRepository clinics, IClinicContext clinicContext)
    {
        _sessions = sessions;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<List<BorneDeviceDto>>> Handle(GetBorneDevicesQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await AttendanceRules.ResolveAsync(_clinicContext, _users, _clinics, adminOnly: true, cancellationToken);
            if (resolved.IsFailure)
                return Result<List<BorneDeviceDto>>.FailureFrom(resolved);
            var (_, clinic) = resolved.Value;

            var mine = _clinicContext.GetSessionFamilyId();
            var live = await _sessions.GetLiveForClinicAsync(clinic.Id, DateTime.UtcNow, MaxDevices, cancellationToken);
            return Result<List<BorneDeviceDto>>.Success(live
                .Where(s => s.DeviceLabel is null || !NotDevices.Contains(s.DeviceLabel))
                // A tracked person's phone is refused as a borne, so it is never offered.
                .Where(s => !(s.User.IsAttendanceTracked
                              && AttendanceRules.DeviceKindOf(s.DeviceLabel) == AttendanceRules.DeviceKindPhone))
                // One row per person and device name, the newest (the read is newest first, and grouping keeps that
                // order) — her own session wins its group so « Cet appareil » stays.
                .GroupBy(s => (s.UserId, s.DeviceLabel))
                .Select(g => g.FirstOrDefault(s => s.Id == mine) ?? g.First())
                .Select(s => new BorneDeviceDto
                {
                    SessionId = s.Id,
                    PersonName = StaffLeaveRules.DisplayName(s.User),
                    DeviceLabel = s.DeviceLabel,
                    LastActiveAt = s.LastRotatedAt,
                    Kind = AttendanceRules.DeviceKindOf(s.DeviceLabel),
                    IsThisDevice = s.Id == mine,
                })
                .ToList());
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<BorneDeviceDto>>.Failure("Erreur lors de la lecture des appareils connectés.", ex);
        }
    }
}
