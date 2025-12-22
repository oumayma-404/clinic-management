using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

public class GetAppointmentQuery : IRequest<Result<AppointmentDto>>
{
    public Guid Id { get; set; }
}

public class GetAppointmentQueryHandler : IRequestHandler<GetAppointmentQuery, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;

    public GetAppointmentQueryHandler(IAppointmentRepository appointmentRepository)
    {
        _appointmentRepository = appointmentRepository;
    }

    public async Task<Result<AppointmentDto>> Handle(GetAppointmentQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var appointment = await _appointmentRepository.GetByIdAsync(request.Id, cancellationToken);

            if (appointment == null)
            {
                return Result<AppointmentDto>.Failure("Appointment not found");
            }

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                PatientId = appointment.PatientId,
                PatientName = appointment.Patient.GetFullName(),
                AppointmentDateTime = appointment.AppointmentDateTime.Kind == DateTimeKind.Utc
                    ? appointment.AppointmentDateTime
                    : DateTime.SpecifyKind(appointment.AppointmentDateTime, DateTimeKind.Utc),
                Duration = appointment.Duration,
                DoctorName = appointment.DoctorName,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                CreatedAt = appointment.CreatedAt.Kind == DateTimeKind.Utc
                    ? appointment.CreatedAt
                    : DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc)
            };

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error retrieving appointment: {ex.Message}");
        }
    }
}

