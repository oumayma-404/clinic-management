using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// The « Salle d'attente » board for today (`salle-attente` S2). Every lane comes back in its order and the
/// waiting lane in <see cref="WaitingRoomOrder"/>'s, so reception, the doctor's phone and the header pill read one
/// queue. <see cref="DoctorId"/> narrows the board to one chair — that dentist's patients plus, in a cabinet de groupe,
/// the unassigned ones any dentist may take.
/// </summary>
public class GetWaitingRoomQuery : IRequest<Result<WaitingRoomDto>>
{
    public Guid? DoctorId { get; set; }
}

public class GetWaitingRoomQueryHandler : IRequestHandler<GetWaitingRoomQuery, Result<WaitingRoomDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly ICurrentClinicResolver _clinicResolver;
    private readonly IClinicContext _clinicContext;

    public GetWaitingRoomQueryHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        ICurrentClinicResolver clinicResolver,
        IClinicContext clinicContext)
    {
        _appointments = appointments;
        _doctors = doctors;
        _clinicResolver = clinicResolver;
        _clinicContext = clinicContext;
    }

    public async Task<Result<WaitingRoomDto>> Handle(GetWaitingRoomQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<WaitingRoomDto>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var day = await WaitingRoomDay.LoadAsync(clinic.Value, DateTime.UtcNow, _appointments, _doctors, cancellationToken);
            var caller = await WaitingRoomCaller.ResolveDentistAsync(_clinicContext, _doctors, day, cancellationToken);
            var names = day.Dentists.ToDictionary(d => d.Id, d => d.FullName);

            bool Shown(Appointment a) =>
                request.DoctorId is not { } only
                || day.ChairOf(a) == only
                || (day.IsGroupPractice && a.DoctorId is null);

            var visits = day.Visits.Where(Shown).ToList();
            var waiting = WaitingRoomOrder.Order(visits.Where(a => a.WaitingRoomLane == WaitingRoomLane.Waiting));

            var dto = new WaitingRoomDto
            {
                ServerNowUtc = day.NowUtc,
                Dentists = day.Dentists.Select(d => new WaitingRoomDentistDto { DoctorId = d.Id, Name = d.FullName }).ToList(),
                CallerDoctorId = caller?.Id,
                CanCallNext = caller is not null,
                Upcoming = visits
                    .Where(a => a.WaitingRoomLane == WaitingRoomLane.Upcoming)
                    .OrderBy(a => a.AppointmentDateTime).ThenBy(a => a.Id)
                    .Select(a => ToVisit(a, day, names, null))
                    .ToList(),
                Waiting = waiting.Select((a, i) => ToVisit(a, day, names, i + 1)).ToList(),
                InChair = visits
                    .Where(a => a.WaitingRoomLane == WaitingRoomLane.InChair)
                    .OrderBy(a => a.CalledInAtUtc).ThenBy(a => a.Id)
                    .Select(a => ToVisit(a, day, names, null))
                    .ToList(),
                Done = visits
                    .Where(a => a.WaitingRoomLane is WaitingRoomLane.Done or WaitingRoomLane.Left)
                    .OrderByDescending(EndedAt).ThenBy(a => a.Id)
                    .Select(a => ToVisit(a, day, names, null))
                    .ToList(),
            };

            return Result<WaitingRoomDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<WaitingRoomDto>.Failure(ErrorMessages.Generic, ex);
        }
    }

    /// <summary>When a finished card finished — its chair time, its departure, or (a fiche with no board) its last write.</summary>
    private static DateTime EndedAt(Appointment a) =>
        a.LeftChairAtUtc ?? a.CancelledAt ?? a.UpdatedAt ?? a.AppointmentDateTime;

    private static WaitingRoomVisitDto ToVisit(
        Appointment a, WaitingRoomDay day, IReadOnlyDictionary<Guid, string> names, int? position)
    {
        var acts = a.ToProcedureDtos().Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return new WaitingRoomVisitDto
        {
            AppointmentId = a.Id,
            PatientId = a.PatientId!.Value,
            PatientName = WaitingRoomDay.NameOf(a),
            DoctorId = a.DoctorId,
            DoctorName = a.DoctorId is { } id && names.TryGetValue(id, out var name) ? name : a.DoctorName,
            ChairDoctorId = day.ChairOf(a),
            AppointmentDateTime = a.AppointmentDateTime,
            DurationMinutes = (int)a.Duration.TotalMinutes,
            Lane = a.WaitingRoomLane.ToString(),
            Status = a.Status.ToString(),
            IsWalkIn = a.IsWalkIn,
            ArrivedAtUtc = a.ArrivedAtUtc,
            CalledInAtUtc = a.CalledInAtUtc,
            LeftChairAtUtc = a.LeftChairAtUtc,
            LeftAtUtc = a.WaitingRoomLane == WaitingRoomLane.Left ? a.CancelledAt : null,
            Acts = acts.Count == 0 ? null : string.Join(" + ", acts),
            Position = position,
        };
    }
}
