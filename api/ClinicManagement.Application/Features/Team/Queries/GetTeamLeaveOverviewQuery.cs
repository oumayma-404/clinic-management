using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>« Utilisateurs → Congés »'s team strip: every active secretary's balance for the year and next congé.</summary>
public class GetTeamLeaveOverviewQuery : IRequest<Result<TeamLeaveOverviewDto>>
{
}

public class GetTeamLeaveOverviewQueryHandler : IRequestHandler<GetTeamLeaveOverviewQuery, Result<TeamLeaveOverviewDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetTeamLeaveOverviewQueryHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
    }

    public async Task<Result<TeamLeaveOverviewDto>> Handle(GetTeamLeaveOverviewQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<TeamLeaveOverviewDto>.FailureFrom(resolved);
            var admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<TeamLeaveOverviewDto>.Failure(StaffLeaveRules.AdminOnly);

            var year = ClinicClock.ClinicYear();
            var today = StaffLeaveRules.ClinicToday();
            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<TeamLeaveOverviewDto>.Failure(StaffLeaveRules.ModuleOff);
            var roster = await StaffLeaveRules.RosterAsync(_users, admin.ClinicId, cancellationToken);
            // Unpaged: a cabinet's congés are a handful per secretary per year.
            var all = await _requests.ListAsync(admin.ClinicId, null, null, null, cancellationToken);
            var byUser = all.Items.ToLookup(r => r.UserId);

            var members = roster.Values
                .Where(u => u.IsSecretary() && u.IsActive)
                .Select(u =>
                {
                    var own = byUser[u.Id].ToList();
                    var next = own
                        .Where(r => r.Status == StaffLeaveStatus.Approved && r.EndDate >= today)
                        .OrderBy(r => r.StartDate)
                        .FirstOrDefault();
                    return new TeamLeaveMemberDto
                    {
                        UserId = u.Id,
                        FullName = StaffLeaveRules.DisplayName(u),
                        Balance = StaffLeaveRules.Balance(year, u.AnnualLeaveAllowanceDays, own, clinic.WorkingHoursJson),
                        NextStartDate = next?.StartDate,
                        NextEndDate = next?.EndDate,
                        NextHalfDay = next?.HalfDay ?? false,
                    };
                })
                .OrderBy(m => m.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(m => m.UserId, StringComparer.Ordinal)
                .ToList();

            return Result<TeamLeaveOverviewDto>.Success(new TeamLeaveOverviewDto { Year = year, Members = members });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<TeamLeaveOverviewDto>.Failure("Erreur lors du chargement de l'équipe.", ex);
        }
    }
}
