using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// What a request would count before it is sent — the day count and the congé payé left after it. The server's own
/// count, so the form cannot announce a figure the save would not store.
/// </summary>
public class PreviewStaffLeaveQuery : IRequest<Result<StaffLeavePreviewDto>>
{
    /// <summary>Whose congé; the caller's own when absent. Another account's needs an admin.</summary>
    public string? UserId { get; set; }

    public string? Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool HalfDay { get; set; }
}

public class PreviewStaffLeaveQueryHandler : IRequestHandler<PreviewStaffLeaveQuery, Result<StaffLeavePreviewDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public PreviewStaffLeaveQueryHandler(
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

    public async Task<Result<StaffLeavePreviewDto>> Handle(PreviewStaffLeaveQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeavePreviewDto>.FailureFrom(resolved);
            var caller = resolved.Value!;

            var target = caller;
            if (!string.IsNullOrEmpty(request.UserId) && request.UserId != caller.Id)
            {
                if (!caller.IsAdmin())
                    return Result<StaffLeavePreviewDto>.Failure(StaffLeaveRules.AdminOnly);
                var other = await _users.GetByIdAsync(request.UserId, cancellationToken);
                if (other == null || other.ClinicId != caller.ClinicId)
                    return Result<StaffLeavePreviewDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);
                target = other;
            }

            var clinic = await _clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeavePreviewDto>.Failure(StaffLeaveRules.ModuleOff);
            if (StaffLeaveRequest.RangeRefusalFor(request.StartDate, request.EndDate) is not null)
                return Result<StaffLeavePreviewDto>.Success(new StaffLeavePreviewDto());

            var halfDay = request.HalfDay && request.StartDate == request.EndDate;
            var days = StaffLeaveRules.CountDays(request.StartDate, request.EndDate, halfDay, clinic.WorkingHoursJson);

            decimal? balanceAfter = null;
            if (StaffLeaveRules.ParseType(request.Type) == StaffLeaveType.PaidLeave
                && target.AnnualLeaveAllowanceDays is { } allowance)
            {
                var all = await _requests.GetByUserAsync(caller.ClinicId, target.Id, cancellationToken);
                balanceAfter = StaffLeaveRules.BalanceAfter(
                    allowance, all, request.StartDate, request.EndDate, days, clinic.WorkingHoursJson);
            }

            return Result<StaffLeavePreviewDto>.Success(new StaffLeavePreviewDto { Days = days, BalanceAfter = balanceAfter });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeavePreviewDto>.Failure("Erreur lors du calcul des jours.", ex);
        }
    }
}
