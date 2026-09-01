using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Appointment → Google Calendar. <b>One direction only, and that is the point.</b>
///
/// <para>⚠️ <b>Google → App was retired</b> (« Importer depuis Google »): the button, the 15-minute
/// <c>GoogleCalendarImportJob</c>, the « ce calendrier ne contient que des rendez-vous » gate and ~750 lines of
/// event-to-patient guesswork that lived in this file. It was a mass write with no bounds and no way back — one
/// press turned 97 days of a practice's calendar into appointment rows, the past week of them landing on
/// « À clôturer » as visits nobody could honestly close. See
/// <c>features/calendar-import-revert/notes.md</c>.</para>
///
/// <para><b>The undo outlived it, deliberately.</b> <c>CalendarImportRun</c>, the revert command, the preview and
/// the « Annuler cet import » banner all read history rather than importing anything, so a cabinet that pressed
/// the old button can still take it back. Nothing here opens a run any more — nothing creates one.</para>
/// </summary>
public class GoogleCalendarSyncService : IGoogleCalendarSyncService
{
    private readonly IGoogleCalendarService _googleCalendarService;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IPatientRepository _patientRepository;
    private readonly IClinicRepository _clinicRepository;
    private readonly IUnitOfWork _unitOfWork;

    private readonly IGoogleTokenProtector _googleTokenProtector;
    private readonly ILogger<GoogleCalendarSyncService> _logger;

    public GoogleCalendarSyncService(
        IGoogleCalendarService googleCalendarService,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        IUnitOfWork unitOfWork,
        IGoogleTokenProtector googleTokenProtector,
        ILogger<GoogleCalendarSyncService> logger)
    {
        _googleTokenProtector = googleTokenProtector;
        _googleCalendarService = googleCalendarService;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _clinicRepository = clinicRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Loads a clinic's own Google connection (refresh token + calendar id). Returns null when the clinic has
    /// not connected Google — callers then skip silently (no cross-clinic shared account any more, #4).
    ///
    /// <para>⚠️ <b>An undecryptable token is a THROW, not a null</b> (FR-3.3). Null means « this practice never
    /// connected Google », which is a normal state every caller is written to skip quietly — so returning it for
    /// a broken key ring would stop every connected clinic's calendar syncing with nothing anywhere saying why,
    /// and the screen would go on reporting « Connecté ». Refusing loudly is the whole of « refuse rather than
    /// degrade »: the exception names the recovery, and the calling handler already logs and swallows it, so a
    /// booking is never lost over a calendar hop.</para>
    ///
    /// <para>⚠️ It reads the <b>ciphertext column only</b>. Falling back to the legacy plaintext one would be the
    /// same degradation wearing a different hat — the credential stays usable off a stolen disk indefinitely, and
    /// the FR-3.4 backfill's own progress figure would never reach zero because nothing would push it there.</para>
    /// </summary>
    private async Task<GoogleCalendarConnection?> ResolveConnectionAsync(Guid clinicId, CancellationToken cancellationToken)
    {
        var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
        if (clinic == null || string.IsNullOrEmpty(clinic.GoogleRefreshTokenProtected))
        {
            return null;
        }

        if (!_googleTokenProtector.TryUnprotect(clinic.GoogleRefreshTokenProtected, out var refreshToken))
        {
            throw new InvalidOperationException(
                $"Le jeton Google Agenda du cabinet « {clinic.Name} » est illisible : la clé de protection des "
                + "données a changé ou est absente. La synchronisation est refusée plutôt que silencieusement "
                + "désactivée. Le cabinet doit reconnecter son agenda depuis « Paramètres → Google Agenda ».");
        }

        return new GoogleCalendarConnection(refreshToken, clinic.GoogleCalendarId);
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
            
            // FR-4.4 — the appointment's own id, never its patient. A reader chasing a sync problem needs to find
            // the row; the name only told them who it belonged to.
            _logger.LogDebug("Appointment found: {AppointmentId}, HasPatient={HasPatient}, DateTime={DateTime}, Status={Status}, GoogleEventId={GoogleEventId}",
                appointment.Id, appointment.PatientId is not null, appointment.AppointmentDateTime, appointment.Status, appointment.GoogleCalendarEventId);

            // Resolve THIS appointment's clinic connection (#4). No global/shared account any more — if the
            // owning clinic has not connected Google, skip silently (nothing to sync, no cross-clinic leak).
            var connection = await ResolveConnectionAsync(appointment.ClinicId, cancellationToken);
            if (connection == null)
            {
                _logger.LogInformation("Clinic {ClinicId} has not connected Google Calendar; skipping sync for appointment {AppointmentId}",
                    appointment.ClinicId, appointmentId);
                return;
            }

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
                        
                        await _googleCalendarService.DeleteEventAsync(connection, appointment.GoogleCalendarEventId, cancellationToken);
                        
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
            
            // ⚠️ `summary` is `$"Appointment: {patient.GetFullName()}"` (built above), so it is a patient name
            // wearing a neutral placeholder name. FR-4.4 applies to the VALUE, not to what the placeholder is
            // called — see the guard note in LogTemplateCoverageTests.
            _logger.LogDebug("Syncing appointment to Google Calendar: Summary={Summary}, Start={StartDateTime}, End={EndDateTime}, Location={Location}",
                LogMask.Name(summary), startDateTime, endDateTime, location);

            if (string.IsNullOrEmpty(appointment.GoogleCalendarEventId))
            {
                // Create new event
                _logger.LogInformation("Creating new Google Calendar event for appointment {AppointmentId}", appointmentId);
                var eventId = await _googleCalendarService.CreateEventAsync(
                    connection,
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
                    connection,
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

    // Field labels emitted into the Google event Description by BuildAppointmentDescription.
    //
    // ⚠️ They used to be described as « shared so the writer and reader can never drift apart » — the reader was
    // `ExtractNotesFromDescription`, on the retired Google→App import. Nothing parses a description back now, so
    // these are write-only labels and the drift risk they warned about is gone with the direction.
    private const string NotesLabel = "Notes: ";
    private const string StatusLabel = "Status: ";

    private string BuildAppointmentDescription(Appointment appointment)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(appointment.DoctorName))
        {
            parts.Add($"Doctor: {appointment.DoctorName}");
        }

        if (!string.IsNullOrEmpty(appointment.Notes))
        {
            parts.Add($"{NotesLabel}{appointment.Notes}");
        }

        parts.Add($"{StatusLabel}{appointment.Status}");
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
}
