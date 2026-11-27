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

// The salle d'attente's reception commands (`salle-attente` S1–S3, round 2). They live under
// `Features/Appointments/Commands` on purpose: `RealtimeBroadcastBehavior` derives its key from the namespace, so every
// board move refreshes every screen listening to `appointments` — the agenda, the board, the doctor's queue — with no
// new realtime key.
//
// ⚠️ None of them takes a `Version`. `AppointmentProgressJob` writes appointments every minute, so a token the
// board read would be stale within a minute and every second press would be refused; each command guards on STATE
// instead (« is he still where the screen showed him? »), and the client's Idempotency-Key absorbs a double press.
// « Annuler » guards the same way, on the whole board state (round 2, C9).
//
// ⚠️ The optional constructor parameters (Google, reminders, notices) are optional so the unit tests can build a
// handler without them; DI always supplies them.

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

    /// <summary>
    /// The dentist who will see him — required to seat a visit naming no dentist in a cabinet de groupe (round 2,
    /// C7: « Faire entrer » used to seat him in nobody's chair and end every other unassigned patient). Ignored
    /// everywhere else.
    /// </summary>
    public Guid? DoctorId { get; set; }
}

public class MoveInWaitingRoomCommandHandler : IRequestHandler<MoveInWaitingRoomCommand, Result<WaitingRoomChangeDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IMediator _mediator;
    private readonly ILogger<MoveInWaitingRoomCommandHandler> _logger;
    private readonly IAppointmentGoogleSyncDispatcher? _googleSync;

    public MoveInWaitingRoomCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IMediator mediator,
        ILogger<MoveInWaitingRoomCommandHandler> logger,
        IAppointmentGoogleSyncDispatcher? googleSync = null)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _mediator = mediator;
        _logger = logger;
        _googleSync = googleSync;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(MoveInWaitingRoomCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto change;
        bool freshArrival;
        Guid clinicId;
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

            clinicId = clinic.Value;
            var day = await WaitingRoomDay.LoadAsync(clinicId, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var appointment = day.Find(request.AppointmentId);
            if (appointment is null)
            {
                return await WaitingRoomRefusal.AbsentAsync(_appointments, request.AppointmentId, clinicId, cancellationToken);
            }

            var from = appointment.WaitingRoomLane;
            var name = WaitingRoomDay.NameOf(appointment);
            change = new WaitingRoomChangeDto
            {
                AppointmentId = appointment.Id,
                PatientName = name,
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

            // One live card per patient (C4): his 16:00 cannot arrive while he waits for — or sits in — his 10:00.
            if (target is WaitingRoomLane.Waiting or WaitingRoomLane.InChair && day.OtherLiveVisitOf(appointment) is not null)
            {
                return Result<WaitingRoomChangeDto>.Failure(
                    WaitingRoomRefusals.AlreadyHere(name), WaitingRoomRefusals.AlreadyHereCode);
            }

            change.Before.Add(WaitingRoomDay.Snapshot(appointment));
            var touched = new List<Appointment> { appointment };

            if (target == WaitingRoomLane.InChair)
            {
                var seated = await WaitingRoomSeating.SeatAsync(
                    day, appointment, request.DoctorId, change, touched, _appointments, cancellationToken);
                if (seated is { } refusal)
                {
                    return refusal;
                }
            }
            else
            {
                appointment.MoveInWaitingRoom(target, day.NowUtc);
            }

            await _appointments.UpdateAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            WaitingRoomDay.RecordAfter(change, touched);
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

        WaitingRoomSideEffects.SyncCalendar(_googleSync, change, clinicId);
        if (freshArrival)
        {
            await WaitingRoomArrivalNotice.TrySendAsync(_mediator, change.AppointmentId!.Value, _logger, cancellationToken);
        }

        return Result<WaitingRoomChangeDto>.Success(change);
    }
}

/// <summary>
/// Seating a patient — « Faire entrer », a drag to « Au fauteuil », « Patient suivant », the next call after
/// « Pas là ». One place, because three commands seat and they must agree about whose chair it is and who leaves it.
/// </summary>
internal static class WaitingRoomSeating
{
    /// <summary>
    /// Put <paramref name="visit"/> in its chair. In a cabinet de groupe a visit naming no dentist first takes
    /// <paramref name="dentistId"/> (refused without one — C7). In a one-chair practice whoever sits in that chair has
    /// finished; with « 2 fauteuils » (Q3) nobody is ended and a third patient is refused. Returns the refusal, or null.
    /// </summary>
    public static async Task<Result<WaitingRoomChangeDto>?> SeatAsync(
        WaitingRoomDay day,
        Appointment visit,
        Guid? dentistId,
        WaitingRoomChangeDto change,
        List<Appointment> touched,
        IAppointmentRepository appointments,
        CancellationToken cancellationToken)
    {
        if (visit.DoctorId is null && day.IsGroupPractice)
        {
            if (dentistId is not { } chosen)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NeedsDentist, WaitingRoomRefusals.NeedsDentistCode);
            }

            if (!day.Dentists.Any(d => d.Id == chosen))
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownDentist, WaitingRoomRefusals.UnknownDentistCode);
            }

            // He becomes that dentist's patient, seen now whatever that dentist had booked at this hour.
            visit.SetDoctorId(chosen);
            visit.MarkBookedWithOverlap();
        }

        var chair = day.ChairOf(visit);
        var others = day.InChairOf(chair).Where(a => a.Id != visit.Id).ToList();
        if (day.HasTwoChairs(chair))
        {
            if (others.Count >= 2)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.ChairsFull, WaitingRoomRefusals.ChairsFullCode);
            }
        }
        else
        {
            // One patient per chair: whoever sits in this visit's chair has finished.
            foreach (var seated in others)
            {
                change.Before.Add(WaitingRoomDay.Snapshot(seated));
                seated.MoveInWaitingRoom(WaitingRoomLane.Done, day.NowUtc);
                await appointments.UpdateAsync(seated, cancellationToken);
                touched.Add(seated);
            }
        }

        visit.MoveInWaitingRoom(WaitingRoomLane.InChair, day.NowUtc);
        return null;
    }
}

/// <summary>The refusals more than one board command answers with.</summary>
internal static class WaitingRoomRefusal
{
    /// <summary>A visit that is not on today's board: unknown, not on the board at all, or another day.</summary>
    public static async Task<Result<WaitingRoomChangeDto>> AbsentAsync(
        IAppointmentRepository appointments, Guid id, Guid clinicId, CancellationToken cancellationToken)
    {
        var any = await appointments.GetByIdAsync(id, cancellationToken);
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
/// What a board change sets off outside the database, after the commit and best-effort: the connected Google agenda
/// learns the RDV's real time (Q1) or its new day (C6), and the reminders follow a RDV moved to another day.
/// </summary>
internal static class WaitingRoomSideEffects
{
    /// <summary>Push to Google every visit whose RDV time the change moved.</summary>
    public static void SyncCalendar(IAppointmentGoogleSyncDispatcher? google, WaitingRoomChangeDto change, Guid clinicId)
    {
        if (google is null)
        {
            return;
        }

        foreach (var after in change.After)
        {
            var before = change.Before.FirstOrDefault(b => b.AppointmentId == after.AppointmentId);
            if (before is not null
                && (before.AppointmentDateTime != after.AppointmentDateTime || before.DurationMinutes != after.DurationMinutes))
            {
                google.Dispatch(after.AppointmentId, clinicId);
            }
        }
    }

    /// <summary>
    /// A RDV that changed day — brought to today by « Le recevoir aujourd'hui », or sent back by its « Annuler » — has
    /// its reminders, its « déplacé » notice and its post-visit review follow it, as the agenda does for a move.
    /// </summary>
    public static async Task FollowDayMovesAsync(
        WaitingRoomChangeDto change,
        IEnumerable<Appointment> touched,
        string? actorUserId,
        INotificationGenerator? notifications,
        IReminderScheduler? reminders,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        foreach (var appointment in touched)
        {
            var before = change.Before.FirstOrDefault(b => b.AppointmentId == appointment.Id);
            if (before is null || appointment.PatientId is not { } patientId
                || ClinicClock.ToClinicLocal(before.AppointmentDateTime).Date
                   == ClinicClock.ToClinicLocal(appointment.AppointmentDateTime).Date)
            {
                continue;
            }

            try
            {
                var name = WaitingRoomDay.NameOf(appointment);
                if (notifications is not null)
                {
                    await notifications.AppointmentRescheduledAsync(
                        appointment.ClinicId, appointment.Id, actorUserId, name,
                        before.AppointmentDateTime, appointment.AppointmentDateTime, cancellationToken);
                    await notifications.EnsurePostVisitReviewAsync(
                        appointment.ClinicId, appointment.Id, appointment.DoctorId, name,
                        appointment.ExpectedEndUtc, cancellationToken);
                }

                if (reminders is not null)
                {
                    await reminders.RescheduleForAppointmentAsync(
                        appointment.ClinicId, appointment.Id, patientId, name,
                        appointment.AppointmentDateTime, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "RDV {AppointmentId} moved day on the board; its notices could not follow", appointment.Id);
            }
        }
    }
}

/// <summary>
/// The « Annuler » of every board action: put each touched visit back exactly as the action's own answer recorded
/// it, and take away a walk-in the action created. Only today's visits, only coherent board states
/// (<see cref="Appointment.RestoreWaitingRoom"/>) — the snapshot travels through the browser — and only while
/// nobody has moved the card since (round 2, C9): an undo pressed after a colleague's move, a payment or a fiche used
/// to overwrite it in silence.
/// </summary>
public class RestoreWaitingRoomCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public List<WaitingRoomSnapshotDto> Before { get; set; } = new();

    /// <summary>The action's own <c>After</c>: what each card must still be for the undo to apply.</summary>
    public List<WaitingRoomSnapshotDto> After { get; set; } = new();

    public List<Guid> RemoveWalkInIds { get; set; } = new();
}

public class RestoreWaitingRoomCommandHandler : IRequestHandler<RestoreWaitingRoomCommand, Result<WaitingRoomChangeDto>>
{
    /// <summary>A board action touches three visits at most (the one moved, the one it unseated, the next called).</summary>
    public const int MaxItems = 10;

    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;
    private readonly IAppointmentGoogleSyncDispatcher? _googleSync;
    private readonly INotificationGenerator? _notifications;
    private readonly IReminderScheduler? _reminders;
    private readonly ILogger<RestoreWaitingRoomCommandHandler>? _logger;

    public RestoreWaitingRoomCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext,
        IAppointmentGoogleSyncDispatcher? googleSync = null,
        INotificationGenerator? notifications = null,
        IReminderScheduler? reminders = null,
        ILogger<RestoreWaitingRoomCommandHandler>? logger = null)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
        _googleSync = googleSync;
        _notifications = notifications;
        _reminders = reminders;
        _logger = logger;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(RestoreWaitingRoomCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto after;
        var touched = new List<Appointment>();
        Guid clinicId;
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

            clinicId = clinic.Value;
            var day = await WaitingRoomDay.LoadAsync(clinicId, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            after = new WaitingRoomChangeDto { Changed = true };

            // Every card first, before anything is written: one card moved since refuses the whole undo.
            foreach (var expected in request.After.GroupBy(a => a.AppointmentId).Select(g => g.First()))
            {
                var current = day.Find(expected.AppointmentId);
                var state = WaitingRoomDay.ToState(expected);
                if (current is null || state is null || !current.CaptureWaitingRoomState().SameAs(state))
                {
                    var name = current is null ? "Ce patient" : WaitingRoomDay.NameOf(current);
                    return Result<WaitingRoomChangeDto>.Failure(
                        WaitingRoomRefusals.MovedSince(name), WaitingRoomRefusals.MovedSinceCode);
                }
            }

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

                if (WaitingRoomDay.ToState(item) is not { } state)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownLane, WaitingRoomRefusals.UnknownLaneCode);
                }

                after.Before.Add(WaitingRoomDay.Snapshot(appointment));
                appointment.RestoreWaitingRoom(state, day.NowUtc);

                // A seat taken for a dentist, a « Changer de praticien »: undoing it gives the patient back. Only ever
                // back to null or to one of this cabinet's own dentists.
                if (item.DoctorId != appointment.DoctorId
                    && (item.DoctorId is null || day.Dentists.Any(d => d.Id == item.DoctorId)))
                {
                    appointment.SetDoctorId(item.DoctorId);
                }

                await _appointments.UpdateAsync(appointment, cancellationToken);
                touched.Add(appointment);
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            WaitingRoomDay.RecordAfter(after, touched);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message, WaitingRoomRefusals.IncoherentCode);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }

        WaitingRoomSideEffects.SyncCalendar(_googleSync, after, clinicId);
        if (_logger is not null)
        {
            await WaitingRoomSideEffects.FollowDayMovesAsync(
                after, touched, _clinicContext.GetUserId(), _notifications, _reminders, _logger, cancellationToken);
        }

        return Result<WaitingRoomChangeDto>.Success(after);
    }
}

/// <summary>
/// « Sans RDV » — a patient who walked in. Booked at the minute he arrived, already in the salle d'attente.
///
/// <para><b>It deduces before it books.</b> A patient who has a RDV today is marked arrived on that RDV instead of
/// getting a second visit beside it — the secretary never has to know which kind of arrival it is. One already in
/// the salle d'attente or in the chair is refused by name.</para>
///
/// <para><b>« Le recevoir aujourd'hui »</b> (round 2, C6) is the same door with <see cref="AppointmentId"/> naming a
/// RDV booked on another day: that RDV moves to now, arrived — never a second visit beside it, which is how the
/// patient would have come back on Thursday too.</para>
///
/// <para>The patient comes first and separately: a « Nouveau patient » is created through the ordinary patient
/// door, which owns the duplicate check and the phone rules, and only its id reaches this command.</para>
/// </summary>
public class RegisterWalkInCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public Guid PatientId { get; set; }

    /// <summary>Required when the cabinet has two or more active dentists; ignored as unnecessary otherwise.</summary>
    public Guid? DoctorId { get; set; }

    /// <summary>The patient's RDV on another day, received today instead (C6). Null for a plain walk-in.</summary>
    public Guid? AppointmentId { get; set; }
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
    private readonly IReminderScheduler? _reminders;
    private readonly IClinicContext? _clinicContext;

    public RegisterWalkInCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IPatientRepository patients,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        INotificationGenerator notifications,
        IAppointmentGoogleSyncDispatcher googleSync,
        IMediator mediator,
        ILogger<RegisterWalkInCommandHandler> logger,
        IReminderScheduler? reminders = null,
        IClinicContext? clinicContext = null)
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
        _reminders = reminders;
        _clinicContext = clinicContext;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(RegisterWalkInCommand request, CancellationToken cancellationToken)
    {
        WaitingRoomChangeDto change;
        Appointment arrived;
        bool created;
        bool movedFromOtherDay = false;
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
            else if (request.AppointmentId is { } otherDayId)
            {
                var otherDay = await _appointments.GetByIdAsync(otherDayId, cancellationToken);
                if (otherDay is null || otherDay.ClinicId != clinic.Value || otherDay.PatientId != patient.Id)
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotFound, WaitingRoomRefusals.NotFoundCode);
                }

                if (otherDay.IsDisregarded
                    || otherDay.Status is not (AppointmentStatus.Scheduled or AppointmentStatus.Confirmed)
                    || ClinicClock.ToClinicLocal(otherDay.AppointmentDateTime).Date == ClinicClock.ClinicToday(day.NowUtc))
                {
                    return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotOtherDay, WaitingRoomRefusals.NotOtherDayCode);
                }

                change.Before.Add(WaitingRoomDay.Snapshot(otherDay));
                otherDay.ReceiveToday(day.NowUtc);
                await _appointments.UpdateAsync(otherDay, cancellationToken);
                arrived = otherDay;
                created = false;
                movedFromOtherDay = true;
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
            if (!created)
            {
                WaitingRoomDay.RecordAfter(change, new[] { arrived });
            }
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
        else if (movedFromOtherDay)
        {
            _googleSync.Dispatch(arrived.Id, arrived.ClinicId);
            await WaitingRoomSideEffects.FollowDayMovesAsync(
                change, new[] { arrived }, _clinicContext?.GetUserId(), _notifications, _reminders, _logger, cancellationToken);
        }

        await WaitingRoomArrivalNotice.TrySendAsync(_mediator, arrived.Id, _logger, cancellationToken);
        return Result<WaitingRoomChangeDto>.Success(change);
    }
}

/// <summary>
/// « Pas là » (round 2, E1) — the patient called in is not there: out at the pharmacy, on the phone, in the car. He
/// goes back to the salle d'attente and keeps his place (tagged until his next call), and the next patient of that
/// chair is called instead, so the dentist is not left waiting for somebody who is not coming in. Any role: it is
/// reception who finds the waiting room empty, and the dentist who finds nobody walking in.
/// </summary>
public class SkipCalledPatientCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    /// <summary>The patient called in who is not there.</summary>
    public Guid AppointmentId { get; set; }
}

public class SkipCalledPatientCommandHandler : IRequestHandler<SkipCalledPatientCommand, Result<WaitingRoomChangeDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public SkipCalledPatientCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(SkipCalledPatientCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var absent = day.Find(request.AppointmentId);
            if (absent is null || absent.WaitingRoomLane != WaitingRoomLane.InChair)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotInChair, WaitingRoomRefusals.NotInChairCode);
            }

            var chair = day.ChairOf(absent);
            var change = new WaitingRoomChangeDto { Changed = true };
            var touched = new List<Appointment> { absent };

            change.Before.Add(WaitingRoomDay.Snapshot(absent));
            absent.SkipCall(day.NowUtc);
            await _appointments.UpdateAsync(absent, cancellationToken);

            // The next of that chair's queue, the absent patient aside — he keeps his place for when he is back.
            var next = chair is { } dentistId
                ? day.QueueOf(dentistId).FirstOrDefault(a => a.Id != absent.Id)
                : null;
            if (next is not null)
            {
                change.Before.Add(WaitingRoomDay.Snapshot(next));
                touched.Add(next);
                var refused = await WaitingRoomSeating.SeatAsync(day, next, chair, change, touched, _appointments, cancellationToken);
                if (refused is { } refusal)
                {
                    return refusal;
                }

                await _appointments.UpdateAsync(next, cancellationToken);
                change.AppointmentId = next.Id;
                change.PatientName = WaitingRoomDay.NameOf(next);
                change.Lane = WaitingRoomLane.InChair.ToString();
            }
            else
            {
                change.AppointmentId = absent.Id;
                change.PatientName = WaitingRoomDay.NameOf(absent);
                change.Lane = absent.WaitingRoomLane.ToString();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            WaitingRoomDay.RecordAfter(change, touched);
            return Result<WaitingRoomChangeDto>.Success(change);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

/// <summary>
/// « Passer en premier » (round 2, E2) — a walk-in with acute pain, a child, an elderly patient goes before the queue,
/// marked « Urgent ». <see cref="Urgent"/> false takes the mark off. Only a patient in the salle d'attente.
/// </summary>
public class SetWaitingRoomUrgentCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public Guid AppointmentId { get; set; }
    public bool Urgent { get; set; } = true;
}

public class SetWaitingRoomUrgentCommandHandler : IRequestHandler<SetWaitingRoomUrgentCommand, Result<WaitingRoomChangeDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;

    public SetWaitingRoomUrgentCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(SetWaitingRoomUrgentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var visit = day.Find(request.AppointmentId);
            if (visit is null || visit.WaitingRoomLane != WaitingRoomLane.Waiting)
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotWaiting, WaitingRoomRefusals.NotWaitingCode);
            }

            var change = new WaitingRoomChangeDto
            {
                AppointmentId = visit.Id,
                PatientName = WaitingRoomDay.NameOf(visit),
                Lane = WaitingRoomLane.Waiting.ToString(),
            };
            change.Before.Add(WaitingRoomDay.Snapshot(visit));
            if (!visit.SetUrgent(request.Urgent, day.NowUtc))
            {
                change.Before.Clear();
                return Result<WaitingRoomChangeDto>.Success(change);
            }

            await _appointments.UpdateAsync(visit, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            WaitingRoomDay.RecordAfter(change, new[] { visit });
            change.Changed = true;
            return Result<WaitingRoomChangeDto>.Success(change);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

/// <summary>
/// « Changer de praticien » (round 2, C10) — a dentist on congé, or gone from the team, still has RDVs today: his
/// patients sat in no queue anybody serves. Only a patient not yet seen (« À venir » or in the salle d'attente).
/// </summary>
public class SetWaitingRoomDentistCommand : IRequest<Result<WaitingRoomChangeDto>>
{
    public Guid AppointmentId { get; set; }
    public Guid DoctorId { get; set; }
}

public class SetWaitingRoomDentistCommandHandler : IRequestHandler<SetWaitingRoomDentistCommand, Result<WaitingRoomChangeDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IAppointmentGoogleSyncDispatcher? _googleSync;

    public SetWaitingRoomDentistCommandHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ICurrentClinicResolver clinicResolver,
        IAppointmentGoogleSyncDispatcher? googleSync = null)
    {
        _appointments = appointments;
        _doctors = doctors;
        _unitOfWork = unitOfWork;
        _clinicResolver = clinicResolver;
        _googleSync = googleSync;
    }

    public async Task<Result<WaitingRoomChangeDto>> Handle(SetWaitingRoomDentistCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomChangeDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var visit = day.Find(request.AppointmentId);
            if (visit is null || visit.WaitingRoomLane is not (WaitingRoomLane.Upcoming or WaitingRoomLane.Waiting))
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.NotWaiting, WaitingRoomRefusals.NotWaitingCode);
            }

            if (!day.Dentists.Any(d => d.Id == request.DoctorId))
            {
                return Result<WaitingRoomChangeDto>.Failure(WaitingRoomRefusals.UnknownDentist, WaitingRoomRefusals.UnknownDentistCode);
            }

            var change = new WaitingRoomChangeDto
            {
                AppointmentId = visit.Id,
                PatientName = WaitingRoomDay.NameOf(visit),
                Lane = visit.WaitingRoomLane.ToString(),
            };
            if (visit.DoctorId == request.DoctorId)
            {
                return Result<WaitingRoomChangeDto>.Success(change);
            }

            change.Before.Add(WaitingRoomDay.Snapshot(visit));
            visit.SetDoctorId(request.DoctorId);
            // He is seen by that dentist today whatever that dentist had booked at this hour.
            visit.MarkBookedWithOverlap();
            await _appointments.UpdateAsync(visit, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            WaitingRoomDay.RecordAfter(change, new[] { visit });
            change.Changed = true;

            _googleSync?.Dispatch(visit.Id, visit.ClinicId);
            return Result<WaitingRoomChangeDto>.Success(change);
        }
        catch (InvalidOperationException ex)
        {
            return Result<WaitingRoomChangeDto>.Failure(ex.Message);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomChangeDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}

/// <summary>
/// « 2 fauteuils » (round 2, Q3) — a dentist who leaves a patient in one chair while the anaesthesia works and takes
/// the next in the other. The dentist sets it for himself; an administrator may set it for any dentist.
/// </summary>
public class SetDentistChairsCommand : IRequest<Result<WaitingRoomDentistDto>>
{
    /// <summary>The dentist; null for the caller's own practitioner record.</summary>
    public Guid? DoctorId { get; set; }

    public bool TwoChairs { get; set; }
}

public class SetDentistChairsCommandHandler : IRequestHandler<SetDentistChairsCommand, Result<WaitingRoomDentistDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;

    public SetDentistChairsCommandHandler(
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

    public async Task<Result<WaitingRoomDentistDto>> Handle(SetDentistChairsCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomDentistDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var caller = await WaitingRoomCaller.ResolveDentistAsync(_clinicContext, _doctors, day, cancellationToken);
            var isAdmin = _clinicContext.GetUserRole() is { } callerRole
                && (string.Equals(callerRole, User.RoleAdmin, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(callerRole, User.RoleManager, StringComparison.OrdinalIgnoreCase));

            var targetId = request.DoctorId ?? caller?.Id;
            if (targetId is not { } id || (caller?.Id != id && !isAdmin))
            {
                return Result<WaitingRoomDentistDto>.Failure(WaitingRoomRefusals.NotYourChairs, WaitingRoomRefusals.NotYourChairsCode);
            }

            var dentist = await _doctors.GetByIdAsync(id, cancellationToken);
            if (dentist is null || dentist.ClinicId != clinic.Value || !dentist.IsActive)
            {
                return Result<WaitingRoomDentistDto>.Failure(WaitingRoomRefusals.UnknownDentist, WaitingRoomRefusals.UnknownDentistCode);
            }

            if (dentist.SetWorksTwoChairs(request.TwoChairs))
            {
                _doctors.Update(dentist);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return Result<WaitingRoomDentistDto>.Success(new WaitingRoomDentistDto
            {
                DoctorId = dentist.Id,
                Name = dentist.FullName,
                WorksTwoChairs = dentist.WorksTwoChairs,
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomDentistDto>.Failure(ErrorMessages.Generic, ex);
        }
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
