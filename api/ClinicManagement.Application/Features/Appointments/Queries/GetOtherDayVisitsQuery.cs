using MediatR;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// « Qui arrive ? » for a patient with no RDV today (`salle-attente` round 2, C6): his next RDV on another day, so
/// reception can « Le recevoir aujourd'hui » instead of booking a second visit beside it — which is how he would have
/// come back on Thursday too. At most one per patient (the earliest still to come), for the few patients the search
/// is showing.
/// </summary>
public class GetOtherDayVisitsQuery : IRequest<Result<List<WaitingRoomOtherDayVisitDto>>>
{
    public List<Guid> PatientIds { get; set; } = new();
}

public class GetOtherDayVisitsQueryHandler : IRequestHandler<GetOtherDayVisitsQuery, Result<List<WaitingRoomOtherDayVisitDto>>>
{
    /// <summary>The search shows six; a little headroom, and a bound on one request's reads.</summary>
    public const int MaxPatients = 10;

    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetOtherDayVisitsQueryHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        ICurrentClinicResolver clinicResolver)
    {
        _appointments = appointments;
        _doctors = doctors;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<List<WaitingRoomOtherDayVisitDto>>> Handle(
        GetOtherDayVisitsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<List<WaitingRoomOtherDayVisitDto>>.Failure(clinic.Error ?? ErrorMessages.Generic);
            }

            var startOfTomorrow = ClinicClock.EndOfLocalDayUtc(ClinicClock.ClinicToday());
            var names = (await _doctors.GetByClinicIdAsync(clinic.Value, cancellationToken))
                .Where(d => d.ClinicId == clinic.Value)
                .ToDictionary(d => d.Id, d => d.FullName);

            var found = new List<WaitingRoomOtherDayVisitDto>();
            foreach (var patientId in request.PatientIds.Distinct().Take(MaxPatients))
            {
                var next = (await _appointments.GetByPatientIdAsync(patientId, cancellationToken))
                    .Where(a => a.ClinicId == clinic.Value
                                && !a.IsDisregarded
                                && a.Status is AppointmentStatus.Scheduled or AppointmentStatus.Confirmed
                                && a.AppointmentDateTime >= startOfTomorrow)
                    .MinBy(a => a.AppointmentDateTime);
                if (next is null)
                {
                    continue;
                }

                var acts = next.ToProcedureDtos().Select(p => p.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                found.Add(new WaitingRoomOtherDayVisitDto
                {
                    PatientId = patientId,
                    AppointmentId = next.Id,
                    AppointmentDateTime = next.AppointmentDateTime,
                    DoctorName = next.DoctorId is { } id && names.TryGetValue(id, out var name) ? name : next.DoctorName,
                    Acts = acts.Count == 0 ? null : string.Join(" + ", acts),
                });
            }

            return Result<List<WaitingRoomOtherDayVisitDto>>.Success(found);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<List<WaitingRoomOtherDayVisitDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
