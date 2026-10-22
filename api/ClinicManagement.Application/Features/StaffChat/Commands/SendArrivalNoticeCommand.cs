using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.StaffChat.Commands;

/// <summary>
/// « Signaler l'arrivée » (US-3, FR-5): « Patient arrivé » with the appointment, to that appointment's dentist — into
/// their secretariat when a secretary sends it, into the two-person thread otherwise. Writes nothing on the appointment.
/// </summary>
public class SendArrivalNoticeCommand : IRequest<Result<StaffMessageDto>>
{
    public Guid AppointmentId { get; set; }
    /// <summary>Send again although someone already announced it today (AC-3.3) — the person confirmed.</summary>
    public bool Resend { get; set; }
}

public class SendArrivalNoticeCommandHandler : IRequestHandler<SendArrivalNoticeCommand, Result<StaffMessageDto>>
{
    private readonly IStaffMessageRepository _messages;
    private readonly IStaffQuickMessageListRepository _quickMessages;
    private readonly IPatientRepository _patients;
    private readonly IUserRepository _users;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRealtimeNotifier _realtime;
    private readonly IStaffChatPushQueue _push;
    private readonly ILogger<SendArrivalNoticeCommandHandler> _logger;

    public SendArrivalNoticeCommandHandler(
        IStaffMessageRepository messages,
        IStaffQuickMessageListRepository quickMessages,
        IPatientRepository patients,
        IUserRepository users,
        IDoctorRepository doctors,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        IRealtimeNotifier realtime,
        IStaffChatPushQueue push,
        ILogger<SendArrivalNoticeCommandHandler> logger)
    {
        _messages = messages;
        _quickMessages = quickMessages;
        _patients = patients;
        _users = users;
        _doctors = doctors;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _realtime = realtime;
        _push = push;
        _logger = logger;
    }

    public async Task<Result<StaffMessageDto>> Handle(SendArrivalNoticeCommand request, CancellationToken cancellationToken)
    {
        StaffMessage message;
        StaffChatRoster roster;
        StaffConversation conversation;
        try
        {
            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return Result<StaffMessageDto>.FailureFrom(loaded);
            }

            roster = loaded.Value!;
            var appointment = (await _messages.GetLinkedAppointmentsAsync(
                roster.ClinicId, new[] { request.AppointmentId }, cancellationToken)).FirstOrDefault();
            if (appointment is null || appointment.Disregarded)
            {
                return Refuse(StaffChatRefusals.AppointmentNotFound, StaffChatRefusals.AppointmentNotFoundCode);
            }

            var target = ArrivalTarget.For(appointment, roster, DateTime.UtcNow);
            if (target.Refusal is { } refusal)
            {
                return Refuse(refusal.Message, refusal.Code);
            }

            var dentistUserId = target.DentistUserId!;
            var desk = StaffConversation.Desk(dentistUserId);
            conversation = roster.Viewer.IsSecretary && desk.IsMember(roster.Viewer.UserId, roster.Members)
                ? desk
                : StaffConversation.Direct(roster.Viewer.UserId, dentistUserId);

            if (!request.Resend
                && await _messages.GetLastArrivalAsync(roster.ClinicId, appointment.Id, cancellationToken) is { } earlier)
            {
                var at = ClinicClock.ToClinicLocal(earlier.SentAt).ToString("HH:mm");
                return Refuse(
                    StaffChatRefusals.ArrivalAlreadySent(at, roster.NameOf(earlier.SenderUserId)),
                    StaffChatRefusals.ArrivalAlreadySentCode);
            }

            // The built-in « Patient arrivé » as the cabinet calls it now; it cannot be removed (AC-6.2).
            var list = await _quickMessages.GetByClinicIdAsync(roster.ClinicId, cancellationToken);
            var arrived = StaffQuickMessageCatalog.Effective(list)
                .First(i => i.Key == StaffQuickMessageDefaults.PatientArrivedKey)
                .Snapshot();

            message = StaffMessage.Create(
                roster.ClinicId, conversation, roster.Viewer.UserId, null, arrived, appointment.PatientId, appointment.Id,
                DateTime.UtcNow);
            await _messages.AddAsync(message, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<StaffMessageDto>.Failure("L'arrivée n'a pas pu être signalée.", ex);
        }

        return Result<StaffMessageDto>.Success(await StaffMessagePosted.AnnounceAsync(
            message, conversation, roster, _realtime, _push, _patients, _messages, _logger, cancellationToken));
    }

    private static Result<StaffMessageDto> Refuse(string message, string code) => Result<StaffMessageDto>.Failure(message, code);
}

/// <summary>
/// Who an arrival goes to, or why it cannot go (AC-3.4, FR-5) — one rule, read by the command and by the agenda's
/// action so the reason shown beside a disabled « Signaler l'arrivée » is the one the server would give.
/// </summary>
public sealed record ArrivalTarget(string? DentistUserId, (string Message, string Code)? Refusal)
{
    public static ArrivalTarget For(StaffLinkedAppointment appointment, StaffChatRoster roster, DateTime nowUtc)
    {
        if (appointment.PatientId is null)
        {
            return Refused(StaffChatRefusals.NoPatient, StaffChatRefusals.NoPatientCode);
        }

        var (from, to) = ClinicClock.TodayRangeUtc(nowUtc);
        if (appointment.StartsAt < from || appointment.StartsAt > to)
        {
            return Refused(StaffChatRefusals.AppointmentNotToday, StaffChatRefusals.AppointmentNotTodayCode);
        }

        // An appointment marked absent can still be announced — the patient came late (FR-5).
        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            return Refused(StaffChatRefusals.AppointmentCancelled, StaffChatRefusals.AppointmentCancelledCode);
        }

        if (appointment.DoctorId is null || roster.Dentists.FirstOrDefault(d => d.DoctorId == appointment.DoctorId) is not { } dentist)
        {
            return Refused(StaffChatRefusals.NoDentist, StaffChatRefusals.NoDentistCode);
        }

        if (string.IsNullOrEmpty(dentist.UserId) || roster.Find(dentist.UserId) is not { } account)
        {
            return Refused(StaffChatRefusals.DentistNoAccount, StaffChatRefusals.DentistNoAccountCode);
        }

        if (!dentist.IsActive || !account.IsActive)
        {
            return Refused(StaffChatRefusals.DentistInactive, StaffChatRefusals.DentistInactiveCode);
        }

        if (account.UserId == roster.Viewer.UserId)
        {
            return Refused(StaffChatRefusals.OwnAppointment, StaffChatRefusals.OwnAppointmentCode);
        }

        return new ArrivalTarget(account.UserId, null);
    }

    private static ArrivalTarget Refused(string message, string code) => new(null, (message, code));
}
