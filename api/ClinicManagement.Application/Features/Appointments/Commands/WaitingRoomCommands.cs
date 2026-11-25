using MediatR;
using Microsoft.Extensions.Logging;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Commands;

// The salle d'attente's reception commands (`salle-attente` S1–S3). They live under `Features/Appointments/Commands`
// on purpose: `RealtimeBroadcastBehavior` derives its key from the namespace, so every board move refreshes every
// screen listening to `appointments` — the agenda, the board, the doctor's queue — with no new realtime key.
//
// ⚠️ None of them takes a `Version`. `AppointmentProgressJob` writes appointments every minute, so a token the
// board read would be stale within a minute and every second press would be refused; each command guards on STATE
// instead (« is he still where the screen showed him? »), and the client's Idempotency-Key absorbs a double press.

/// <summary>
/// Move one visit to a lane of the board — « Arrivé », « Faire entrer », « Terminer », « Parti sans être vu »,
/// « Annuler l'arrivée », « Remettre… », and every drag. One command for all of them so the moves cannot drift apart:
/// the domain owns what each lane means (<see cref="Appointment.MoveInWaitingRoom"/>).
/// </summary>
public class MoveInWaitingRoomCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public Guid AppointmentId { get; set; }

    /// <summary><c>Upcoming</c> · <c>Waiting</c> · <c>InChair</c> · <c>Done</c> · <c>Left</c>.</summary>
    public string Lane { get; set; } = string.Empty;
}

public class MoveInWaitingRoomCommandHandler : IRequestHandler<MoveInWaitingRoomCommand, Result<WaitingRoomChangeDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<MoveInWaitingRoomCommandHandler> _logger;

    public MoveInWaitingRoomCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IMediator mediator,
        ILogger<MoveInWaitingRoomCommandHandler> logger)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(MoveInWaitingRoomCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto change;
        bool freshArrival;
        try
        {
            if (!WaitingRoomDay.TryParseLane(request.Lane, out var target))
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownLane, WaitingRoomRefusals.UnknownLaneCode);
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var appointment = day.Find(request.AppointmentId);
            if (appointment is null)
            {
                return await RefuseAbsentAsync(request.AppointmentId, clinic.Value, cancellationToken);
            }

            var from = appointment.WaitingRoomLane;
            change = new WaitingRoomChangeDto
            {
                AppointmentId = appointment.Id,
                PatientName = WaitingRoomDay.NameOf(appointment),
                Lane = target.ToString(),
            };

            if (from == target)
            {
                return Result<WaitingRoomChangeDto>.Success(change);
            }

            if (target == WaitingRoomLane.Upcoming && appointment.IsWalkIn)
            {
                return Result<WaitingRoomChangeDto>.Failure(
                    Appointment.WalkInHasNoSlotMessage, WaitingRoomRefusals.WalkInHasNoSlotCode);
            }

            change.Before.Add(WaitingRoomDay.Snapshot(appointment));

            // One patient per chair: whoever sits in this visit's chair has finished.
            if (target == WaitingRoomLane.InChair)
            {
                foreach (var seated in day.InChairOf(day.ChairOf(appointment)).Where(a => a.Id != appointment.Id))
                {
                    change.Before.Add(WaitingRoomDay.Snapshot(seated));
                    seated.MoveInWaitingRoom(WaitingRoomLane.Done, day.NowUtc);
                    await _appointments.UpdateAsync(seated, cancellationToken);
                }
            }

            appointment.MoveInWaitingRoom(target, day.NowUtc);
            await _appointments.UpdateAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            change.Changed = true;
            freshArrival = from == WaitingRoomLane.Upcoming && target == WaitingRoomLane.Waiting;
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }

        if (freshArrival)
        {
            await WaitingRoomArrivalNotice.TrySendAsync(_mediator, change.AppointmentId!.Value, _logger, cancellationToken);
        }

        return Result<WaitingRoomChangeDto>.Success(change);
    }

    private async Task<Result<WaitingRoomChangeDto>> RefuseAbsentAsync(Guid id, Guid clinicId, CancellationToken cancellationToken)
    {
        var any = await _appointments.GetByIdAsync(id, cancellationToken);
        if (any is null || any.ClinicId != clinicId)
        {
            return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotFound, WaitingRoomRefusals.NotFoundCode);
        }

        return any.WaitingRoomLane == WaitingRoomLane.None
            ? Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotOnBoard, WaitingRoomRefusals.NotOnBoardCode)
            : Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotToday, WaitingRoomRefusals.NotTodayCode);
    }
}

/// <summary>
/// The « Annuler » of every board action: put each touched visit back exactly as the action's own answer recorded
/// it, and take away a walk-in the action created. Only today's visits, and only coherent board states
/// (<see cref="Appointment.RestoreWaitingRoom"/>) — the snapshot travels through the browser.
/// </summary>
public class RestoreWaitingRoomCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public List<WaitingRoomSnapshotDto> Before { get; set; } = new();
    public List<Guid> RemoveWalkInIds { get; set; } = new();
}

public class RestoreWaitingRoomCommandHandler : IRequestHandler<RestoreWaitingRoomCommand, Result<WaitingRoomChangeDto>>
{
    /// <summary>A board action touches two visits at most (the one moved and the one it unseated); a little headroom.</summary>
    public const int MaxItems = 10;

    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;

    public RestoreWaitingRoomCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(RestoreWaitingRoomCommand request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Before.Count + request.RemoveWalkInIds.Count is 0 or > MaxItems)
            {
                return Result<WaitingRoomChangeDto>.Failure("Rien à annuler.");
            }

            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var after = new WaitingRoomChangeDto { Changed = true };

            foreach (var walkInId in request.RemoveWalkInIds.Distinct())
            {
                var walkIn = day.Find(walkInId);
                if (walkIn is null || !walkIn.IsWalkIn)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotFound, WaitingRoomRefusals.NotFoundCode);
                }

                // Only a walk-in nobody has seen yet goes away; once called in, the visit happened.
                if (walkIn.WaitingRoomLane != WaitingRoomLane.Waiting)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.WalkInHasWork, WaitingRoomRefusals.WalkInHasWorkCode);
                }

                after.Before.Add(WaitingRoomDay.Snapshot(walkIn));
                // « Retirer de la liste » rather than a delete: it takes the visit off the agenda, the board and the
                // figures, gives its slot back, and stays answerable in the journal.
                walkIn.Disregard(_clinicContext.GetUserId() ?? string.Empty, day.NowUtc);
                await _appointments.UpdateAsync(walkIn, cancellationToken);
            }

            foreach (var item in request.Before.GroupBy(b => b.AppointmentId).Select(g => g.First()))
            {
                if (request.RemoveWalkInIds.Contains(item.AppointmentId))
                {
                    continue;
                }

                var appointment = day.Find(item.AppointmentId);
                if (appointment is null)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotToday, WaitingRoomRefusals.NotTodayCode);
                }

                if (!WaitingRoomDay.TryParseLane(item.Lane, out var lane))
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownLane, WaitingRoomRefusals.UnknownLaneCode);
                }

                after.Before.Add(WaitingRoomDay.Snapshot(appointment));
                appointment.RestoreWaitingRoom(lane, item.ArrivedAtUtc, item.CalledInAtUtc, item.LeftChairAtUtc, day.NowUtc);

                // « Patient suivant » takes an unassigned patient for the dentist who called him; undoing the call
                // gives him back. Only ever back to null or to one of this cabinet's own dentists.
                if (item.DoctorId != appointment.DoctorId
                    && (item.DoctorId is null || day.Dentists.Any(d => d.Id == item.DoctorId)))
                {
                    appointment.SetDoctorId(item.DoctorId);
                }

                await _appointments.UpdateAsync(appointment, cancellationToken);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<WaitingRoomChangeDto>.Success(after);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message, WaitingRoomRefusals.IncoherentCode);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

/// <summary>
/// « Sans RDV » — a patient who walked in. Booked at the minute he arrived, already in the salle d'attente.
///
/// <para><b>It deduces before it books.</b> A patient who has a RDV today is marked arrived on that RDV instead of
/// getting a second visit beside it — the secretary never has to know which kind of arrival it is. One already in
/// the salle d'attente or in the chair is refused by name.</para>
///
/// <para>The patient comes first and separately: a « Nouveau patient » is created through the ordinary patient
/// door, which owns the duplicate check and the phone rules, and only its id reaches this command.</para>
/// </summary>
public class RegisterWalkInCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>Required when the cabinet has two or more active dentists; ignored as unnecessary otherwise.</summary>
    public Guid? DoctorId { get; set; }
}

public class RegisterWalkInCommandHandler : IRequestHandler<RegisterWalkInCommand, Result<WaitingRoomChangeDto>>
{
    /// <summary>A walk-in's agenda block. Nobody knows his act yet; the fiche records what was done.</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(30);

    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IPatientRepository _patients;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly INotificationGenerator _notifications;
    private readonly IAppointmentGoogleSyncDispatcher _googleSync;
    private readonly IMediator _mediator;
    private readonly ILogger<RegisterWalkInCommandHandler> _logger;

    public RegisterWalkInCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IPatientRepository patients,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        INotificationGenerator notifications,
        IAppointmentGoogleSyncDispatcher googleSync,
        IMediator mediator,
        ILogger<RegisterWalkInCommandHandler> logger)
    {
        _appointments = appointments;
        _doctors = doctors;
        _patients = patients;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _notifications = notifications;
        _googleSync = googleSync;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(RegisterWalkInCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto change;
        Appointment arrived;
        bool created;
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var patient = await _patients.GetByIdAsync(request.PatientId, cancellationToken);
            if (patient is null || patient.ClinicId != clinic.Value)
            {
                return Result<WaitingRoomChangeDto>.Failure("Ce patient est introuvable.", WaitingRoomRefusals.NotFoundCode);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var name = patient.GetFullName();
            var todays = day.Visits.Where(a => a.PatientId == patient.Id).ToList();

            if (todays.Any(a => a.WaitingRoomLane is WaitingRoomLane.Waiting or WaitingRoomLane.InChair))
            {
                return Result<WaitingRoomChangeDto>.Failure(
                    WaitingRoomRefusals.AlreadyHere(name), WaitingRoomRefusals.AlreadyHereCode);
            }

            change = new WaitingRoomChangeDto { Changed = true, PatientName = name, Lane = WaitingRoomLane.Waiting.ToString() };

            if (todays.Where(a => a.WaitingRoomLane == WaitingRoomLane.Upcoming).MinBy(a => a.AppointmentDateTime) is { } booked)
            {
                change.Before.Add(WaitingRoomDay.Snapshot(booked));
                booked.MoveInWaitingRoom(WaitingRoomLane.Waiting, day.NowUtc);
                await _appointments.UpdateAsync(booked, cancellationToken);
                change.UsedTodaysAppointment = true;
                arrived = booked;
                created = false;
            }
            else
            {
                Guid? doctorId;
                if (request.DoctorId is { } asked)
                {
                    if (!day.Dentists.Any(d => d.Id == asked))
                    {
                        return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownDentist, WaitingRoomRefusals.UnknownDentistCode);
                    }

                    doctorId = asked;
                }
                else if (day.IsGroupPractice)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NeedsDentist, WaitingRoomRefusals.NeedsDentistCode);
                }
                else
                {
                    doctorId = day.SoleDentistId;
                }

                arrived = Appointment.WalkIn(Guid.NewGuid(), clinic.Value, patient.Id, doctorId, day.NowUtc, RegisterWalkInCommandHandler.DefaultDuration);
                await _appointments.AddAsync(arrived, cancellationToken);
                change.RemoveWalkInIds.Add(arrived.Id);
                created = true;
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            change.AppointmentId = arrived.Id;
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }

        if (created)
        {
            // CreateAppointmentCommand's post-commit pair, for the same reasons: the dentist is reminded to write the
            // fiche when the visit ends, and a connected Google calendar shows the visit. Both best-effort.
            await _notifications.EnsurePostVisitReviewAsync(
                arrived.ClinicId, arrived.Id, arrived.DoctorId, change.PatientName ?? string.Empty, arrived.ExpectedEndUtc, cancellationToken);
            _googleSync.Dispatch(arrived.Id, arrived.ClinicId);
        }

        await WaitingRoomArrivalNotice.TrySendAsync(_mediator, arrived.Id, _logger, cancellationToken);
        return Result<WaitingRoomChangeDto>.Success(change);
    }
}

/// <summary>
/// « Arrivé » keeps telling the dentist, as « Signaler l'arrivée » always did (`salle-attente` D8). Best-effort and
/// after the commit: the arrival is recorded whether or not the notice can go (no dentist named, a dentist with no
/// account, the dentist marking his own patient), and a refusal here is never the secretary's problem.
/// </summary>
internal static class WaitingRoomArrivalNotice
{
    public static async Task TrySendAsync(IMediator mediator, Guid appointmentId, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            var sent = await mediator.Send(new SendArrivalNoticeCommand { AppointmentId = appointmentId }, cancellationToken);
            if (sent.IsFailure)
            {
                logger.LogDebug("Arrival of {AppointmentId} recorded without a notice: {Code}", appointmentId, sent.Code);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Arrival of {AppointmentId} recorded but its notice could not be sent", appointmentId);
        }
    }
}
