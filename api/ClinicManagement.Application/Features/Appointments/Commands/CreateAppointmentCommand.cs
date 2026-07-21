using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Appointments.Commands;

public class CreateAppointmentCommand : IRequest<Result<AppointmentDto>>
{
    public Guid? PatientId { get; set; }
    public string? DoctorId { get; set; }
    public DateTime AppointmentDateTime { get; set; }
    public int DurationMinutes { get; set; }
    public string? DoctorName { get; set; }
    public string? Notes { get; set; }
    public Guid? ProcedureTypeId { get; set; }
}

public class CreateAppointmentCommandHandler : IRequestHandler<CreateAppointmentCommand, Result<AppointmentDto>>
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IReminderScheduler _reminderScheduler;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public CreateAppointmentCommandHandler(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IUserRepository userRepository,
        IClinicContext clinicContext,
        IUnitOfWork unitOfWork,
        INotificationGenerator notificationGenerator,
        IReminderScheduler reminderScheduler,
        IServiceScopeFactory serviceScopeFactory)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _userRepository = userRepository;
        _clinicContext = clinicContext;
        _unitOfWork = unitOfWork;
        _notificationGenerator = notificationGenerator;
        _reminderScheduler = reminderScheduler;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task<Result<AppointmentDto>> Handle(CreateAppointmentCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Get user ID from token
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Result<AppointmentDto>.Failure("User ID not found in token");
            }

            // Get user from database to get clinic ID
            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return Result<AppointmentDto>.Failure("User not found");
            }

            var clinicId = user.ClinicId;

            // If patient is provided, verify it exists
            Patient? patient = null;
            if (request.PatientId.HasValue)
            {
                patient = await _patientRepository.GetByIdAsync(request.PatientId.Value, cancellationToken);
                if (patient == null || patient.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Patient not found");
                }
            }

            // Get procedure type if specified
            Guid? procedureTypeId = request.ProcedureTypeId;
            int? procedureDurationMinutes = null;
            string? procedureColorHex = null;
            string? procedureTypeName = null;

            if (procedureTypeId.HasValue)
            {
                var procedureType = await _procedureTypeRepository.GetByIdAsync(procedureTypeId.Value, cancellationToken);
                if (procedureType == null || procedureType.ClinicId != clinicId)
                {
                    return Result<AppointmentDto>.Failure("Procedure type not found");
                }
                if (!procedureType.IsActive)
                {
                    return Result<AppointmentDto>.Failure("Selected procedure type is not active");
                }
                procedureDurationMinutes = procedureType.DefaultDurationMinutes;
                procedureColorHex = procedureType.Color.Value;
                procedureTypeName = procedureType.Name;
                // Use procedure duration if not specified
                if (request.DurationMinutes == 0)
                {
                    request.DurationMinutes = procedureType.DefaultDurationMinutes;
                }
            }

            var duration = TimeSpan.FromMinutes(request.DurationMinutes);
            var appointment = new Appointment(
                Guid.NewGuid(),
                clinicId,
                request.PatientId,
                request.DoctorId,
                request.AppointmentDateTime,
                duration,
                request.DoctorName,
                request.Notes,
                null, // recurringAppointmentId
                procedureTypeId,
                procedureDurationMinutes,
                procedureColorHex);

            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Real-time "appointments changed" is broadcast centrally by RealtimeBroadcastBehavior after
            // this command returns success (i.e. after the commit above) — no per-handler broadcast here.

            // In-app staff notifications (best-effort, never fails this command). Only for real patient
            // appointments — patient-less "busy slot" appointments generate nothing (spec US-2). The creator
            // is actor-excluded from the "created" notification; the ~24h reminder is visible to all staff.
            if (patient != null)
            {
                var patientName = patient.GetFullName();
                await _notificationGenerator.AppointmentCreatedAsync(
                    clinicId, appointment.Id, userId, patientName, appointment.AppointmentDateTime, cancellationToken);
                await _notificationGenerator.ScheduleAppointmentReminderAsync(
                    clinicId, appointment.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
                // Post-visit review: becomes visible at the appointment end (start + duration), targeted at
                // the linked doctor if any (else all staff). Deferred visibility replaces a background job.
                await _notificationGenerator.EnsurePostVisitReviewAsync(
                    clinicId, appointment.Id, appointment.DoctorId, patientName,
                    appointment.AppointmentDateTime + appointment.Duration, cancellationToken);

                // Outbound SMS/WhatsApp reminder(s): enqueued to the Notification outbox per configured
                // channel, sent later by the connectivity-gated dispatcher. Best-effort, never fails create.
                await _reminderScheduler.ScheduleForAppointmentAsync(
                    clinicId, appointment.Id, patient.Id, patientName, appointment.AppointmentDateTime, cancellationToken);
            }

            var dto = new AppointmentDto
            {
                Id = appointment.Id,
                ClinicId = appointment.ClinicId,
                PatientId = appointment.PatientId,
                PatientName = patient?.GetFullName() ?? "Occupé",
                DoctorId = appointment.DoctorId,
                DoctorName = appointment.DoctorName,
                AppointmentDateTime = appointment.AppointmentDateTime,
                Duration = appointment.Duration,
                Notes = appointment.Notes,
                Status = appointment.Status.ToString(),
                ProcedureTypeId = appointment.ProcedureTypeId,
                ProcedureTypeName = procedureTypeName,
                ProcedureColorHex = appointment.ProcedureColorHex,
                CreatedAt = appointment.CreatedAt,
                IsSyncedToGoogle = appointment.GoogleCalendarEventId != null
            };

            // Sync to Google Calendar post-commit (fire-and-forget), mirroring the update path so a newly
            // created appointment is pushed to Google without a manual "Push". A fresh DI scope avoids
            // DbContext-disposal issues once the request scope ends; failures never affect the create.
            // Patient-less "busy slot" appointments are skipped inside the sync service.
            var appointmentId = appointment.Id;
            _ = Task.Run(async () =>
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<CreateAppointmentCommandHandler>>();

                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

                    // Gate on the SERVER's internet egress (Local LAN clients have none of their own). Skip the
                    // sync — and its OAuth token refresh — when offline; connectivity returning re-enables it on
                    // the next create/update. IInternetProbe caches, so this is cheap.
                    var probe = scope.ServiceProvider.GetRequiredService<IInternetProbe>();
                    if (!await probe.IsInternetReachableAsync(cts.Token))
                    {
                        logger.LogDebug("Server offline; skipping Google Calendar sync for appointment {AppointmentId}", appointmentId);
                        return;
                    }

                    var syncService = scope.ServiceProvider.GetRequiredService<IGoogleCalendarSyncService>();
                    await syncService.SyncAppointmentToGoogleCalendarAsync(appointmentId, cts.Token);
                    logger.LogInformation("Successfully synced appointment {AppointmentId} to Google Calendar", appointmentId);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
                {
                    logger.LogDebug("Google Calendar not configured, skipping sync for appointment {AppointmentId}", appointmentId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error syncing appointment {AppointmentId} to Google Calendar", appointmentId);
                }
            });

            return Result<AppointmentDto>.Success(dto);
        }
        catch (Exception ex)
        {
            return Result<AppointmentDto>.Failure($"Error creating appointment: {ex.Message}");
        }
    }
}
