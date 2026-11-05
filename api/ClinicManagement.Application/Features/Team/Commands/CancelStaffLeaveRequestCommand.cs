using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Team.Commands;

/// <summary>
/// Withdraws a request still waiting, or an accepted congé not yet begun — by its owner or an admin. Withdrawing an
/// accepted one tells the other side.
/// </summary>
public class CancelStaffLeaveRequestCommand : IRequest<Result<StaffLeaveRequestDto>>
{
    public Guid Id { get; set; }

    /// <summary>The version the client read; <c>0</c> means « not supplied ».</summary>
    public uint Version { get; set; }
}

public class CancelStaffLeaveRequestCommandHandler
    : IRequestHandler<CancelStaffLeaveRequestCommand, Result<StaffLeaveRequestDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<CancelStaffLeaveRequestCommandHandler> _logger;

    public CancelStaffLeaveRequestCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ILogger<CancelStaffLeaveRequestCommandHandler> logger)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<StaffLeaveRequestDto>> Handle(
        CancelStaffLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        StaffLeaveRequest leave;
        User caller;
        bool wasApproved;
        IReadOnlyDictionary<string, User> roster;
        var today = StaffLeaveRules.ClinicToday();
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            caller = resolved.Value!;

            var clinic = await _clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.ModuleOff);

            var lookup = await StaffLeaveRules.ResolveRequestAsync(_requests, clinic.Id, request.Id, cancellationToken);
            if (lookup.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(lookup);
            leave = lookup.Value!;

            if (leave.UserId != caller.Id && !caller.IsAdmin())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotOwnRequest);

            if (leave.Status == StaffLeaveStatus.Cancelled)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AlreadyCancelled);
            if (!leave.HoldsItsDays)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotPending);
            if (!leave.CanBeCancelledOn(today))
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AlreadyStarted);
            if (!leave.CanBeCancelledBy(caller.IsAdmin(), today))
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AdminEnteredOnly);

            roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            wasApproved = leave.Status == StaffLeaveStatus.Approved;
            leave.Cancel(caller.Id, today);
            _unitOfWork.SetExpectedVersion(leave, request.Version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors de l'annulation du congé.", ex);
        }

        if (leave.UserId != caller.Id)
        {
            await StaffLeaveNotices.NotifyAsync(
                _notifications, _logger, leave.ClinicId, NotificationCategory.StaffLeaveCancelled,
                StaffLeaveNotices.CancelledByAdmin(StaffLeaveRules.DisplayName(caller), leave), caller.Id, leave.UserId,
                cancellationToken);
        }
        else if (wasApproved)
        {
            await StaffLeaveNotices.NotifyAsync(
                _notifications, _logger, leave.ClinicId, NotificationCategory.StaffLeaveCancelled,
                StaffLeaveNotices.CancelledByOwner(StaffLeaveRules.DisplayName(caller), leave), caller.Id,
                targetUserId: null, cancellationToken);
        }

        return Result<StaffLeaveRequestDto>.Success(
            StaffLeaveRules.ToDto(leave, roster, today, viewerIsAdmin: caller.IsAdmin()));
    }
}
