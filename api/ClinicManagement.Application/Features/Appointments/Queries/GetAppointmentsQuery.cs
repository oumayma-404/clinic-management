using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

public class GetAppointmentsQuery : IRequest<Result<IEnumerable<AppointmentDto>>>
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public Guid? PatientId { get; set; }
    public string? DoctorName { get; set; }
}

public class GetAppointmentsQueryHandler : IRequestHandler<GetAppointmentsQuery, Result<IEnumerable<AppointmentDto>>>
{
    private readonly IAppointmentRepository _appointmentRepository;

    public GetAppointmentsQueryHandler(IAppointmentRepository appointmentRepository)
    {
        _appointmentRepository = appointmentRepository;
    }

    public async Task<Result<IEnumerable<AppointmentDto>>> Handle(GetAppointmentsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            DateTime? normalizedStartDate = null;
            if (request.StartDate.HasValue)
            {
                var startDate = request.StartDate.Value;
                if (startDate.Kind == DateTimeKind.Unspecified)
                {
                    normalizedStartDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);
                }
                else if (startDate.Kind == DateTimeKind.Local)
                {
                    normalizedStartDate = startDate.ToUniversalTime();
                }
                else
                {
                    normalizedStartDate = startDate;
                }
            }

            DateTime? normalizedEndDate = null;
            if (request.EndDate.HasValue)
            {
                var endDate = request.EndDate.Value;
                if (endDate.Kind == DateTimeKind.Unspecified)
                {
                    normalizedEndDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);
                }
                else if (endDate.Kind == DateTimeKind.Local)
                {
                    normalizedEndDate = endDate.ToUniversalTime();
                }
                else
                {
                    normalizedEndDate = endDate;
                }
            }

            IEnumerable<Domain.Entities.Appointment> appointments;

            if (request.PatientId.HasValue)
            {
                appointments = await _appointmentRepository.GetByPatientIdAsync(request.PatientId.Value, cancellationToken);
            }
            else if (normalizedStartDate.HasValue && normalizedEndDate.HasValue)
            {
                var allAppointments = new List<Domain.Entities.Appointment>();
                var currentDate = normalizedStartDate.Value.Date;
                var endDateOnly = normalizedEndDate.Value.Date;

                while (currentDate <= endDateOnly)
                {
                    var dayAppointments = await _appointmentRepository.GetAppointmentsForDateAsync(currentDate, cancellationToken);
                    allAppointments.AddRange(dayAppointments);
                    currentDate = currentDate.AddDays(1);
                }

                appointments = allAppointments
                    .Where(a => a.AppointmentDateTime >= normalizedStartDate.Value &&
                               a.AppointmentDateTime <= normalizedEndDate.Value);
            }
            else if (normalizedStartDate.HasValue)
            {
                appointments = await _appointmentRepository.GetUpcomingAppointmentsAsync(normalizedStartDate.Value, cancellationToken);
            }
            else
            {
                appointments = await _appointmentRepository.GetUpcomingAppointmentsAsync(DateTime.UtcNow, cancellationToken);
            }

            // Filter by doctor name if provided
            if (!string.IsNullOrWhiteSpace(request.DoctorName))
            {
                appointments = appointments.Where(a => 
                    !string.IsNullOrWhiteSpace(a.DoctorName) && 
                    a.DoctorName.Contains(request.DoctorName!, StringComparison.OrdinalIgnoreCase));
            }

            var dtos = appointments.Select(a => new AppointmentDto
            {
                Id = a.Id,
                PatientId = a.PatientId,
                PatientName = a.Patient.GetFullName(),
                AppointmentDateTime = a.AppointmentDateTime.Kind == DateTimeKind.Utc 
                    ? a.AppointmentDateTime 
                    : DateTime.SpecifyKind(a.AppointmentDateTime, DateTimeKind.Utc),
                Duration = a.Duration,
                DoctorName = a.DoctorName,
                Notes = a.Notes,
                Status = a.Status.ToString(),
                CreatedAt = a.CreatedAt.Kind == DateTimeKind.Utc 
                    ? a.CreatedAt 
                    : DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc)
            }).OrderBy(a => a.AppointmentDateTime);

            return Result<IEnumerable<AppointmentDto>>.Success(dtos);
        }
        catch (Exception ex)
        {
            return Result<IEnumerable<AppointmentDto>>.Failure($"Error retrieving appointments: {ex.Message}");
        }
    }
}


