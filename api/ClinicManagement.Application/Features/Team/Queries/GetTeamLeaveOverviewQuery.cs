using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// « Utilisateurs → Congés »'s team strip: every active secretary's balance for the year, next congé and congé payé
/// still waiting for a decision.
/// </summary>
public class GetTeamLeaveOverviewQuery : IRequest<Result<TeamLeaveOverviewDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }
}

public class GetTeamLeaveOverviewQueryHandler : IRequestHandler<GetTeamLeaveOverviewQuery, Result<TeamLeaveOverviewDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetTeamLeaveOverviewQueryHandler(
        IStaffLeaveRequestRepository requests,
        IStaffLeaveAllowanceRepository allowances,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext)
    {
        _requests = requests;
        _allowances = allowances;
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

            var today = request.Today ?? StaffLeaveRules.ClinicToday();
            var year = today.Year;
            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<TeamLeaveOverviewDto>.Failure(StaffLeaveRules.ModuleOff);
            var roster = await StaffLeaveRules.RosterAsync(_users, admin.ClinicId, cancellationToken);
            // Unpaged: a cabinet's congés are a handful per secretary per year.
            var all = await _requests.ListAsync(admin.ClinicId, null, null, null, cancellationToken);
            var byUser = all.Items.ToLookup(r => r.UserId);
            // One read for the whole cabinet, grouped — never one per secretary.
            var entriesByUser = (await _allowances.GetForClinicAsync(admin.ClinicId, cancellationToken)).ToLookup(e => e.UserId);

            var members = roster.Values
                .Where(u => u.IsSecretary() && u.IsActive)
                .Select(u =>
                {
                    var own = byUser[u.Id].ToList();
                    var next = own
                        .Where(r => r.Status == StaffLeaveStatus.Approved && r.EndDate >= today)
                        .OrderBy(r => r.StartDate)
                        .FirstOrDefault();
                    var balance = StaffLeaveAccounts.Balance(
                        StaffLeaveAccounts.Fold(entriesByUser[u.Id], own, clinic.WorkingHoursJson, today), year);
                    return new TeamLeaveMemberDto
                    {
                        UserId = u.Id,
                        FullName = StaffLeaveRules.DisplayName(u),
                        Balance = balance,
                        NextStartDate = next?.StartDate,
                        NextEndDate = next?.EndDate,
                        NextHalfDay = next?.HalfDay ?? false,
                        NextHalfDayPart = next?.HalfDayPart?.ToString(),
                        // The year's share, as her own card counts it — a request for next year is not waiting on this one.
                        PendingDays = balance.Pending,
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
