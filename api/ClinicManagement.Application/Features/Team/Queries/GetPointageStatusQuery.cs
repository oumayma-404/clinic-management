using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// What a device learns about itself: anonymous, so any device may ask, and only a borne hears back the tiles — a
/// name, initials and today's state per person, nothing else.
/// </summary>
public class GetPointageStatusQuery : IRequest<Result<PointageStatusDto>>
{
    /// <summary>Set by the controller from the borne cookie.</summary>
    public string? Secret { get; set; }
}

public class GetPointageStatusQueryHandler : IRequestHandler<GetPointageStatusQuery, Result<PointageStatusDto>>
{
    private readonly IAttendanceTerminalRepository _terminals;
    private readonly IAttendanceEntryRepository _entries;
    private readonly IStaffLeaveRequestRepository _leaves;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly ITenantScope _tenantScope;

    public GetPointageStatusQueryHandler(
        IAttendanceTerminalRepository terminals, IAttendanceEntryRepository entries, IStaffLeaveRequestRepository leaves,
        IUserRepository users, IClinicRepository clinics, ITenantScope tenantScope)
    {
        _terminals = terminals;
        _entries = entries;
        _leaves = leaves;
        _users = users;
        _clinics = clinics;
        _tenantScope = tenantScope;
    }

    public async Task<Result<PointageStatusDto>> Handle(GetPointageStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var terminal = await AttendanceRules.FindBorneAsync(_terminals, _tenantScope, request.Secret, cancellationToken);
            if (terminal is null)
                return Result<PointageStatusDto>.Success(new PointageStatusDto { IsBorne = false });

            var clinic = await _clinics.GetByIdAsync(terminal.ClinicId, cancellationToken);
            if (clinic is not { AttendanceEnabled: true })
                return Result<PointageStatusDto>.Success(new PointageStatusDto { IsBorne = true, TerminalId = terminal.Id, Name = terminal.Name });

            var now = DateTime.UtcNow;
            var today = AttendanceRules.Today(now);
            var roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            var entries = await _entries.ListBetweenAsync(clinic.Id, today, today, null, cancellationToken);
            var leaves = await _leaves.GetApprovedOverlappingAsync(clinic.Id, today, today, cancellationToken);

            var people = roster.Values
                .Where(u => u.IsActive && u.IsAttendanceTracked)
                .Select(u =>
                {
                    var day = TeamTodayRules.PersonToday(u, entries, leaves, now);
                    return new PointagePersonDto
                    {
                        UserId = u.Id,
                        Name = day.FullName,
                        Initials = AttendanceRules.Initials(day.FullName),
                        State = day.State,
                        Since = day.Since,
                        NeedsSchema = day.NeedsSchema,
                        LockedUntil = day.SchemaLockedUntil,
                        LeftAt = day.LeftAt,
                    };
                })
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(p => p.UserId, StringComparer.Ordinal)
                .ToList();

            return Result<PointageStatusDto>.Success(new PointageStatusDto
            {
                IsBorne = true,
                TerminalId = terminal.Id,
                Enabled = true,
                Name = terminal.Name,
                People = people,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<PointageStatusDto>.Failure("Erreur lors du chargement de la borne.", ex);
        }
    }
}
