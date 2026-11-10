using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Queries;

/// <summary>
/// What a request would count before it is sent — the day count, whether the cabinet is open at all, the congé payé left
/// after it (and the day that is read on), and her own request already on these dates. The server's own count, so the
/// form cannot announce a figure the save would not store, nor find the overlap only on send.
/// </summary>
public class PreviewStaffLeaveQuery : IRequest<Result<StaffLeavePreviewDto>>
{
    /// <summary>The clinic day judged; today when absent. Tests pin it, so a balance never depends on when the suite runs.</summary>
    public DateOnly? Today { get; init; }

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
    private readonly IStaffLeaveAllowanceRepository _allowances;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;

    public PreviewStaffLeaveQueryHandler(
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

            StaffLeaveBalanceAfter? balanceAfter = null;
            if (StaffLeaveRules.ParseType(request.Type) == StaffLeaveType.PaidLeave)
            {
                var all = await _requests.GetByUserAsync(caller.ClinicId, target.Id, cancellationToken);
                var entries = await _allowances.GetForUserAsync(caller.ClinicId, target.Id, cancellationToken);
                balanceAfter = StaffLeaveAccounts.BalanceAfter(
                    entries, all, clinic.WorkingHoursJson, request.StartDate, request.EndDate, days, request.Today ?? StaffLeaveRules.ClinicToday());
            }

            var overlap = await _requests.FindHoldingOverlapAsync(
                caller.ClinicId, target.Id, request.StartDate, request.EndDate, cancellationToken);

            return Result<StaffLeavePreviewDto>.Success(new StaffLeavePreviewDto
            {
                Days = days,
                NoOpenDay = days == 0,
                BalanceAfter = balanceAfter?.Balance,
                BalanceAfterOn = balanceAfter?.On,
                BalanceAfterWithPending = balanceAfter?.WithPending,
                Overlap = overlap is null
                    ? null
                    : new StaffLeaveOverlapDto { StartDate = overlap.StartDate, EndDate = overlap.EndDate, Status = overlap.Status.ToString() },
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeavePreviewDto>.Failure("Erreur lors du calcul des jours.", ex);
        }
    }
}
