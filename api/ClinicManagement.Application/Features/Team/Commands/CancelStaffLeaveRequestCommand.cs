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
/// Withdraws a congé. Its owner: a request still waiting, or an accepted congé not yet begun (never an absence the admin
/// recorded). An admin: any waiting request or accepted congé, whatever its dates — with a motif when it is someone
/// else's, which she reads in the bell and the e-mail. Withdrawing an accepted one tells the other side.
/// </summary>
public class CancelStaffLeaveRequestCommand : IRequest<Result<StaffLeaveRequestDto>>
{
    public Guid Id { get; set; }

    /// <summary>The version the client read; <c>0</c> means « not supplied ».</summary>
    public uint Version { get; set; }

    /// <summary>The motif; required when an admin withdraws someone else's.</summary>
    public string? Reason { get; set; }
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
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<CancelStaffLeaveRequestCommandHandler> _logger;

    public CancelStaffLeaveRequestCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger<CancelStaffLeaveRequestCommandHandler> logger)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _emailSender = emailSender;
        _appUrl = appUrl;
        _logger = logger;
    }

    public async Task<Result<StaffLeaveRequestDto>> Handle(
        CancelStaffLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        StaffLeaveRequest leave;
        User caller;
        Clinic clinic;
        bool wasApproved;
        IReadOnlyDictionary<string, User> roster;
        var today = StaffLeaveRules.ClinicToday();
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            caller = resolved.Value!;

            var found = await _clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (found is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.ModuleOff);
            clinic = found;

            var lookup = await StaffLeaveRules.ResolveRequestAsync(_requests, clinic.Id, request.Id, cancellationToken);
            if (lookup.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(lookup);
            leave = lookup.Value!;

            var byAdmin = caller.IsAdmin();
            var ownRequest = leave.UserId == caller.Id;
            if (!ownRequest && !byAdmin)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotOwnRequest);

            if (leave.Status == StaffLeaveStatus.Cancelled)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AlreadyCancelled);
            if (!leave.HoldsItsDays)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotPending);
            if (!byAdmin && !leave.CanBeCancelledOn(today))
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AlreadyStarted);
            if (!leave.CanBeCancelledBy(byAdmin, today))
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AdminEnteredOnly);
            if (StaffLeaveRequest.CancelRefusalFor(request.Reason, required: !ownRequest) is { } refusal)
            {
                return string.IsNullOrWhiteSpace(request.Reason)
                    ? Result<StaffLeaveRequestDto>.Failure(refusal, StaffLeaveRules.CancelReasonRequiredCode)
                    : Result<StaffLeaveRequestDto>.Failure(refusal);
            }

            roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            wasApproved = leave.Status == StaffLeaveStatus.Approved;
            leave.Cancel(caller.Id, today, byAdmin, request.Reason);
            _unitOfWork.SetExpectedVersion(leave, request.Version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors de l'annulation du congé.", ex);
        }

        if (leave.UserId != caller.Id)
        {
            var adminName = StaffLeaveRules.DisplayName(caller);
            await StaffLeaveNotices.NotifyAsync(
                _notifications, _logger, leave.ClinicId, NotificationCategory.StaffLeaveCancelled,
                StaffLeaveNotices.CancelledByAdmin(adminName, leave, wasApproved), caller.Id, leave.UserId,
                cancellationToken);
            if (roster.TryGetValue(leave.UserId, out var owner))
            {
                await StaffLeaveNotices.SendCancellationAsync(
                    _emailSender, _appUrl, _logger, leave, wasApproved, owner, clinic.Name, adminName, cancellationToken);
            }
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
