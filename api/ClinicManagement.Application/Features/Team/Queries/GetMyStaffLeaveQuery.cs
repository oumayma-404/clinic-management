using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>The caller's own congés: the year's balance and every request, newest first.</summary>
public class GetMyStaffLeaveQuery : IRequest<Result<MyStaffLeaveDto>>
{
    /// <summary>The year the balance is for; the clinic's current year when absent.</summary>
    public int? Year { get; set; }
}

public class GetMyStaffLeaveQueryHandler : IRequestHandler<GetMyStaffLeaveQuery, Result<MyStaffLeaveDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public GetMyStaffLeaveQueryHandler(
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

    public async Task<Result<MyStaffLeaveDto>> Handle(GetMyStaffLeaveQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<MyStaffLeaveDto>.FailureFrom(resolved);
            var caller = resolved.Value!;

            var clinic = await _clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<MyStaffLeaveDto>.Failure(StaffLeaveRules.ModuleOff);
            var all = await _requests.GetByUserAsync(caller.ClinicId, caller.Id, cancellationToken);
            var roster = await StaffLeaveRules.RosterAsync(_users, caller.ClinicId, cancellationToken);
            var today = StaffLeaveRules.ClinicToday();

            return Result<MyStaffLeaveDto>.Success(new MyStaffLeaveDto
            {
                Balance = StaffLeaveRules.Balance(
                    request.Year ?? ClinicClock.ClinicYear(), caller.AnnualLeaveAllowanceDays, all, clinic.WorkingHoursJson),
                Items = all.Select(r => StaffLeaveRules.ToDto(r, roster, today, viewerIsAdmin: caller.IsAdmin())).ToList(),
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<MyStaffLeaveDto>.Failure("Erreur lors du chargement de vos congés.", ex);
        }
    }
}
