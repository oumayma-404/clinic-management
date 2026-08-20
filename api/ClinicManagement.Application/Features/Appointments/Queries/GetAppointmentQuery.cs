using MediatR;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

public class GetAppointmentQuery : IRequest<Result<AppointmentDto>>
{
    public Guid Id { get; set; }
}

public class GetAppointmentQueryHandler : IRequestHandler<GetAppointmentQuery, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;

    public GetAppointmentQueryHandler(
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        IUserRepository userRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext)
    {
        _appointmentRepository = appointmentRepository;
        _invoiceRepository = invoiceRepository;
        _userRepository = userRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<AppointmentDto>> Handle(GetAppointmentQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Resolve the caller's clinic from the DB (not just the JWT claim, which the global query
            // filter treats as fail-open when absent) so a cross-clinic id can't leak another clinic's
            // appointment/PHI. Mirrors GetAppointmentsQuery / UpdateAppointmentCommand.
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<AppointmentDto>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<AppointmentDto>.Failure("Utilisateur introuvable.");
            }

            var appointment = await _appointmentRepository.GetByIdAsync(request.Id, cancellationToken);

            // A missing appointment, or one belonging to another clinic, reads as "not found".
            if (appointment == null || appointment.ClinicId != user.ClinicId)
            {
                return Result<AppointmentDto>.Failure("Rendez-vous introuvable.");
            }

            // The note d'honoraires this visit is billed on, if any (AC-P6.13) — resolved through the same
            // helper the list read uses, so the two cannot disagree about which invoice counts.
            var invoiceLinks = await AppointmentInvoiceLinks.ResolveAsync(
                _invoiceRepository, user.ClinicId, new[] { appointment.Id }, cancellationToken);

            // Same helper, same reason: the practitioner's name is resolved from `DoctorId`, never read off the
            // `DoctorName` snapshot no write path fills.
            var roster = await AppointmentDoctorNames.ResolveRosterAsync(
                _doctorRepository, user.ClinicId, cancellationToken);

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                PatientName = appointment.Patient?.GetFullName() ?? "Occupé",
                DoctorId = appointment.DoctorId,
                AppointmentDateTime = appointment.AppointmentDateTime.Kind == DateTimeKind.Utc
                    ? appointment.AppointmentDateTime
                    : DateTime.SpecifyKind(appointment.AppointmentDateTime, DateTimeKind.Utc),
                Duration = appointment.Duration,
                DoctorName = AppointmentDoctorNames.For(
                    appointment.DoctorId, appointment.DoctorName, roster),
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                AllowedNextStatuses = Appointment.NextStatusesFrom(appointment.Status).Select(s => s.ToString()).ToList(),
                CreatedAt = appointment.CreatedAt.Kind == DateTimeKind.Utc
                    ? appointment.CreatedAt
                    : DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc),
                Version = appointment.Version,
                ProcedureTypeId = appointment.ProcedureTypeId,
                ProcedureTypeName = appointment.LeadProcedureName(),
                // Use current procedure type color if available, otherwise use stored color
                ProcedureColorHex = appointment.ProcedureType?.Color.Value ?? appointment.ProcedureColorHex,
                Procedures = appointment.ToProcedureDtos(),
                TreatmentPlanItemId = appointment.TreatmentPlanItemId,
                InvoiceId = invoiceLinks.GetValueOrDefault(appointment.Id)?.InvoiceId,
                InvoiceNumber = invoiceLinks.GetValueOrDefault(appointment.Id)?.Number,
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<AppointmentDto>.Failure($"Error retrieving appointment: {ex.Message}");
        }
    }
}


