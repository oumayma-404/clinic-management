using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// Sets or clears a secretary's yearly congé payé allowance. No version: the account row is rewritten by every
/// sign-in, so a token would refuse an admin's edit for a save nobody made.
/// </summary>
public class SetLeaveAllowanceCommand : IRequest<Result<StaffLeaveBalanceDto>>
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Days a year, in halves; null clears it.</summary>
    public decimal? AllowanceDays { get; set; }
}

public class SetLeaveAllowanceCommandHandler : IRequestHandler<SetLeaveAllowanceCommand, Result<StaffLeaveBalanceDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;

    public SetLeaveAllowanceCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<StaffLeaveBalanceDto>> Handle(SetLeaveAllowanceCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveBalanceDto>.FailureFrom(resolved);
            var admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveRules.AdminOnly);

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveRules.ModuleOff);

            var target = await _users.GetByIdAsync(request.UserId, cancellationToken);
            if (target == null || target.ClinicId != admin.ClinicId)
                return Result<StaffLeaveBalanceDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);
            if (User.AllowanceRefusalFor(request.AllowanceDays) is { } refusal)
                return Result<StaffLeaveBalanceDto>.Failure(refusal);

            var all = await _requests.GetByUserAsync(admin.ClinicId, target.Id, cancellationToken);
            target.SetAnnualLeaveAllowance(request.AllowanceDays);
            _users.Update(target);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result<StaffLeaveBalanceDto>.Success(
                StaffLeaveRules.Balance(ClinicClock.ClinicYear(), target.AnnualLeaveAllowanceDays, all, clinic.WorkingHoursJson));
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveBalanceDto>.Failure("Erreur lors de l'enregistrement du droit annuel.", ex);
        }
    }
}
