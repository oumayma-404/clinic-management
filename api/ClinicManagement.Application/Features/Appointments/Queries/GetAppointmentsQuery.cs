using ClinicManagement.Application.Common;
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
    /// With <see cref="DoctorId"/>: also keep the RDV that name NO dentist (cabinet-de-groupe D10). A « créneau
    /// occupé » with no dentist blocks every chair (L2b), so an agenda filtered to one dentist that hid it would
    /// offer a slot the save then refuses — and an old patient RDV booked with none would vanish from every view.
    /// Off by default, so the export and every other reader keep the strict filter.
    /// </summary>
    public bool IncludeUnassigned { get; set; }

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
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;

    public GetAppointmentsQueryHandler(
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

            // The window bounds the rows, so the unassigned ones are kept here rather than by a second query.
            var widened = request.IncludeUnassigned && request.DoctorId.HasValue;
            var appointments = await _appointmentRepository.GetByClinicIdAsync(
                clinicId,
                request.StartDate,
                request.EndDate,
                widened ? null : request.DoctorId,
                request.PatientId,
                cancellationToken);
            if (widened)
            {
                appointments = appointments
                    .Where(a => a.DoctorId == request.DoctorId || a.DoctorId == null)
                    .ToList();
            }

            // Which of these visits are already billed (AC-P6.13). One batched read for the whole window, not
            // one per row — and bounded by the window, so the agenda does not pull every appointment-linked
            // invoice the clinic has ever raised.
            // ⚠️ A séance retirée is gone from this read, and that is what makes « Supprimer (créé par erreur) »
            // mean anything: the mark already leaves the dashboard's figures, but a row the user was told they
            // deleted must also stop drawing itself on the agenda and in the patient's history.
            //
            // ⚠️ Filtered HERE and not in `AppointmentRepository.GetByClinicIdAsync`. That method also feeds
            // `AppointmentScheduling.FindCollisionAsync`, and the database's exclusion constraint holds the slot on
            // `Status NOT IN (5, 6)` — a retired séance is still `Scheduled`, so it still owns its slot in
            // PostgreSQL. Hiding it from collision detection would pass the app check and then fail the INSERT on a
            // raw constraint violation. Freeing the slot means changing the constraint, which is a migration.
            var appointmentList = appointments.Where(a => !a.IsDisregarded).ToList();
            var invoiceLinks = await AppointmentInvoiceLinks.ResolveAsync(
                _invoiceRepository, clinicId, appointmentList.Select(a => a.Id).ToList(), cancellationToken);

            // The practitioner's name comes from `DoctorId`; `DoctorName` is an unpopulated snapshot kept only
            // as a fallback for rows that carry one — see `AppointmentDoctorNames`.
            var roster = await AppointmentDoctorNames.ResolveRosterAsync(
                _doctorRepository, clinicId, cancellationToken);

            var dtos = appointmentList.Select(a => new AppointmentDto
            {
                Id = a.Id,
                ClinicId = a.ClinicId,
                PatientId = a.PatientId,
                PatientName = a.Patient?.GetFullName() ?? "Occupé",
                DoctorId = a.DoctorId,
                DoctorName = AppointmentDoctorNames.For(a.DoctorId, a.DoctorName, roster),
                AppointmentDateTime = a.AppointmentDateTime,
                Duration = a.Duration,
                Notes = a.Notes,
                Status = a.Status.ToString(),
                AllowedNextStatuses = Appointment.ManualNextStatusesFrom(a.Status).Select(s => s.ToString()).ToList(),
                ProcedureTypeId = a.ProcedureTypeId,
                ProcedureTypeName = a.LeadProcedureName(),
                ProcedureColorHex = a.ProcedureColorHex,
                Procedures = a.ToProcedureDtos(),
                TreatmentPlanItemId = a.TreatmentPlanItemId,
                InvoiceId = invoiceLinks.GetValueOrDefault(a.Id)?.InvoiceId,
                InvoiceNumber = invoiceLinks.GetValueOrDefault(a.Id)?.Number,
                LateArrivalMinutes = a.LateArrivalMinutes,
                CreatedAt = a.CreatedAt,
                Version = a.Version,
                IsSyncedToGoogle = a.GoogleCalendarEventId != null
            });

            return Result<IEnumerable<AppointmentDto>>.Success(dtos);
        }
        catch (Exception ex) when (ex is not ConflictException)
        {
            return Result<IEnumerable<AppointmentDto>>.Failure(ErrorMessages.Generic, ex);
        }
    }
}
