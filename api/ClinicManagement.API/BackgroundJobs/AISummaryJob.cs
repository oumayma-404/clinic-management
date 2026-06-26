using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Logging;
using Hangfire;

namespace ClinicManagement.API.BackgroundJobs;

public class AISummaryJob
{
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IPatientSummaryService _summaryService;
    private readonly ILogger<AISummaryJob> _logger;

    public AISummaryJob(
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IPatientSummaryService summaryService,
        ILogger<AISummaryJob> logger)
    {
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _summaryService = summaryService;
        _logger = logger;
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task GenerateSummariesForUpcomingAppointments()
    {
        _logger.LogInformation("Generating AI summaries for upcoming appointments");

        // Get appointments in the next 15-30 minutes
        var now = DateTime.UtcNow;
        var startTime = now.AddMinutes(15);
        var endTime = now.AddMinutes(30);

        var upcomingAppointments = await _appointmentRepository.GetUpcomingAppointmentsAsync(startTime);
        var appointmentsToProcess = upcomingAppointments
            .Where(a => a.AppointmentDateTime >= startTime && a.AppointmentDateTime <= endTime)
            .ToList();

        foreach (var appointment in appointmentsToProcess)
        {
            try
            {
                // Skip busy slots (appointments without a patient)
                if (!appointment.PatientId.HasValue)
                {
                    _logger.LogDebug("Skipping AI summary generation for busy slot appointment {AppointmentId}", appointment.Id);
                    continue;
                }

                var patient = await _patientRepository.GetByIdWithAppointmentsAsync(appointment.PatientId.Value);
                if (patient == null)
                {
                    _logger.LogWarning("Patient not found for appointment {AppointmentId}", appointment.Id);
                    continue;
                }

                var summary = await _summaryService.GenerateSummaryAsync(patient, appointment);

                _logger.LogInformation("Generated summary for appointment {AppointmentId}: {Summary}", appointment.Id, summary);

                // Here you could store the summary or send it to the doctor
                // For now, we'll just log it
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating summary for appointment {AppointmentId}", appointment.Id);
            }
        }

        _logger.LogInformation("Finished generating AI summaries");
    }
}

