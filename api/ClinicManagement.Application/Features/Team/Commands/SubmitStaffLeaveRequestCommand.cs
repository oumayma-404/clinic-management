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

/// <summary>A secretary asks for a congé; every admin of the cabinet is told.</summary>
public class SubmitStaffLeaveRequestCommand : IRequest<Result<StaffLeaveRequestDto>>
{
    public string? Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool HalfDay { get; set; }

    /// <summary>« Morning » / « Afternoon » for a half day; absent when not said.</summary>
    public string? HalfDayPart { get; set; }

    public string? Reason { get; set; }
}

public class SubmitStaffLeaveRequestCommandHandler
    : IRequestHandler<SubmitStaffLeaveRequestCommand, Result<StaffLeaveRequestDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<SubmitStaffLeaveRequestCommandHandler> _logger;

    public SubmitStaffLeaveRequestCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ILogger<SubmitStaffLeaveRequestCommandHandler> logger)
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
        SubmitStaffLeaveRequestCommand request, CancellationToken cancellationToken)
    {
        StaffLeaveRequest leave;
        User caller;
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            caller = resolved.Value!;
            if (!caller.IsSecretary())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.SecretaryOnly);

            var clinic = await _clinics.GetByIdAsync(caller.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.ModuleOff);

            var valid = await StaffLeaveRules.ValidateNewAsync(
                _requests, clinic, caller, request.Type, request.StartDate, request.EndDate, request.HalfDay,
                request.HalfDayPart, request.Reason, StaffLeaveRules.OwnOverlapSentence, cancellationToken);
            if (valid.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(valid);

            var (type, days, part) = valid.Value;
            leave = new StaffLeaveRequest(
                Guid.NewGuid(), clinic.Id, caller.Id, type, request.StartDate, request.EndDate, request.HalfDay,
                request.Reason!, days, part);
            await _requests.AddAsync(leave, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors de l'envoi de la demande de congé.", ex);
        }

        await StaffLeaveNotices.NotifyAsync(
            _notifications, _logger, leave.ClinicId, NotificationCategory.StaffLeaveRequested,
            StaffLeaveNotices.Requested(StaffLeaveRules.DisplayName(caller), leave), caller.Id, targetUserId: null,
            cancellationToken);

        var roster = new Dictionary<string, User> { [caller.Id] = caller };
        return Result<StaffLeaveRequestDto>.Success(
            StaffLeaveRules.ToDto(leave, roster, StaffLeaveRules.ClinicToday(), viewerIsAdmin: false));
    }
}
