using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// « Pointez votre arrivée » and « Pointer votre départ ? » (<c>pointage-obligatoire</c> AC-1, AC-2, AC-7): her own
/// account only. Asked by every signed-in screen, so it answers « not locked » rather than refusing when the pointage is
/// off or the account is not tracked.
/// </summary>
public class GetMyClockStatusQuery : IRequest<Result<ClockStatusDto>>
{
    /// <summary>Set by the controller from the borne cookie, when this device carries one.</summary>
    public string? Secret { get; set; }

    /// <summary>The instant judged; the wall clock when absent. Tests pin it, so « inside opening hours » never depends on when the suite runs.</summary>
    public DateTime? NowUtc { get; init; }
}

public class GetMyClockStatusQueryHandler : IRequestHandler<GetMyClockStatusQuery, Result<ClockStatusDto>>
{
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IAttendanceEntryRepository _entries;
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly ITenantScope _tenantScope;
    private readonly AttendanceLockEvaluator _lock;

    public GetMyClockStatusQueryHandler(
        IClinicContext clinicContext, IUserRepository users, IClinicRepository clinics, IAttendanceEntryRepository entries,
        IAttendanceTerminalRepository terminals, ITenantScope tenantScope, AttendanceLockEvaluator @lock)
    {
        _clinicContext = clinicContext;
        _users = users;
        _clinics = clinics;
        _entries = entries;
        _terminals = terminals;
        _tenantScope = tenantScope;
        _lock = @lock;
    }

    public async Task<Result<ClockStatusDto>> Handle(GetMyClockStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<ClockStatusDto>.FailureFrom(resolved);
            var me = resolved.Value!;

            // Asked before the early answers: an admin or a doctor's device can be the one chosen as the borne.
            var borneWaiting = _clinicContext.GetSessionFamilyId() is { } sessionId
                ? (await _terminals.FindPendingForSessionAsync(me.ClinicId, sessionId, cancellationToken))?.Name
                : null;

            var switches = await _clinics.GetAttendanceSwitchesAsync(me.ClinicId, cancellationToken);
            if (switches is not { AttendanceEnabled: true } || !me.IsActive || !me.IsAttendanceTracked)
                return Result<ClockStatusDto>.Success(new ClockStatusDto
                {
                    UserId = me.Id, Name = StaffLeaveRules.DisplayName(me), HasSchema = me.HasAttendanceSchema,
                    BorneWaiting = borneWaiting,
                });

            var now = request.NowUtc ?? DateTime.UtcNow;
            var today = AttendanceRules.Today(now);
            var lines = await _entries.ListBetweenAsync(me.ClinicId, today, today, me.Id, cancellationToken);
            var open = lines.LastOrDefault(e => e.IsOpen);
            var lastDeparture = open is null ? lines.Max(e => e.DepartureUtc) : null;
            var bornes = (await _terminals.ListAsync(me.ClinicId, cancellationToken))
                .Where(t => t.IsLive)
                .Select(t => t.Name)
                .ToList();
            var here = await AttendanceRules.FindBorneAsync(_terminals, _tenantScope, request.Secret, cancellationToken);

            return Result<ClockStatusDto>.Success(new ClockStatusDto
            {
                UserId = me.Id,
                Name = StaffLeaveRules.DisplayName(me),
                Locked = await _lock.IsLockedAsync(me, now, new AttendanceLockReads(switches, lines.Count > 0), cancellationToken),
                Bornes = bornes,
                PresentSince = open is null ? null : AttendanceRules.LocalTime(open.ArrivalUtc),
                CanClockOutHere = here is not null && here.ClinicId == me.ClinicId,
                HasSchema = me.HasAttendanceSchema,
                BorneWaiting = borneWaiting,
                Tracked = true,
                LeftAt = lastDeparture is { } left ? AttendanceRules.LocalTime(left) : null,
                SchemaLockedUntil = me.IsSchemaLocked(now) ? me.SchemaLockedUntilUtc : null,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<ClockStatusDto>.Failure("Erreur lors de la lecture du pointage.", ex);
        }
    }
}
