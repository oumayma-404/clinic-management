using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
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
    private readonly IClinicRepository _clinicRepository;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// L3b — <b>this class is a real write path for appointments and had no reminder wiring at all.</b> It called
    /// <c>Reschedule(...)</c> and committed straight through the repository, so a visit moved in Google kept its
    /// reminder frozen at the old day and the patient was told the wrong date; an appointment *created* in Google
    /// got no reminder whatsoever. Every other writer goes through the appointment handlers, which is why the
    /// omission was invisible.
    /// </summary>
    private readonly IReminderScheduler _reminderScheduler;
    private readonly INotificationGenerator _notificationGenerator;
    private readonly IGoogleTokenProtector _googleTokenProtector;
    private readonly ILogger<GoogleCalendarSyncService> _logger;

    public GoogleCalendarSyncService(
        IGoogleCalendarService googleCalendarService,
        IAppointmentRepository appointmentRepository,
        IPatientRepository patientRepository,
        IClinicRepository clinicRepository,
        IUnitOfWork unitOfWork,
        IReminderScheduler reminderScheduler,
        INotificationGenerator notificationGenerator,
        IGoogleTokenProtector googleTokenProtector,
        ILogger<GoogleCalendarSyncService> logger)
    {
        _googleTokenProtector = googleTokenProtector;
        _googleCalendarService = googleCalendarService;
        _appointmentRepository = appointmentRepository;
        _patientRepository = patientRepository;
        _clinicRepository = clinicRepository;
        _unitOfWork = unitOfWork;
        _reminderScheduler = reminderScheduler;
        _notificationGenerator = notificationGenerator;
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

    public async Task SyncGoogleCalendarToAppointmentsAsync(
        Guid clinicId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting sync from Google Calendar to appointments for clinic {ClinicId}", clinicId);

            // The clinic is the caller's to name — the controller resolves the signed-in user's, the recurring
            // import passes each connected clinic in turn. Every read and write below is scoped to it, and its own
            // connection is used, so neither caller can write across clinics (#4).
            var connection = await ResolveConnectionAsync(clinicId, cancellationToken);
            if (connection == null)
            {
                _logger.LogInformation("Clinic {ClinicId} has not connected Google Calendar; skipping Google→App sync.", clinicId);
                return;
            }

            // Read once for the whole pass: `GoogleCalendarHoldsOnlyAppointments` decides which events count as
            // appointments at all, and re-reading it per event would be a query per row of the calendar.
            var clinic = await _clinicRepository.GetByIdAsync(clinicId, cancellationToken);
            if (clinic == null)
            {
                _logger.LogWarning("Clinic {ClinicId} disappeared between resolving its connection and the sync; skipping.", clinicId);
                return;
            }

            var startDate = DateTime.UtcNow.AddDays(-7);
            var endDate = DateTime.UtcNow.AddDays(90);
            _logger.LogInformation("Fetching events from {StartDate} to {EndDate}", startDate, endDate);

            var googleEvents = await _googleCalendarService.GetEventsAsync(
                connection,
                startDate: startDate,
                endDate: endDate,
                cancellationToken);

            var eventList = googleEvents.ToList();
            _logger.LogInformation("Retrieved {Count} events from Google Calendar", eventList.Count);
            
            if (eventList.Count > 0)
            {
                // ⚠️ At Information, so this reaches the DURABLE rolling file. A Google event summary written by
                // this product is `Appointment: <patient full name>`, and one written by hand in the calendar is
                // whatever the practice typed — a patient name either way. Masked, not dropped: the date and the
                // count are what diagnose a sync, and the initial-plus-length still distinguishes an empty
                // summary from an unparseable one.
                _logger.LogInformation("Sample events: {Events}",
                    string.Join(", ", eventList.Take(3).Select(e => $"'{LogMask.Name(e.Summary)}' ({e.StartDateTime:yyyy-MM-dd HH:mm})")));
            }

            var allAppointments = (await _appointmentRepository.GetAllAsync(cancellationToken))
                .Where(a => a.ClinicId == clinicId)
                .ToList();
            _logger.LogInformation("Retrieved {Count} appointments for clinic {ClinicId}", allAppointments.Count, clinicId);
            
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
                    googleEvent.Id, LogMask.Name(googleEvent.Summary), googleEvent.StartDateTime);

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
                    _logger.LogDebug("Extracted a patient name from the event: {PatientName}", LogMask.Name(patientName));
                    
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
                if (IsClinicAppointment(googleEvent, clinic))
                {
                    _logger.LogDebug("Event looks like a clinic appointment, creating new appointment");
                    var created = await CreateAppointmentFromGoogleEventAsync(googleEvent, clinicId, cancellationToken);
                    if (created)
                    {
                        createdCount++;
                    }
                }
                else
                {
                    _logger.LogDebug("Event does not match clinic appointment pattern, skipping: {Summary}", LogMask.Name(googleEvent.Summary));
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

    // Field labels emitted into the Google event Description by BuildAppointmentDescription and parsed
    // back out by ExtractNotesFromDescription. Shared so the writer and reader can never drift apart
    // (a silent AC-6 regression risk if one side's literal were changed independently).
    private const string NotesLabel = "Notes: ";
    private const string StatusLabel = "Status: ";

    /// <summary>The prefix this product writes on every event it pushes. Stripped before any name test.</summary>
    private const string OurSummaryPrefix = "Appointment: ";

    private static readonly char[] NameSeparators = [' ', '-'];

    /// <summary>
    /// Words that give a title away as something other than a patient, even inside the practice's own
    /// « this calendar holds only appointments » declaration. Deliberately short: the declaration is the guard, and
    /// a long blocklist would start refusing real Tunisian surnames.
    /// </summary>
    private static readonly HashSet<string> NonPatientWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "réunion", "reunion", "meeting", "call", "appel", "congé", "conge", "vacances", "férié", "ferie",
        "déjeuner", "dejeuner", "pause", "formation", "cnam", "fermé", "ferme", "fermeture", "anniversaire",
    };

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

    /// <summary>
    /// Extracts just the user's notes from a description built by <see cref="BuildAppointmentDescription"/>
    /// (the "Notes: ..." line). Returns null when there is no "Notes:" marker, signalling the caller to
    /// leave the existing notes untouched (prevents the metadata block from being swallowed into Notes).
    /// The notes value runs until the "Status:" field that BuildAppointmentDescription always appends after it.
    /// </summary>
    private static string? ExtractNotesFromDescription(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return null;
        }

        var markerIndex = description.IndexOf(NotesLabel, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var start = markerIndex + NotesLabel.Length;
        var statusIndex = description.IndexOf("\n" + StatusLabel, start, StringComparison.Ordinal);
        var value = statusIndex >= 0
            ? description.Substring(start, statusIndex - start)
            : description.Substring(start);

        return value.Trim();
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

        // A bare « Prénom Nom » title, which is what the practice types when its calendar holds only appointments.
        // ⚠️ Through `LooksLikeAPersonName`, the SAME test `IsClinicAppointment` gates on — a second copy of the
        // word count and its blocklist here would let the gate admit an event this then refuses to name.
        if (summary.Length < 100 && LooksLikeAPersonName(summary))
        {
            return summary.Trim();
        }

        return null;
    }

    /// <summary>
    /// Whether a Google event is one of this clinic's appointments (<c>calendar-import-review</c> AC-1 to AC-3).
    ///
    /// <para>Two regimes, chosen by the practice. Off — the default and unchanged behaviour — an event must carry a
    /// clinic keyword, or our own <c>Appointment: </c> prefix. On, the practice has declared the calendar holds
    /// nothing but appointments, so a title that reads as a person's name is enough; the keyword convention was
    /// confusing staff and buying nothing.</para>
    ///
    /// <para>⚠️ Even with it on this is <b>not</b> « accept everything ». A title that is not a two-to-four-word
    /// name — « Réunion CNAM », one word, a sentence — is refused, because the importer has no patient to book it
    /// against and inventing one from a fragment is worse than skipping the event.</para>
    /// </summary>
    private static bool IsClinicAppointment(GoogleCalendarEvent googleEvent, Clinic clinic)
    {
        if (string.IsNullOrWhiteSpace(googleEvent.Summary))
        {
            return false;
        }

        var summary = googleEvent.Summary.ToLowerInvariant();

        if (clinic.GoogleCalendarHoldsOnlyAppointments)
        {
            return LooksLikeAPersonName(StripOurPrefix(googleEvent.Summary));
        }

        var description = googleEvent.Description?.ToLowerInvariant() ?? string.Empty;

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

    /// <summary>Our own written format's prefix, removed before any name test — see <c>SyncAppointmentTo…</c>.</summary>
    private static string StripOurPrefix(string summary) =>
        summary.StartsWith(OurSummaryPrefix, StringComparison.OrdinalIgnoreCase)
            ? summary[OurSummaryPrefix.Length..].Trim()
            : summary.Trim();

    /// <summary>
    /// Whether an existing patient is the person a calendar title names — the full name as stored, or a first/last
    /// split of the title. Exact on both spellings; see the call site on why nothing partial is accepted.
    /// </summary>
    private static bool MatchesName(Patient patient, string title)
    {
        if (patient.GetFullName().Trim().Equals(title, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parts = title.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        return patient.FirstName.Trim().Equals(parts[0].Trim(), StringComparison.OrdinalIgnoreCase)
            && patient.LastName.Trim().Equals(string.Join(" ", parts.Skip(1)).Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The existing patient this import is probably a duplicate of, or null — <c>calendar-import-duplicate-merge</c>
    /// AC-2 to AC-7. Reached only when no patient matched the name <b>exactly</b>, so a hit here is always a
    /// question for a human and never a link.
    ///
    /// <para>Two gates, and both must pass. <b>Exactly one</b> patient whose name is the same name written
    /// differently — zero is the ordinary case and two or more is a refusal, for the reason the exact path already
    /// refuses: a third duplicate is worse than a second, and guessing between two people is worse than both.
    /// Then the phone, when the event description carried one: it <b>vetoes</b> a candidate whose own number is
    /// different. A name equivalence never survives a contradicting phone, because two spellings of one name plus
    /// two different numbers is two people.</para>
    /// </summary>
    private Guid? FindSuggestedDuplicate(
        IReadOnlyCollection<Patient> patients,
        string firstName,
        string lastName,
        string? eventPhoneE164)
    {
        var candidates = patients
            .Where(p => PatientNameEquivalence.AreWritingVariants(firstName, lastName, p.FirstName, p.LastName))
            .ToList();

        if (candidates.Count != 1)
        {
            if (candidates.Count > 1)
            {
                _logger.LogInformation(
                    "Google import: {Count} patients resemble the imported name; suggesting none rather than guessing.",
                    candidates.Count);
            }

            return null;
        }

        var candidate = candidates[0];
        var candidatePhone = PhoneNumber.ToE164(candidate.PhoneNumber?.Value);

        if (eventPhoneE164 != null && candidatePhone != null && candidatePhone != eventPhoneE164)
        {
            _logger.LogInformation(
                "Google import: patient {PatientId} matches the imported name but holds a different phone; no suggestion.",
                candidate.Id);
            return null;
        }

        return candidate.Id;
    }

    /// <summary>
    /// A deliverable Tunisian phone number written into the event description, or null. <b>Ambiguity is null</b>:
    /// two different numbers in one description name no single patient, and picking the first would be a guess.
    /// <see cref="PhoneNumber.ToE164"/> is the only judge of what a number is, so this cannot drift from what
    /// patient entry and reminder dispatch accept.
    /// </summary>
    private static string? ExtractPhone(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var found = System.Text.RegularExpressions.Regex
            .Matches(description, @"\+?[\d][\d\s\-\.]{6,20}\d")
            .Select(m => PhoneNumber.ToE164(m.Value))
            .Where(e164 => e164 != null)
            .Distinct()
            .ToList();

        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// Whether a title reads as « Prénom Nom » — two to four words, none of them a keyword that gives the event
    /// away as something other than a patient.
    ///
    /// <para>Two words minimum is load-bearing: a single token cannot be split into a first and a last name, and the
    /// branch that used to try stored « Karim » as both.</para>
    /// </summary>
    private static bool LooksLikeAPersonName(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var parts = title.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts.Length > 4)
        {
            return false;
        }

        return !parts.Any(part => NonPatientWords.Contains(part.Trim()));
    }

    private async Task UpdateAppointmentFromGoogleEventAsync(
        Appointment appointment,
        GoogleCalendarEvent googleEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            var updated = false;
            var moved = false;

            // Update appointment time if changed
            if (appointment.AppointmentDateTime != googleEvent.StartDateTime)
            {
                appointment.Reschedule(googleEvent.StartDateTime);
                updated = true;
                moved = true;
            }

            // Update duration if changed
            var newDuration = googleEvent.EndDateTime - googleEvent.StartDateTime;
            if (appointment.Duration != newDuration)
            {
                appointment.UpdateDuration(newDuration);
                updated = true;
            }

            // Update notes if changed. Parse back ONLY the user's notes from the composite description
            // block that BuildAppointmentDescription writes (Doctor:/Notes:/Status:/Patient ID:). Assigning
            // the whole Description here made the metadata block accumulate and nest on every sync. When the
            // description carries no "Notes:" line, leave the appointment's notes unchanged.
            var parsedNotes = ExtractNotesFromDescription(googleEvent.Description);
            if (parsedNotes != null && appointment.Notes != parsedNotes)
            {
                appointment.UpdateNotes(parsedNotes);
                updated = true;
            }

            if (updated)
            {
                await _appointmentRepository.UpdateAsync(appointment, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Updated appointment {AppointmentId} from Google Calendar event {EventId}", appointment.Id, googleEvent.Id);

                // L3b — void-and-re-enqueue the reminder, post-commit, exactly as the appointment handlers do.
                // Only on a MOVE: a notes-only edit leaves the queued reminder correct, and re-enqueuing it would
                // reset a tier that has already been reached.
                if (moved)
                {
                    await RescheduleRemindersAsync(appointment, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating appointment {AppointmentId} from Google Calendar event", appointment.Id);
        }
    }

    /// <summary>
    /// Re-queues the outbound reminders for an appointment whose time changed on the Google side.
    ///
    /// <para>Post-commit and best-effort, the same contract the appointment handlers use — <c>IReminderScheduler</c>
    /// swallows its own failures, and a reminder must never roll back a sync that has already written the new
    /// time. A busy slot (no patient) has nobody to remind, so it is skipped rather than passed a null id.</para>
    /// </summary>
    private async Task RescheduleRemindersAsync(Appointment appointment, CancellationToken cancellationToken)
    {
        if (appointment.PatientId is not Guid patientId)
        {
            return;
        }

        var patient = appointment.Patient ?? await _patientRepository.GetByIdAsync(patientId, cancellationToken);
        if (patient == null)
        {
            return;
        }

        await _reminderScheduler.RescheduleForAppointmentAsync(
            appointment.ClinicId, appointment.Id, patientId, patient.GetFullName(),
            appointment.AppointmentDateTime, cancellationToken);
    }

    private async Task<bool> CreateAppointmentFromGoogleEventAsync(
        GoogleCalendarEvent googleEvent,
        Guid clinicId,
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
                    if (patientById != null && patientById.ClinicId == clinicId)
                    {
                        patientName = patientById.GetFullName();
                        _logger.LogInformation("Found patient {PatientId} by id in the Google Calendar event description.", patientById.Id);
                    }
                }
            }
            
            if (string.IsNullOrEmpty(patientName))
            {
                // ⚠️ At Warning, so this reaches the durable file too — and it is the one statement in this file
                // that logs a summary the product did NOT build, i.e. free text a practice typed into Google.
                _logger.LogWarning("Cannot create appointment from Google Calendar event {EventId}: patient name not found in summary '{Summary}' or description",
                    googleEvent.Id, LogMask.Name(googleEvent.Summary));
                return false;
            }

            // Normalize patient name (trim, normalize spaces)
            patientName = System.Text.RegularExpressions.Regex.Replace(patientName.Trim(), @"\s+", " ");

            // Try to find existing patient by name (more flexible matching) — scoped to THIS clinic only (#4).
            // includeArchived: matching must see archived patients. Hiding them here would not protect anything —
            // the very next line auto-creates a placeholder patient, so excluding them would silently produce a
            // DUPLICATE record for someone the clinic already has.
            var patients = (await _patientRepository.GetByClinicIdAsync(clinicId, includeArchived: true, cancellationToken: cancellationToken)).Items;
            
            // AC-4 to AC-6 — EXACT and UNAMBIGUOUS, in one pass over both spellings of a match.
            //
            // ⚠️ The `Contains` fallback that stood here is deleted, not tightened. It made « Ali » match
            // « Ali Ben Salah », so an event could be booked onto the wrong person's file — and behind the old
            // keyword gate that fired rarely, while as the primary path it would run on every event of the calendar.
            // Two people of the same name is a refusal for the same reason: a wrong patient on an appointment is
            // worse than an unimported event, and a third duplicate is worse than both.
            var candidates = patients.Where(p => MatchesName(p, patientName)).ToList();

            if (candidates.Count > 1)
            {
                _logger.LogWarning(
                    "Google event {EventId} names a patient matching {Count} records; skipped rather than guessing.",
                    googleEvent.Id, candidates.Count);
                return false;
            }

            var patient = candidates.SingleOrDefault();
            var createdPatient = false;

            if (patient == null)
            {
                // Patient not found - create it automatically
                _logger.LogInformation("Patient {PatientName} not found. Creating one automatically from Google Calendar event {EventId}",
                    LogMask.Name(patientName), googleEvent.Id);
                
                // ⚠️ The split is `PatientNameEquivalence`'s, not a second copy: the near-duplicate rule compares
                // the halves it produces, so a different split here would compare something we did not store.
                var split = PatientNameEquivalence.SplitTitle(patientName);
                if (split == null)
                {
                    // A single token cannot be split into a first and a last name. The branch that used to try
                    // stored « Karim » as both, so the clinic acquired a patient called « Karim Karim ».
                    _logger.LogWarning("Cannot extract a first and last name from {PatientName} for Google Calendar event {EventId}",
                        LogMask.Name(patientName), googleEvent.Id);
                    return false;
                }

                var firstName = split.Value.First;
                var lastName = split.Value.Last;

                // A phone typed into the event description is real data and the reviewer's next keystrokes, so it
                // is stored — and it is also the only evidence besides the name that this import ever has.
                var eventPhone = ExtractPhone(googleEvent.Description);
                var suggestedDuplicateId = FindSuggestedDuplicate(patients, firstName, lastName, eventPhone);

                // AC-7 — **DateOfBirth is null**, not today's date. It used to be `DateTime.UtcNow`, so every patient
                // this path created was recorded as born today; `Patient.DateOfBirth`'s own note calls that
                // substitution out as removed, and this was the call site that never got the fix.
                // No contact details either: a patient conjured from a calendar event title genuinely has none.
                // Dentition stays at the entity's Adult default rather than being derived — there is now honestly
                // no birth date to derive it from, and whoever completes the fiche sets it.
                var newPatient = new Patient(
                    Guid.NewGuid(),
                    clinicId,
                    firstName,
                    lastName,
                    dateOfBirth: null,
                    "Unknown", // Default gender
                    phoneNumber: eventPhone == null ? null : new PhoneNumber(eventPhone));

                newPatient.MarkImportedFromCalendar(DateTime.UtcNow, suggestedDuplicateId);

                await _patientRepository.AddAsync(newPatient, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                patient = newPatient;
                createdPatient = true;
                _logger.LogInformation("Created patient {PatientId} from Google Calendar event {EventId}",
                    patient.Id, googleEvent.Id);
            }
            else
            {
                _logger.LogInformation("Found matching patient {PatientId} for Google Calendar event {EventId}",
                    patient.Id, googleEvent.Id);
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

            // AC-P1.29 — the stated rule for this writer: **import-with-override-flag, never skip.**
            //
            // This service writes appointments straight through the repository, bypassing both MediatR handlers
            // and therefore the working-hours guard. Two options were available and one is clearly wrong:
            // refusing an out-of-hours Google event would SILENTLY DROP the Sunday appointment the dentist
            // typed into their own calendar — the inner catch here only logs — which is worse than importing
            // it, because the clinic would believe Google and the app agreed when they did not.
            //
            // So the event is always imported, and an out-of-hours one is logged rather than refused.
            // `doctorId` is null on this path, so only the clinic-wide hours can ever apply.
            var clinic = await _clinicRepository.GetByIdAsync(patient.ClinicId, cancellationToken);
            var hours = WorkingHoursResolver.Resolve(null, clinic?.WorkingHoursJson);
            if (!WorkingHoursResolver.IsWithin(hours, appointmentDateTime, duration, out var outsideReason))
            {
                // The log is now the whole record: the column this also used to stamp was read by nothing (AC-25).
                _logger.LogWarning(
                    "Google event {EventId} imported outside working hours for clinic {ClinicId}: {Reason}",
                    googleEvent.Id, patient.ClinicId, outsideReason);
            }

            appointment.SetGoogleCalendarEventId(googleEvent.Id);
            await _appointmentRepository.AddAsync(appointment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created appointment {AppointmentId} from Google Calendar event {EventId}",
                appointment.Id, googleEvent.Id);

            // AC-9/AC-11 — post-commit and best-effort, the interface's own contract. Only for a patient this pass
            // CREATED: an established practice connecting its calendar must not badge the bell once per person it
            // already knew.
            if (createdPatient)
            {
                await _notificationGenerator.PatientImportedFromCalendarAsync(
                    patient.ClinicId, patient.Id, patient.GetFullName(), cancellationToken);
            }

            // L3b — an appointment created in Google is an appointment, and it used to enqueue no reminder at
            // all: the dentist who types a visit into their own calendar got a silently reminder-less booking.
            // Post-commit and best-effort, like every other reminder call in the product.
            await _reminderScheduler.ScheduleForAppointmentAsync(
                patient.ClinicId, appointment.Id, patient.Id, patient.GetFullName(),
                appointmentDateTime, cancellationToken);

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

