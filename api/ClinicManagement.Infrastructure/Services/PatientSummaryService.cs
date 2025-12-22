using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

public class PatientSummaryService : IPatientSummaryService
{
    private readonly ILogger<PatientSummaryService> _logger;

    public PatientSummaryService(ILogger<PatientSummaryService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GenerateSummaryAsync(Patient patient, Appointment appointment, CancellationToken cancellationToken = default)
    {
        // TODO: Integrate with actual AI service (OpenAI, Azure OpenAI, etc.)
        // For now, this is a placeholder that generates a basic summary

        var summary = $@"
PATIENT SUMMARY - {patient.GetFullName()}
Appointment: {appointment.AppointmentDateTime:yyyy-MM-dd HH:mm}
Doctor: {appointment.DoctorName ?? "TBD"}

PATIENT INFORMATION:
- Age: {CalculateAge(patient.DateOfBirth)}
- Gender: {patient.Gender}
- Email: {patient.Email.Value}
- Phone: {patient.PhoneNumber.Value}

MEDICAL HISTORY:
{patient.MedicalHistory ?? "No medical history recorded"}

ALLERGIES:
{patient.Allergies ?? "No known allergies"}

FLAGS:
{string.Join("\n", patient.Flags.Where(f => f.IsActive).Select(f => $"- {f.FlagType}: {f.Description}"))}

PREVIOUS APPOINTMENTS:
{string.Join("\n", patient.Appointments
    .Where(a => a.Status == AppointmentStatus.Completed)
    .OrderByDescending(a => a.AppointmentDateTime)
    .Take(5)
    .Select(a => $"- {a.AppointmentDateTime:yyyy-MM-dd}: {a.DoctorName ?? "N/A"}"))}
";

        _logger.LogInformation("Generated patient summary for {PatientId}", patient.Id);

        return await Task.FromResult(summary);
    }

    private int CalculateAge(DateTime dateOfBirth)
    {
        var today = DateTime.Today;
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age)) age--;
        return age;
    }
}



