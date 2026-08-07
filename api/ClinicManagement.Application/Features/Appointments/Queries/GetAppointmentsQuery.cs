using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Appointments.Queries;

public class GetAppointmentsQuery : IRequest<Result<IEnumerable<AppointmentDto>>>
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    /// <summary>Optional per-practitioner filter — only appointments assigned to this doctor.</summary>
    public Guid? DoctorId { get; set; }

    /// <summary>
    /// Optional per-patient filter — the patient page's own agenda. Cut in SQL, not in the browser: the client
    /// had been sending <c>?patientId=</c> since the page was written and nothing bound it, so the patient's
    /// « À compléter » section listed every undocumented visit in the clinic.
    /// </summary>
    public Guid? PatientId { get; set; }
}

public class GetAppointmentsQueryHandler : IRequestHandler<GetAppointmentsQuery, Result<IEnumerable<AppointmentDto>>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;

    public GetAppointmentsQueryHandler(
        IAppointmentRepository appointmentRepository,
        IInvoiceRepository invoiceRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext)
    {
        _appointmentRepository = appointmentRepository;
        _invoiceRepository = invoiceRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
    }

    public async Task<Result<IEnumerable<AppointmentDto>>> Handle(GetAppointmentsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<IEnumerable<AppointmentDto>>.Failure("Session invalide, veuillez vous reconnecter.");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<IEnumerable<AppointmentDto>>.Failure("Utilisateur introuvable.");
            }

            var clinicId = user.ClinicId;

            var appointments = await _appointmentRepository.GetByClinicIdAsync(
                clinicId,
                request.StartDate,
                request.EndDate,
                request.DoctorId,
                request.PatientId,
                cancellationToken);

            // Which of these visits are already billed (AC-P6.13). One batched read for the whole window, not
            // one per row — and bounded by the window, so the agenda does not pull every appointment-linked
            // invoice the clinic has ever raised.
            var appointmentList = appointments.ToList();
            var invoiceLinks = await AppointmentInvoiceLinks.ResolveAsync(
                _invoiceRepository, clinicId, appointmentList.Select(a => a.Id).ToList(), cancellationToken);

            var dtos = appointmentList.Select(a => new AppointmentDto
            {
                Id = a.Id,
                ClinicId = a.ClinicId,
                PatientId = a.PatientId,
                PatientName = a.Patient?.GetFullName() ?? "Occupé",
                DoctorId = a.DoctorId,
                DoctorName = a.DoctorName,
                AppointmentDateTime = a.AppointmentDateTime,
                Duration = a.Duration,
                Notes = a.Notes,
                Status = a.Status.ToString(),
                AllowedNextStatuses = Appointment.NextStatusesFrom(a.Status).Select(s => s.ToString()).ToList(),
                ProcedureTypeId = a.ProcedureTypeId,
                ProcedureTypeName = a.LeadProcedureName(),
                ProcedureColorHex = a.ProcedureColorHex,
                Procedures = a.ToProcedureDtos(),
                TreatmentPlanItemId = a.TreatmentPlanItemId,
                InvoiceId = invoiceLinks.GetValueOrDefault(a.Id)?.InvoiceId,
                InvoiceNumber = invoiceLinks.GetValueOrDefault(a.Id)?.Number,
                CreatedAt = a.CreatedAt,
                Version = a.Version,
                IsSyncedToGoogle = a.GoogleCalendarEventId != null
            });

            return Result<IEnumerable<AppointmentDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<AppointmentDto>>.Failure($"Error retrieving appointments: {ex.Message}");
        }
    }
}
