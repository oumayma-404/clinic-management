using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Appointments.Commands;

/// <summary>
/// « Patient suivant » (`salle-attente` S5): the dentist's one button. In one save it ends the visit in <b>his</b>
/// chair, calls the first of <b>his</b> queue (<see cref="Domain.Services.WaitingRoomOrder"/>) — or the patient he
/// named with « Appeler » — and, after the commit, rings reception with a <c>call-in</c> message in his secrétariat.
/// With nobody waiting it only ends the chair (« Terminer la séance »).
///
/// <para><b>Two devices, or a double tap, call one patient.</b> The button sends the id it was showing
/// (<see cref="ExpectedNextId"/>); a patient no longer waiting is refused by code and the screen refreshes, and the
/// client's Idempotency-Key answers a repeated request with the first answer.</para>
///
/// <para>⚠️ <b>The call-in message is built here, not taken from the cabinet's quick-message list</b>: it is not a
/// button in the chat bar (the dentist presses « Patient suivant », never a chat chip), so a cabinet that renamed or
/// removed « Faites entrer le patient suivant » loses nothing. The browser recognises it by its key and nothing
/// else.</para>
/// </summary>
public class CallNextPatientCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    /// <summary>« Appeler » on one patient; null for « Patient suivant ».</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>The patient the button was showing — refused if he is no longer waiting.</summary>
    public Guid? ExpectedNextId { get; set; }
}

public class CallNextPatientCommandHandler : IRequestHandler<CallNextPatientCommand, Result<WaitingRoomChangeDto>>
{
    /// <summary>The key reception's screens ring on. Never in the cabinet's editable list.</summary>
    public const string CallInKey = "call-in";

    /// <summary>The message's own wording — what the chat thread shows; the banner names the patient beside it.</summary>
    public const string CallInLabel = "Faites entrer le patient suivant";

    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUserRepository _users;
    private readonly IPatientRepository _patients;
    private readonly IStaffMessageRepository _messages;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IRealtimeNotifier _realtime;
    private readonly IStaffChatPushQueue _push;
    private readonly ILogger<CallNextPatientCommandHandler> _logger;

    public CallNextPatientCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUserRepository users,
        IPatientRepository patients,
        IStaffMessageRepository messages,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IRealtimeNotifier realtime,
        IStaffChatPushQueue push,
        ILogger<CallNextPatientCommandHandler> logger)
    {
        _appointments = appointments;
        _doctors = doctors;
        _users = users;
        _patients = patients;
        _messages = messages;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _realtime = realtime;
        _push = push;
        _logger = logger;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(CallNextPatientCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto change;
        Appointment? called;
        Doctor dentist;
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var resolved = await WaitingRoomCaller.ResolveDentistAsync(_clinicContext, _doctors, day, cancellationToken);
            if (resolved is null)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotADentist, WaitingRoomRefusals.NotADentistCode);
            }

            dentist = resolved;
            var queue = day.QueueOf(dentist.Id);

            if (request.AppointmentId is { } chosen)
            {
                called = day.Find(chosen);
                if (called is null || called.WaitingRoomLane != WaitingRoomLane.Waiting)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.AlreadyCalled, WaitingRoomRefusals.AlreadyCalledCode);
                }

                if (!queue.Contains(called))
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.OtherDentist, WaitingRoomRefusals.OtherDentistCode);
                }
            }
            else
            {
                called = queue.FirstOrDefault();
            }

            if (request.ExpectedNextId is { } expected && called?.Id != expected)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.AlreadyCalled, WaitingRoomRefusals.AlreadyCalledCode);
            }

            var seated = day.InChairOf(dentist.Id);
            if (called is null && seated.Count == 0)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.Nobody, WaitingRoomRefusals.NobodyCode);
            }

            change = new WaitingRoomChangeDto { Changed = true };

            foreach (var previous in seated)
            {
                change.Before.Add(WaitingRoomDay.Snapshot(previous));
                previous.MoveInWaitingRoom(WaitingRoomLane.Done, day.NowUtc);
                await _appointments.UpdateAsync(previous, cancellationToken);
            }

            if (called is not null)
            {
                change.Before.Add(WaitingRoomDay.Snapshot(called));
                if (called.DoctorId is null && day.IsGroupPractice)
                {
                    // An unassigned patient becomes the patient of the dentist who called him. Acknowledged as an
                    // overlap: he is seen now, whatever that dentist had booked at this hour.
                    called.SetDoctorId(dentist.Id);
                    called.MarkBookedWithOverlap();
                }

                called.MoveInWaitingRoom(WaitingRoomLane.InChair, day.NowUtc);
                await _appointments.UpdateAsync(called, cancellationToken);
                change.AppointmentId = called.Id;
                change.PatientName = WaitingRoomDay.NameOf(called);
                change.Lane = WaitingRoomLane.InChair.ToString();
            }
            else
            {
                change.AppointmentId = seated[0].Id;
                change.PatientName = WaitingRoomDay.NameOf(seated[0]);
                change.Lane = WaitingRoomLane.Done.ToString();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }

        if (called is not null)
        {
            await RingReceptionAsync(dentist, called, cancellationToken);
        }

        return Result<WaitingRoomChangeDto>.Success(change);
    }

    /// <summary>
    /// Post the call into the dentist's secrétariat. Best-effort: the call is already recorded and the board shows it
    /// at reception within a refresh, so a cabinet with no secretary account, or a failed post, loses only the chime.
    /// </summary>
    private async Task RingReceptionAsync(Doctor dentist, Appointment called, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(dentist.UserId))
            {
                return;
            }

            var loaded = await StaffChatRoster.LoadForCallerAsync(_clinicContext, _users, _doctors, cancellationToken);
            if (loaded.IsFailure)
            {
                return;
            }

            var roster = loaded.Value!;
            var desk = StaffConversation.Desk(dentist.UserId);
            var members = desk.Members(roster.Members);
            if (!roster.Members.Any(m => m.IsSecretary && members.Contains(m.UserId)))
            {
                return;
            }

            var message = StaffMessage.Create(
                roster.ClinicId, desk, roster.Viewer.UserId, null,
                new StaffQuickMessageSnapshot(CallInKey, CallInLabel, false),
                called.PatientId, called.Id, DateTime.UtcNow);
            await _messages.AddAsync(message, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await StaffMessagePosted.AnnounceAsync(
                message, desk, roster, _realtime, _push, _patients, _messages, _logger, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Patient {AppointmentId} called in, but reception could not be rung", called.Id);
        }
    }
}

/// <summary>
/// Who « Patient suivant » serves: the caller's own practitioner record — or, for an admin or a dentist account not
/// linked to one, the cabinet's only dentist. A secretary is never a dentist here, whatever the roster says.
/// </summary>
public static class WaitingRoomCaller
{
    public static async Task<Doctor?> ResolveDentistAsync(
        IClinicContext clinicContext, IDoctorRepository doctors, WaitingRoomDay day, CancellationToken cancellationToken)
    {
        if (string.Equals(clinicContext.GetUserRole(), User.RoleSecretary, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var userId = clinicContext.GetUserId();
        if (!string.IsNullOrEmpty(userId)
            && await doctors.GetByUserIdAsync(userId, cancellationToken) is { } own
            && own.ClinicId == day.ClinicId
            && own.IsActive)
        {
            return own;
        }

        return day.SoleDentistId is { } sole ? day.Dentists.First(d => d.Id == sole) : null;
    }
}
