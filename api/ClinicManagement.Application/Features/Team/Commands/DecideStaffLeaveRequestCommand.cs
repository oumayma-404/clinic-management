using ClinicManagement.Application.Common;
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

/// <summary>An admin accepts (with the days counted) or refuses (with a motif); she is told in the app and by e-mail.</summary>
public class DecideStaffLeaveRequestCommand : IRequest<Result<StaffLeaveRequestDto>>
{
    public Guid Id { get; set; }
    public bool Approve { get; set; }

    /// <summary>The days the admin counts; absent means « as asked ».</summary>
    public decimal? CountedDays { get; set; }

    public string? RejectionReason { get; set; }

    /// <summary>The version the client read; <c>0</c> means « not supplied ».</summary>
    public uint Version { get; set; }
}

public class DecideStaffLeaveRequestCommandHandler
    : IRequestHandler<DecideStaffLeaveRequestCommand, Result<StaffLeaveRequestDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ITransactionalEmailSender _emailSender;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<DecideStaffLeaveRequestCommandHandler> _logger;

    public DecideStaffLeaveRequestCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ITransactionalEmailSender emailSender,
        IPublicAppUrlProvider appUrl,
        ILogger<DecideStaffLeaveRequestCommandHandler> logger)
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
        DecideStaffLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        StaffLeaveRequest leave;
        User admin;
        Clinic clinic;
        IReadOnlyDictionary<string, User> roster;
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AdminOnly);

            var found = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (found is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.ModuleOff);
            clinic = found;

            var lookup = await StaffLeaveRules.ResolveRequestAsync(_requests, clinic.Id, request.Id, cancellationToken);
            if (lookup.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(lookup);
            leave = lookup.Value!;
            if (leave.Status != StaffLeaveStatus.Pending)
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotPending);

            if (request.Approve)
            {
                var counted = request.CountedDays ?? leave.RequestedDays;
                if (StaffLeaveRequest.CountRefusalFor(counted) is { } refusal)
                    return Result<StaffLeaveRequestDto>.Failure(refusal);
                roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
                leave.Approve(admin.Id, counted);
            }
            else
            {
                if (StaffLeaveRequest.RejectionRefusalFor(request.RejectionReason) is { } refusal)
                    return Result<StaffLeaveRequestDto>.Failure(refusal);
                roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
                leave.Reject(admin.Id, request.RejectionReason!);
            }

            _unitOfWork.SetExpectedVersion(leave, request.Version);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors de l'enregistrement de la décision.", ex);
        }

        var notice = request.Approve ? StaffLeaveNotices.Approved(leave) : StaffLeaveNotices.Rejected(leave);
        var category = request.Approve ? NotificationCategory.StaffLeaveApproved : NotificationCategory.StaffLeaveRejected;
        await StaffLeaveNotices.NotifyAsync(
            _notifications, _logger, clinic.Id, category, notice, admin.Id, leave.UserId, cancellationToken);

        if (roster.TryGetValue(leave.UserId, out var owner))
        {
            var remaining = await RemainingAsync(owner, clinic, leave, cancellationToken);
            await StaffLeaveNotices.SendDecisionAsync(
                _emailSender, _appUrl, _logger, leave, owner, clinic.Name, StaffLeaveRules.DisplayName(admin), remaining,
                cancellationToken);
        }

        return Result<StaffLeaveRequestDto>.Success(StaffLeaveRules.ToDto(leave, roster, StaffLeaveRules.ClinicToday()));
    }

    /// <summary>The congé payé left in the request's year, for the e-mail; null when none is tracked or it cannot be read.</summary>
    private async Task<decimal?> RemainingAsync(User owner, Clinic clinic, StaffLeaveRequest leave, CancellationToken cancellationToken)
    {
        if (owner.AnnualLeaveAllowanceDays is null || leave.Type != StaffLeaveType.PaidLeave || leave.Status != StaffLeaveStatus.Approved)
            return null;
        try
        {
            var all = await _requests.GetByUserAsync(clinic.Id, owner.Id, cancellationToken);
            return StaffLeaveRules
                .Balance(leave.StartDate.Year, owner.AnnualLeaveAllowanceDays, all, clinic.WorkingHoursJson)
                .Remaining;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Congé balance could not be read for the decision e-mail.");
            return null;
        }
    }
}
