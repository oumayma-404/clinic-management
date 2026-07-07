using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

public class GoogleCalendarSyncService : IGoogleCalendarSyncService
{
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GoogleCalendarSyncService> _logger;

    public GoogleCalendarSyncService(
        IGoogleCalendarService googleCalendarService,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IUnitOfWork unitOfWork,
        ILogger<GoogleCalendarSyncService> logger)
    {
        _googleCalendarService = googleCalendarService;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task SyncAppointmentToGoogleCalendarAsync(
        Guid appointmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Syncing appointment {AppointmentId} to Google Calendar", appointmentId);
            
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, cancellationToken);
            if (appointment == null)
            {
                _logger.LogWarning("Appointment {AppointmentId} not found for Google Calendar sync", appointmentId);
                return;
            }
            
            _logger.LogDebug("Appointment found: Patient={PatientName}, DateTime={DateTime}, Status={Status}, GoogleEventId={GoogleEventId}",
                appointment.Patient?.GetFullName() ?? "Occupé", appointment.AppointmentDateTime, appointment.Status, appointment.GoogleCalendarEventId);

            // Handle cancelled and completed appointments - delete from Google Calendar
            if (appointment.Status == AppointmentStatus.Cancelled || appointment.Status == AppointmentStatus.Completed)
            {
                _logger.LogInformation("Appointment {AppointmentId} is {Status}. Checking if it needs to be deleted from Google Calendar. GoogleEventId: {GoogleEventId}", 
                    appointmentId, appointment.Status, appointment.GoogleCalendarEventId ?? "(none)");
                
                // Delete from Google Calendar if it exists
                if (!string.IsNullOrEmpty(appointment.GoogleCalendarEventId))
                {
                    try
                    {
                        _logger.LogInformation("Deleting Google Calendar event {EventId} for {Status} appointment {AppointmentId}", 
                            appointment.GoogleCalendarEventId, appointment.Status, appointmentId);
                        
                        await _googleCalendarService.DeleteEventAsync(appointment.GoogleCalendarEventId, cancellationToken);
                        
                        _logger.LogInformation("Successfully deleted Google Calendar event {EventId} for appointment {AppointmentId}", 
                            appointment.GoogleCalendarEventId, appointmentId);
                        
                        // Clear the Google Calendar event ID from the appointment
                        appointment.SetGoogleCalendarEventId(null);
                        await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        
                        _logger.LogInformation("Cleared GoogleCalendarEventId from appointment {AppointmentId}", appointmentId);
                    }
                    catch (Google.GoogleApiException gex) when (gex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // Event doesn't exist in Google Calendar (might have been deleted manually)
                        _logger.LogWarning("Google Calendar event {EventId} not found (may have been deleted manually). Clearing reference from appointment {AppointmentId}", 
                            appointment.GoogleCalendarEventId, appointmentId);
                        
                        appointment.SetGoogleCalendarEventId(null);
                        await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error deleting Google Calendar event {EventId} for appointment {AppointmentId}. Error: {ErrorMessage}", 
                            appointment.GoogleCalendarEventId, appointmentId, ex.Message);
                        // Don't throw - we don't want to fail the appointment cancellation if Google Calendar sync fails
                    }
                }
                else
                {
                    _logger.LogDebug("Appointment {AppointmentId} is {Status} but has no GoogleCalendarEventId. Nothing to delete from Google Calendar.", 
                        appointmentId, appointment.Status);
                }
                return;
            }

            // Skip Google Calendar sync for busy slots (appointments without a patient)
            if (!appointment.PatientId.HasValue)
            {
                _logger.LogInformation("Skipping Google Calendar sync for busy slot appointment {AppointmentId} (no patient assigned)", appointmentId);
                return;
            }

            // Ensure patient is loaded
            Patient? patient = appointment.Patient;
            if (patient == null)
            {
                _logger.LogWarning("Patient not loaded for appointment {AppointmentId}. Loading patient...", appointmentId);
                var patientFromDb = await _patientRepository.GetByIdAsync(appointment.PatientId.Value, cancellationToken);
                if (patientFromDb == null)
                {
                    _logger.LogError("Patient {PatientId} not found for appointment {AppointmentId}", appointment.PatientId, appointmentId);
                    return;
                }
                patient = patientFromDb;
            }
            var summary = $"Appointment: {patient.GetFullName()}";
            var description = BuildAppointmentDescription(appointment);
            
            // Normalize dates to UTC
            var startDateTime = appointment.AppointmentDateTime;
            if (startDateTime.Kind == DateTimeKind.Unspecified)
            {
                startDateTime = DateTime.SpecifyKind(startDateTime, DateTimeKind.Utc);
            }
            else if (startDateTime.Kind == DateTimeKind.Local)
            {
                startDateTime = startDateTime.ToUniversalTime();
            }
            
            var endDateTime = startDateTime.Add(appointment.Duration);
            if (endDateTime.Kind == DateTimeKind.Unspecified)
            {
                endDateTime = DateTime.SpecifyKind(endDateTime, DateTimeKind.Utc);
            }
            else if (endDateTime.Kind == DateTimeKind.Local)
            {
                endDateTime = endDateTime.ToUniversalTime();
            }
            
            var location = appointment.DoctorName != null ? $"Doctor: {appointment.DoctorName}" : null;
            
            _logger.LogDebug("Syncing appointment to Google Calendar: Summary={Summary}, Start={StartDateTime}, End={EndDateTime}, Location={Location}",
                summary, startDateTime, endDateTime, location);

            if (string.IsNullOrEmpty(appointment.GoogleCalendarEventId))
            {
                // Create new event
                _logger.LogInformation("Creating new Google Calendar event for appointment {AppointmentId}", appointmentId);
                var eventId = await _googleCalendarService.CreateEventAsync(
                    summary,
                    description,
                    startDateTime,
                    endDateTime,
                    location,
                    cancellationToken);

                if (string.IsNullOrEmpty(eventId))
                {
                    _logger.LogError("Failed to create Google Calendar event for appointment {AppointmentId}. EventId is null or empty.", appointmentId);
                    return;
                }

                appointment.SetGoogleCalendarEventId(eventId);
                await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Successfully created Google Calendar event {EventId} for appointment {AppointmentId}", eventId, appointmentId);
            }
            else
            {
                // Update existing event
                _logger.LogInformation("Updating existing Google Calendar event {EventId} for appointment {AppointmentId}", appointment.GoogleCalendarEventId, appointmentId);
                await _googleCalendarService.UpdateEventAsync(
                    appointment.GoogleCalendarEventId,
                    summary,
                    description,
                    startDateTime,
                    endDateTime,
                    location,
                    cancellationToken);

                _logger.LogInformation("Successfully updated Google Calendar event {EventId} for appointment {AppointmentId}", appointment.GoogleCalendarEventId, appointmentId);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            _logger.LogWarning("Google Calendar is not configured. Skipping sync for appointment {AppointmentId}", appointmentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing appointment {AppointmentId} to Google Calendar", appointmentId);
            throw;
        }
    }

    public async Task SyncGoogleCalendarToAppointmentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting sync from Google Calendar to appointments");
            
            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow.AddDays(90);
            _logger.LogInformation("Fetching events from {StartDate} to {EndDate}", startDate, endDate);
            
            var googleEvents = await _googleCalendarService.GetEventsAsync(
                startDate: startDate,
                endDate: endDate,
                cancellationToken);

            var eventList = googleEvents.ToList();
            _logger.LogInformation("Retrieved {Count} events from Google Calendar", eventList.Count);
            
            if (eventList.Count > 0)
            {
                _logger.LogInformation("Sample events: {Events}", 
                    string.Join(", ", eventList.Take(3).Select(e => $"'{e.Summary}' ({e.StartDateTime:yyyy-MM-dd HH:mm})")));
            }

            var allAppointments = await _appointmentRepository.GetAllAsync(cancellationToken);
            _logger.LogInformation("Retrieved {Count} appointments from database", allAppointments.Count());
            
            var appointmentsByGoogleId = allAppointments
                .Where(a => !string.IsNullOrEmpty(a.GoogleCalendarEventId))
                .ToDictionary(a => a.GoogleCalendarEventId!, a => a);

            _logger.LogInformation("Found {Count} appointments already linked to Google Calendar events", appointmentsByGoogleId.Count);

            int updatedCount = 0;
            int linkedCount = 0;
            int createdCount = 0;

            foreach (var googleEvent in eventList)
            {
                _logger.LogDebug("Processing Google Calendar event: {EventId} - {Summary} at {StartTime}", 
                    googleEvent.Id, googleEvent.Summary, googleEvent.StartDateTime);

                // Skip if we already have this event synced
                if (appointmentsByGoogleId.ContainsKey(googleEvent.Id))
                {
                    var existingAppointment = appointmentsByGoogleId[googleEvent.Id];
                    
                    // Check if Google event was updated more recently
                    if (googleEvent.Updated.HasValue && 
                        existingAppointment.UpdatedAt.HasValue &&
                        googleEvent.Updated.Value > existingAppointment.UpdatedAt.Value)
                    {
                        // Update appointment from Google Calendar
                        await UpdateAppointmentFromGoogleEventAsync(existingAppointment, googleEvent, cancellationToken);
                        updatedCount++;
                        _logger.LogInformation("Updated appointment {AppointmentId} from Google Calendar event {EventId}", 
                            existingAppointment.Id, googleEvent.Id);
                    }
                    continue;
                }

                // Try to match by patient name and time (for events created in Google Calendar)
                var patientName = ExtractPatientNameFromSummary(googleEvent.Summary);
                if (!string.IsNullOrEmpty(patientName))
                {
                    _logger.LogDebug("Extracted patient name from event: {PatientName}", patientName);
                    
                    var matchingAppointment = allAppointments
                        .FirstOrDefault(a => 
                            a.PatientId.HasValue &&
                            a.Patient != null &&
                            a.Patient.GetFullName().Equals(patientName, StringComparison.OrdinalIgnoreCase) &&
                            Math.Abs((a.AppointmentDateTime - googleEvent.StartDateTime).TotalMinutes) < 30);

                    if (matchingAppointment != null && string.IsNullOrEmpty(matchingAppointment.GoogleCalendarEventId))
                    {
                        // Link existing appointment to Google Calendar event
                        matchingAppointment.SetGoogleCalendarEventId(googleEvent.Id);
                        await _appointmentRepository.UpdateAsync(matchingAppointment, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        linkedCount++;
                        _logger.LogInformation("Linked appointment {AppointmentId} to Google Calendar event {EventId}", 
                            matchingAppointment.Id, googleEvent.Id);
                        continue;
                    }
                }

                // Create new appointment from Google Calendar event (if it looks like a clinic appointment)
                if (IsClinicAppointment(googleEvent))
                {
                    _logger.LogDebug("Event looks like a clinic appointment, creating new appointment");
                    var created = await CreateAppointmentFromGoogleEventAsync(googleEvent, cancellationToken);
                    if (created)
                    {
                        createdCount++;
                    }
                }
                else
                {
                    _logger.LogDebug("Event does not match clinic appointment pattern, skipping: {Summary}", googleEvent.Summary);
                }
            }

            _logger.LogInformation("Sync completed: {Updated} updated, {Linked} linked, {Created} created", 
                updatedCount, linkedCount, createdCount);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured"))
        {
            _logger.LogWarning("Google Calendar is not configured. Skipping sync from Google Calendar");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing from Google Calendar to appointments");
            throw;
        }
    }

    private string BuildAppointmentDescription(Appointment appointment)
    {
        var parts = new List<string>();
        
        if (!string.IsNullOrEmpty(appointment.DoctorName))
        {
            parts.Add($"Doctor: {appointment.DoctorName}");
        }

        if (!string.IsNullOrEmpty(appointment.Notes))
        {
            parts.Add($"Notes: {appointment.Notes}");
        }

        parts.Add($"Status: {appointment.Status}");
        if (appointment.PatientId.HasValue)
        {
            parts.Add($"Patient ID: {appointment.PatientId.Value}");
        }
        else
        {
            parts.Add("Busy Slot - No Patient");
        }

        return string.Join("\n", parts);
    }

    private string? ExtractPatientNameFromSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        // Format 1: "Appointment: John Doe"
        if (summary.StartsWith("Appointment: ", StringComparison.OrdinalIgnoreCase))
        {
            return summary.Substring("Appointment: ".Length).Trim();
        }

        // Format 2: "John Doe - Appointment" or "John Doe Appointment"
        var patterns = new[]
        {
            @"^(.+?)\s*-\s*appointment",
            @"^(.+?)\s+appointment",
            @"appointment:\s*(.+?)$"
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                summary, 
                pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success && match.Groups.Count > 1)
            {
                var name = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        // If no pattern matches but summary is short and looks like a name, return it
        // (for cases where someone just puts a patient name as the event title)
        if (summary.Length < 100 && !summary.Contains("meeting") && !summary.Contains("call"))
        {
            // Check if it looks like a name (has at least one space, suggesting first and last name)
            var parts = summary.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts.Length <= 4)
            {
                return summary.Trim();
            }
        }

        return null;
    }

    private bool IsClinicAppointment(GoogleCalendarEvent googleEvent)
    {
        // If summary is empty, skip
        if (string.IsNullOrWhiteSpace(googleEvent.Summary))
        {
            return false;
        }

        var summary = googleEvent.Summary.ToLowerInvariant();
        var description = googleEvent.Description?.ToLowerInvariant() ?? string.Empty;
        
        // Check if event summary contains clinic-related keywords
        // This helps filter out personal events that aren't clinic appointments
        var hasClinicKeywords = summary.Contains("appointment") || 
                               summary.Contains("patient") || 
                               summary.Contains("doctor") ||
                               summary.Contains("clinic") ||
                               summary.Contains("consultation") ||
                               summary.Contains("visit") ||
                               description.Contains("patient id") ||
                               description.Contains("doctor:");

        // Also accept events that start with "Appointment: " (our format)
        var isOurFormat = summary.StartsWith("appointment: ");

        return hasClinicKeywords || isOurFormat;
    }

    private async Task UpdateAppointmentFromGoogleEventAsync(
        Appointment appointment,
        GoogleCalendarEvent googleEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = false;

            // Update appointment time if changed
            if (appointment.AppointmentDateTime != googleEvent.StartDateTime)
            {
                appointment.Reschedule(googleEvent.StartDateTime);
                updated = true;
            }

            // Update duration if changed
            var newDuration = googleEvent.EndDateTime - googleEvent.StartDateTime;
            if (appointment.Duration != newDuration)
            {
                appointment.UpdateDuration(newDuration);
                updated = true;
            }

            // Update notes if changed
            if (appointment.Notes != googleEvent.Description)
            {
                appointment.UpdateNotes(googleEvent.Description);
                updated = true;
            }

            if (updated)
            {
                await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Updated appointment {AppointmentId} from Google Calendar event {EventId}", appointment.Id, googleEvent.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating appointment {AppointmentId} from Google Calendar event", appointment.Id);
        }
    }

    private async Task<bool> CreateAppointmentFromGoogleEventAsync(
        GoogleCalendarEvent googleEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var patientName = ExtractPatientNameFromSummary(googleEvent.Summary);
            
            // If we can't extract patient name from summary, try to extract from description
            if (string.IsNullOrEmpty(patientName) && !string.IsNullOrEmpty(googleEvent.Description))
            {
                // Try to find "Patient ID: ..." or similar in description
                var patientIdMatch = System.Text.RegularExpressions.Regex.Match(
                    googleEvent.Description, 
                    @"Patient ID:\s*([a-f0-9-]{36})", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                if (patientIdMatch.Success && Guid.TryParse(patientIdMatch.Groups[1].Value, out var patientId))
                {
                    var patientById = await _patientRepository.GetByIdAsync(patientId, cancellationToken);
                    if (patientById != null)
                    {
                        patientName = patientById.GetFullName();
                        _logger.LogInformation("Found patient by ID from Google Calendar event description: {PatientName}", patientName);
                    }
                }
            }
            
            if (string.IsNullOrEmpty(patientName))
            {
                _logger.LogWarning("Cannot create appointment from Google Calendar event {EventId}: patient name not found in summary '{Summary}' or description", 
                    googleEvent.Id, googleEvent.Summary);
                return false;
            }

            // Normalize patient name (trim, normalize spaces)
            patientName = System.Text.RegularExpressions.Regex.Replace(patientName.Trim(), @"\s+", " ");

            // Try to find existing patient by name (more flexible matching)
            var patients = await _patientRepository.GetAllAsync(cancellationToken);
            
            // Try exact match first (case-insensitive)
            var patient = patients.FirstOrDefault(p => 
                p.GetFullName().Trim().Equals(patientName, StringComparison.OrdinalIgnoreCase));

            // If not found, try matching first name + last name separately
            if (patient == null)
            {
                var nameParts = patientName.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (nameParts.Length >= 2)
                {
                    var firstName = nameParts[0].Trim();
                    var lastName = string.Join(" ", nameParts.Skip(1)).Trim();
                    
                    patient = patients.FirstOrDefault(p => 
                        p.FirstName.Trim().Equals(firstName, StringComparison.OrdinalIgnoreCase) &&
                        p.LastName.Trim().Equals(lastName, StringComparison.OrdinalIgnoreCase));
                }
            }

            // If still not found, try partial matching
            if (patient == null)
            {
                patient = patients.FirstOrDefault(p => 
                    p.GetFullName().Trim().Contains(patientName, StringComparison.OrdinalIgnoreCase) ||
                    patientName.Contains(p.GetFullName().Trim(), StringComparison.OrdinalIgnoreCase));
            }

            if (patient == null)
            {
                // Patient not found - create it automatically
                _logger.LogInformation("Patient '{PatientName}' not found. Creating new patient automatically from Google Calendar event {EventId}", 
                    patientName, googleEvent.Id);
                
                var nameParts = patientName.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
                string firstName;
                string lastName;
                
                if (nameParts.Length >= 2)
                {
                    firstName = nameParts[0].Trim();
                    lastName = string.Join(" ", nameParts.Skip(1)).Trim();
                }
                else if (nameParts.Length == 1)
                {
                    // If only one part, use it as last name
                    firstName = nameParts[0].Trim();
                    lastName = nameParts[0].Trim();
                }
                else
                {
                    _logger.LogWarning("Cannot extract patient name from '{PatientName}' for Google Calendar event {EventId}", 
                        patientName, googleEvent.Id);
                    return false;
                }

                // Create new patient with minimal required information
                // Ensure DateOfBirth is UTC
                var dateOfBirth = DateTime.UtcNow;
                if (dateOfBirth.Kind != DateTimeKind.Utc)
                {
                    dateOfBirth = DateTime.SpecifyKind(dateOfBirth, DateTimeKind.Utc);
                }
                
                // Get clinic ID from first existing patient, or skip if no patients exist
                var existingPatients = await _patientRepository.GetAllAsync(cancellationToken);
                var firstPatient = existingPatients.FirstOrDefault();
                if (firstPatient == null)
                {
                    _logger.LogWarning("Cannot create patient from Google Calendar sync: No existing patients found to determine clinic ID");
                    return false;
                }
                var clinicId = firstPatient.ClinicId;
                
                var newPatient = new Patient(
                    Guid.NewGuid(),
                    clinicId,
                    firstName,
                    lastName,
                    dateOfBirth,
                    "Unknown", // Default gender
                    new Email("unknown@example.com"), // Default email
                    new PhoneNumber("000-000-0000")); // Default phone

                await _patientRepository.AddAsync(newPatient, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                
                patient = newPatient;
                _logger.LogInformation("Created new patient: '{PatientName}' (ID: {PatientId}) from Google Calendar event {EventId}", 
                    patient.GetFullName(), patient.Id, googleEvent.Id);
            }
            else
            {
                _logger.LogInformation("Found matching patient: '{PatientName}' (ID: {PatientId}) for Google Calendar event {EventId}", 
                    patient.GetFullName(), patient.Id, googleEvent.Id);
            }

            var duration = googleEvent.EndDateTime - googleEvent.StartDateTime;
            if (duration <= TimeSpan.Zero)
            {
                _logger.LogWarning("Invalid duration for Google Calendar event {EventId}: {Duration}", googleEvent.Id, duration);
                duration = TimeSpan.FromMinutes(30); // Default to 30 minutes
            }

            // Normalize appointment date time to UTC
            var appointmentDateTime = googleEvent.StartDateTime;
            if (appointmentDateTime.Kind == DateTimeKind.Unspecified)
            {
                appointmentDateTime = DateTime.SpecifyKind(appointmentDateTime, DateTimeKind.Utc);
            }
            else if (appointmentDateTime.Kind == DateTimeKind.Local)
            {
                appointmentDateTime = appointmentDateTime.ToUniversalTime();
            }

            var appointment = new Appointment(
                Guid.NewGuid(),
                patient.ClinicId,
                patient.Id,
                null, // doctorId - extract from event if available
                appointmentDateTime,
                duration,
                ExtractDoctorNameFromLocation(googleEvent.Location),
                googleEvent.Description);

            appointment.SetGoogleCalendarEventId(googleEvent.Id);
            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created appointment {AppointmentId} from Google Calendar event {EventId} for patient {PatientName}", 
                appointment.Id, googleEvent.Id, patientName);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating appointment from Google Calendar event {EventId}: {Error}", googleEvent.Id, ex.Message);
            return false;
        }
    }

    private string? ExtractDoctorNameFromLocation(string? location)
    {
        if (string.IsNullOrEmpty(location))
            return null;

        // Location format: "Doctor: Dr. Smith"
        if (location.StartsWith("Doctor: ", StringComparison.OrdinalIgnoreCase))
        {
            return location.Substring("Doctor: ".Length).Trim();
        }

        return location;
    }
}

