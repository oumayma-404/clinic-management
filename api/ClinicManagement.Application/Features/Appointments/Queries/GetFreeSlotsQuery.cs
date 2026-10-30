using System.Globalization;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;
using MediatR;

namespace ClinicManagement.Application.Features.Appointments.Queries;

/// <summary>
/// Where a visit can move to: its practitioner's free starts for its own length, from <see cref="From"/> (default the
/// clinic's today) over <see cref="Days"/> days. The visit itself never blocks its own slot.
/// </summary>
public sealed class GetFreeSlotsQuery : IRequest<Result<FreeSlotsDto>>
{
    public Guid AppointmentId { get; init; }
    public DateOnly? From { get; init; }
    public int Days { get; init; } = 14;
}

public sealed class GetFreeSlotsQueryHandler : IRequestHandler<GetFreeSlotsQuery, Result<FreeSlotsDto>>
{
    private readonly IAppointmentRepository _appointments;
    private readonly IDoctorRepository _doctors;
    private readonly IClinicRepository _clinics;
    private readonly IDoctorLeaveRepository _leaves;
    private readonly ICurrentClinicResolver _clinicResolver;

    public GetFreeSlotsQueryHandler(
        IAppointmentRepository appointments,
        IDoctorRepository doctors,
        IClinicRepository clinics,
        IDoctorLeaveRepository leaves,
        ICurrentClinicResolver clinicResolver)
    {
        _appointments = appointments;
        _doctors = doctors;
        _clinics = clinics;
        _leaves = leaves;
        _clinicResolver = clinicResolver;
    }

    public async Task<Result<FreeSlotsDto>> Handle(GetFreeSlotsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var clinic = await _clinicResolver.GetClinicIdAsync(cancellationToken);
            if (clinic.IsFailure)
            {
                return Result<FreeSlotsDto>.FailureFrom(clinic);
            }

            var appointment = await _appointments.GetByIdAsync(request.AppointmentId, cancellationToken);
            if (appointment == null || appointment.ClinicId != clinic.Value)
            {
                return Result<FreeSlotsDto>.Failure("Rendez-vous introuvable.");
            }

            var nowUtc = DateTime.UtcNow;
            var from = request.From ?? DateOnly.FromDateTime(ClinicClock.ClinicToday(nowUtc));
            var days = await FreeSlotFinder.FindAsync(
                _appointments, _doctors, _clinics, _leaves,
                clinic.Value, appointment.DoctorId, appointment.Duration,
                from, request.Days, appointment.Id, nowUtc, cancellationToken);

            return Result<FreeSlotsDto>.Success(new FreeSlotsDto
            {
                Days = days.Select(day => new FreeDayDto
                {
                    Date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    State = day.State.ToString(),
                    Starts = day.Starts.Select(start => start.ToString("HH:mm", CultureInfo.InvariantCulture)).ToList(),
                }).ToList(),
            });
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<FreeSlotsDto>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
