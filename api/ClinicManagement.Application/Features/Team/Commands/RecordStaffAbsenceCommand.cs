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

/// <summary>« Ajouter une absence »: an admin records a secretary's congé directly, accepted at once.</summary>
public class RecordStaffAbsenceCommand : IRequest<Result<StaffLeaveRequestDto>>
{
    public string UserId { get; set; } = string.Empty;
    public string? Type { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool HalfDay { get; set; }
    public string? Reason { get; set; }
}

public class RecordStaffAbsenceCommandHandler : IRequestHandler<RecordStaffAbsenceCommand, Result<StaffLeaveRequestDto>>
{
    private readonly IStaffLeaveRequestRepository _requests;
    private readonly IUserRepository _users;
    private readonly IClinicRepository _clinics;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notifications;
    private readonly ILogger<RecordStaffAbsenceCommandHandler> _logger;

    public RecordStaffAbsenceCommandHandler(
        IStaffLeaveRequestRepository requests,
        IUserRepository users,
        IClinicRepository clinics,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notifications,
        ILogger<RecordStaffAbsenceCommandHandler> logger)
    {
        _requests = requests;
        _users = users;
        _clinics = clinics;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<Result<StaffLeaveRequestDto>> Handle(RecordStaffAbsenceCommand request, CancellationToken cancellationToken)
    {
        StaffLeaveRequest leave;
        User admin;
        IReadOnlyDictionary<string, User> roster;
        try
        {
            var resolved = await StaffLeaveRules.ResolveCallerAsync(_clinicContext, _users, cancellationToken);
            if (resolved.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(resolved);
            admin = resolved.Value!;
            if (!admin.IsAdmin())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AdminOnly);

            var clinic = await _clinics.GetByIdAsync(admin.ClinicId, cancellationToken);
            if (clinic is not { LeaveRequestsEnabled: true })
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.ModuleOff);

            roster = await StaffLeaveRules.RosterAsync(_users, clinic.Id, cancellationToken);
            if (!roster.TryGetValue(request.UserId, out var target))
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.AccountNotFound, StaffLeaveRules.NotFoundCode);
            if (!target.IsSecretary())
                return Result<StaffLeaveRequestDto>.Failure(StaffLeaveRules.NotASecretary);

            var valid = await StaffLeaveRules.ValidateNewAsync(
                _requests, clinic, target, request.Type, request.StartDate, request.EndDate, request.HalfDay,
                request.Reason, overlap => StaffLeaveRules.OverlapSentence(StaffLeaveRules.DisplayName(target), overlap),
                cancellationToken);
            if (valid.IsFailure)
                return Result<StaffLeaveRequestDto>.FailureFrom(valid);

            var (type, days) = valid.Value;
            leave = StaffLeaveRequest.RecordByAdmin(
                Guid.NewGuid(), clinic.Id, target.Id, type, request.StartDate, request.EndDate, request.HalfDay,
                request.Reason!, days, admin.Id);
            await _requests.AddAsync(leave, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffLeaveRequestDto>.Failure("Erreur lors de l'enregistrement de l'absence.", ex);
        }

        await StaffLeaveNotices.NotifyAsync(
            _notifications, _logger, leave.ClinicId, NotificationCategory.StaffLeaveApproved,
            StaffLeaveNotices.RecordedByAdmin(leave), admin.Id, leave.UserId, cancellationToken);

        return Result<StaffLeaveRequestDto>.Success(StaffLeaveRules.ToDto(leave, roster, StaffLeaveRules.ClinicToday()));
    }
}
