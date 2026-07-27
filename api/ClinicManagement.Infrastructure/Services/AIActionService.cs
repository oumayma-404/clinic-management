using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.AI.Commands;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClinicManagement.Infrastructure.Services;

public class AIActionService : IAIActionService
{
    // French clinic locale for user-facing dates in AI action responses.
    private static readonly CultureInfo FrCulture = new("fr-FR");

    private readonly IHuggingFaceAIService _huggingFaceAIService;
    private readonly IMediator _mediator;
    private readonly IPatientRepository _patientRepository;
    private readonly IAppointmentRepository _appointmentRepository;
    private readonly IProcedureTypeRepository _procedureTypeRepository;
    private readonly IDoctorRepository _doctorRepository;
    private readonly IClinicContext _clinicContext;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AIActionService> _logger;

    public AIActionService(
        IHuggingFaceAIService huggingFaceAIService,
        IMediator mediator,
        IPatientRepository patientRepository,
        IAppointmentRepository appointmentRepository,
        IProcedureTypeRepository procedureTypeRepository,
        IDoctorRepository doctorRepository,
        IClinicContext clinicContext,
        IUserRepository userRepository,
        ILogger<AIActionService> logger)
    {
        _huggingFaceAIService = huggingFaceAIService;
        _mediator = mediator;
        _patientRepository = patientRepository;
        _appointmentRepository = appointmentRepository;
        _procedureTypeRepository = procedureTypeRepository;
        _doctorRepository = doctorRepository;
        _clinicContext = clinicContext;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<AIActionResult> ExecuteActionAsync(AIActionRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use AI to detect intent and extract parameters
            var intentResult = await DetectIntentAsync(request, cancellationToken);

            if (!intentResult.ShouldExecuteAction)
            {
                return intentResult;
            }

            // Execute the detected action
            return intentResult.ActionType switch
            {
                "create_appointment" => await CreateAppointmentActionAsync(intentResult.ActionParameters!, cancellationToken),
                "search_patient" or "find_patient" => await SearchPatientActionAsync(intentResult.ActionParameters!, cancellationToken),
                "view_patient" or "get_patient" => await ViewPatientActionAsync(intentResult.ActionParameters!, cancellationToken),
                "list_appointments" or "get_appointments" => await ListAppointmentsActionAsync(intentResult.ActionParameters!, cancellationToken),
                "cancel_appointment" => await CancelAppointmentActionAsync(intentResult.ActionParameters!, cancellationToken),
                _ => new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Je comprends que vous souhaitez effectuer une action, mais je ne sais pas comment la traiter. Pourriez-vous reformuler ?"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur : {ex.Message}. Veuillez réessayer."
            };
        }
    }

    private async Task<AIActionResult> DetectIntentAsync(AIActionRequest request, CancellationToken cancellationToken)
    {
        // Get current doctor information for context
        string? doctorName = null;
        string? doctorInfo = null;

        if (request.Context?.DoctorId.HasValue == true)
        {
            var doctor = await _doctorRepository.GetByIdAsync(request.Context.DoctorId.Value, cancellationToken);
            if (doctor != null)
            {
                doctorName = doctor.FullName;
                doctorInfo = $"Dr. {doctor.FullName} ({doctor.Specialty})";
            }
        }
        else
        {
            // Try to get doctor from current user
            var userId = _clinicContext.GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                var doctor = await _doctorRepository.GetByUserIdAsync(userId, cancellationToken);
                if (doctor != null)
                {
                    doctorName = doctor.FullName;
                    doctorInfo = $"Dr. {doctor.FullName} ({doctor.Specialty})";
                }
            }
        }

        // Build a prompt for the AI to detect intent and extract parameters
        var doctorContext = !string.IsNullOrEmpty(doctorInfo)
            ? $"\n\nIMPORTANT: The current logged-in doctor is {doctorInfo}. When creating appointments, always use this doctor unless the user explicitly specifies a different doctor."
            : "";

        var systemPrompt = @"You are an AI assistant for a clinic management system. Your job is to detect when users want to perform actions and extract the necessary parameters." + doctorContext + @"

SUPPORTED ACTIONS:

1. create_appointment - Create a new appointment
   Parameters: patient_name, date, time, procedure (optional)
   Examples: ""Create an appointment for John Smith tomorrow at 2pm"", ""Schedule with Sarah Johnson on Monday at 10:30am""
   Note: Always use the logged-in doctor unless user specifies otherwise.

2. search_patient / find_patient - Search for a patient by name
   Parameters: patient_name (required)
   Examples: ""Find patient John Smith"", ""Search for Sarah Johnson"", ""Who is Mike Davis?""

3. view_patient / get_patient - View patient details
   Parameters: patient_name (required)
   Examples: ""Show me John Smith's details"", ""View patient Sarah Johnson"", ""Get info for Mike Davis""

4. list_appointments / get_appointments - List appointments
   Parameters: date (optional), patient_name (optional)
   Examples: ""Show appointments for tomorrow"", ""List appointments for John Smith"", ""What appointments are scheduled?""

5. cancel_appointment - Cancel an appointment
   Parameters: patient_name (required), date (optional), time (optional)
   Examples: ""Cancel John Smith's appointment tomorrow"", ""Cancel the 2pm appointment for Sarah""

Respond ONLY with a JSON object in this exact format:
{
  ""should_execute_action"": true/false,
  ""action_type"": ""create_appointment"" | ""search_patient"" | ""view_patient"" | ""list_appointments"" | ""cancel_appointment"" | null,
  ""action_parameters"": {
    ""patient_name"": ""..."",
    ""date"": ""..."",
    ""time"": ""..."",
    ""procedure"": ""..."" (optional)
  },
  ""response_message"": ""A natural language response to confirm what you understood""
}

IMPORTANT: Always write the ""response_message"" value in French — French is the clinic's language.

If the user is just asking a question or chatting, set should_execute_action to false and provide a helpful response.";

        // Convert conversation history to Hugging Face format
        var messages = new List<HuggingFaceAIMessage>
        {
            new HuggingFaceAIMessage { Role = "user", Content = systemPrompt },
            new HuggingFaceAIMessage { Role = "assistant", Content = "I understand. I will detect intents and extract parameters in JSON format, with the response_message in French." }
        };

        // Add conversation history
        foreach (var msg in request.ConversationHistory.TakeLast(5)) // Last 5 messages for context
        {
            messages.Add(new HuggingFaceAIMessage
            {
                Role = msg.Role == "assistant" ? "assistant" : "user",
                Content = msg.Content
            });
        }

        // Add current user message
        messages.Add(new HuggingFaceAIMessage
        {
            Role = "user",
            Content = request.UserMessage
        });

        try
        {
            var aiResponse = await _huggingFaceAIService.ChatAsync(messages, request.Context, cancellationToken);
            var responseText = aiResponse.Message.Trim();

            // Try to parse JSON from the response
            var jsonMatch = Regex.Match(responseText, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = jsonMatch.Value;
                var result = JsonSerializer.Deserialize<JsonElement>(json);

                var shouldExecute = result.TryGetProperty("should_execute_action", out var shouldExec) && shouldExec.GetBoolean();
                var actionType = result.TryGetProperty("action_type", out var action) ? action.GetString() : null;
                var responseMessage = result.TryGetProperty("response_message", out var resp) ? resp.GetString() : null;

                var parameters = new Dictionary<string, object>();
                if (result.TryGetProperty("action_parameters", out var paramsElement))
                {
                    foreach (var prop in paramsElement.EnumerateObject())
                    {
                        var value = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            parameters[prop.Name] = value;
                        }
                    }
                }

                return new AIActionResult
                {
                    ShouldExecuteAction = shouldExecute && !string.IsNullOrEmpty(actionType),
                    ActionType = actionType,
                    ActionParameters = parameters.Count > 0 ? parameters : null,
                    ResponseMessage = responseMessage ?? "Je vais vous aider."
                };
            }

            // If no JSON found, check if it's a simple appointment creation request
            return ParseSimpleAppointmentRequest(request.UserMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI intent, trying simple parsing");
            return ParseSimpleAppointmentRequest(request.UserMessage);
        }
    }

    private AIActionResult ParseSimpleAppointmentRequest(string message)
    {
        var lowerMessage = message.ToLower();

        // Check for appointment creation keywords
        if (!lowerMessage.Contains("create") && !lowerMessage.Contains("schedule") &&
            !lowerMessage.Contains("book") && !lowerMessage.Contains("make") &&
            !lowerMessage.Contains("appointment"))
        {
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = null
            };
        }

        var parameters = new Dictionary<string, object>();

        // Extract patient name (look for patterns like "with [name]", "for [name]", "patient [name]")
        var patientPattern = @"(?:with|for|patient)\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)";
        var patientMatch = Regex.Match(message, patientPattern, RegexOptions.IgnoreCase);
        if (patientMatch.Success)
        {
            parameters["patient_name"] = patientMatch.Groups[1].Value.Trim();
        }

        // Extract date
        var datePattern = @"(?:on|for)\s+(\d{1,2}[/-]\d{1,2}[/-]\d{2,4}|\d{4}-\d{2}-\d{2}|tomorrow|today|monday|tuesday|wednesday|thursday|friday|saturday|sunday)";
        var dateMatch = Regex.Match(message, datePattern, RegexOptions.IgnoreCase);
        if (dateMatch.Success)
        {
            parameters["date"] = dateMatch.Groups[1].Value.Trim();
        }

        // Extract time
        var timePattern = @"(?:at|@)\s*(\d{1,2}:\d{2}|\d{1,2}\s*(?:am|pm))";
        var timeMatch = Regex.Match(message, timePattern, RegexOptions.IgnoreCase);
        if (timeMatch.Success)
        {
            parameters["time"] = timeMatch.Groups[1].Value.Trim();
        }

        // Extract procedure
        var procedurePattern = @"(?:for|procedure|type)\s+([a-z\s]+)";
        var procedureMatch = Regex.Match(message, procedurePattern, RegexOptions.IgnoreCase);
        if (procedureMatch.Success)
        {
            parameters["procedure"] = procedureMatch.Groups[1].Value.Trim();
        }

        if (parameters.Count == 0)
        {
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = "Je comprends que vous souhaitez créer un rendez-vous, mais il me manque des informations. Veuillez indiquer : le nom du patient, la date et l'heure."
            };
        }

        return new AIActionResult
        {
            ShouldExecuteAction = true,
            ActionType = "create_appointment",
            ActionParameters = parameters,
            ResponseMessage = "Je crée ce rendez-vous pour vous."
        };
    }

    private async Task<AIActionResult> CreateAppointmentActionAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get clinic ID from context
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Authentification requise pour créer des rendez-vous."
                };
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Utilisateur introuvable."
                };
            }

            var clinicId = user.ClinicId;

            // Get current user's doctor (the logged-in doctor)
            var currentDoctor = await _doctorRepository.GetByUserIdAsync(userId, cancellationToken);
            Guid? doctorId = null;
            string? doctorName = null;

            if (currentDoctor != null)
            {
                doctorId = currentDoctor.Id;
                doctorName = currentDoctor.FullName;
            }

            // Find patient by name (only in user's clinic)
            Guid? patientId = null;
            if (parameters.TryGetValue("patient_name", out var patientNameObj))
            {
                var requestedPatientName = patientNameObj.ToString() ?? "";
                // Archived patients are excluded — the assistant must not find what the UI's own search hides.
                var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
                var patient = patients.FirstOrDefault(p =>
                    $"{p.FirstName} {p.LastName}".Equals(requestedPatientName, StringComparison.OrdinalIgnoreCase) ||
                    p.FirstName.Equals(requestedPatientName, StringComparison.OrdinalIgnoreCase) ||
                    p.LastName.Equals(requestedPatientName, StringComparison.OrdinalIgnoreCase));

                if (patient == null)
                {
                    return new AIActionResult
                    {
                        ShouldExecuteAction = false,
                        ResponseMessage = $"Je n'ai trouvé aucun patient nommé « {requestedPatientName} » dans votre cabinet. Vérifiez le nom et réessayez."
                    };
                }
                patientId = patient.Id;
            }

            // Parse date
            DateTime appointmentDate;
            if (parameters.TryGetValue("date", out var dateObj))
            {
                var dateStr = dateObj.ToString() ?? "";
                if (!TryParseDate(dateStr, out appointmentDate))
                {
                    return new AIActionResult
                    {
                        ShouldExecuteAction = false,
                        ResponseMessage = $"Je n'ai pas compris la date « {dateStr} ». Utilisez un format comme « 2024-12-25 » ou « demain »."
                    };
                }
            }
            else
            {
                appointmentDate = DateTime.Today.AddDays(1); // Default to tomorrow
            }

            // Parse time
            int hour = 9, minute = 0; // Default to 9:00 AM
            if (parameters.TryGetValue("time", out var timeObj))
            {
                var timeStr = timeObj.ToString() ?? "";
                if (!TryParseTime(timeStr, out hour, out minute))
                {
                    return new AIActionResult
                    {
                        ShouldExecuteAction = false,
                        ResponseMessage = $"Je n'ai pas compris l'heure « {timeStr} ». Utilisez un format comme « 14:30 » ou « 14h »."
                    };
                }
            }

            // Create DateTime in local time (user's timezone)
            // This ensures "10am" means 10am in the user's local time, not UTC
            var appointmentDateTime = new DateTime(
                appointmentDate.Year,
                appointmentDate.Month,
                appointmentDate.Day,
                hour,
                minute,
                0,
                DateTimeKind.Local
            );

            // Find procedure type if specified
            Guid? procedureTypeId = null;
            if (parameters.TryGetValue("procedure", out var procedureObj))
            {
                var procedureName = procedureObj.ToString() ?? "";
                var procedureTypes = await _procedureTypeRepository.GetActiveAsync(cancellationToken);
                var procedureType = procedureTypes.FirstOrDefault(pt =>
                    pt.Name.Equals(procedureName, StringComparison.OrdinalIgnoreCase) ||
                    pt.Name.Contains(procedureName, StringComparison.OrdinalIgnoreCase));

                if (procedureType != null)
                {
                    procedureTypeId = procedureType.Id;
                }
            }

            // Create appointment
            var command = new CreateAppointmentCommand
            {
                PatientId = patientId,
                AppointmentDateTime = appointmentDateTime,
                DurationMinutes = 30, // Default 30 minutes (will be overridden by procedure if specified)
                Notes = null,
                ProcedureTypeId = procedureTypeId,
                DoctorId = doctorId,
                DoctorName = doctorName
            };

            var result = await _mediator.Send(command, cancellationToken);

            if (result.IsFailure)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai pas pu créer le rendez-vous : {result.Error}"
                };
            }

            var appointment = result.Value!; // non-null: guarded by the IsFailure check above
            var appointmentPatientName = appointment.PatientName ?? "Inconnu";
            var dateTime = appointment.AppointmentDateTime.ToString("dd MMMM yyyy 'à' HH:mm", FrCulture);
            var procedureInfo = !string.IsNullOrEmpty(appointment.ProcedureTypeName)
                ? $"\nActe : {appointment.ProcedureTypeName}"
                : "";

            return new AIActionResult
            {
                ShouldExecuteAction = true,
                ActionType = "create_appointment",
                ActionResult = appointment,
                ResponseMessage = $"✅ Rendez-vous créé avec succès !\n\n" +
                                $"Patient : {appointmentPatientName}\n" +
                                $"Date et heure : {dateTime}\n" +
                                $"Durée : {appointment.Duration.TotalMinutes} minutes{procedureInfo}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating appointment via AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur lors de la création du rendez-vous : {ex.Message}"
            };
        }
    }

    private bool TryParseDate(string dateStr, out DateTime date)
    {
        date = DateTime.Today;

        // Try common date formats
        if (DateTime.TryParse(dateStr, out date))
        {
            return true;
        }

        // Handle natural language dates
        var lower = dateStr.ToLower();
        if (lower == "today")
        {
            date = DateTime.Today;
            return true;
        }
        if (lower == "tomorrow")
        {
            date = DateTime.Today.AddDays(1);
            return true;
        }

        // Try to parse day names
        var dayOfWeek = lower switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            "sunday" => DayOfWeek.Sunday,
            _ => (DayOfWeek?)null
        };

        if (dayOfWeek.HasValue)
        {
            var today = DateTime.Today;
            var daysUntil = ((int)dayOfWeek.Value - (int)today.DayOfWeek + 7) % 7;
            if (daysUntil == 0) daysUntil = 7; // Next week if today is that day
            date = today.AddDays(daysUntil);
            return true;
        }

        return false;
    }

    private bool TryParseTime(string timeStr, out int hour, out int minute)
    {
        hour = 9;
        minute = 0;

        // Clean up the time string
        timeStr = timeStr.Trim().ToLower();

        // Try standard format (HH:MM or H:MM)
        if (Regex.IsMatch(timeStr, @"^\d{1,2}:\d{2}$"))
        {
            var parts = timeStr.Split(':');
            if (int.TryParse(parts[0], out hour) && int.TryParse(parts[1], out minute))
            {
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                {
                    return true;
                }
            }
        }

        // Try 12-hour format with AM/PM and minutes (e.g., "2:30pm", "10:15am")
        var timeMatchWithMinutes = Regex.Match(timeStr, @"(\d{1,2}):(\d{2})\s*(am|pm)", RegexOptions.IgnoreCase);
        if (timeMatchWithMinutes.Success)
        {
            if (int.TryParse(timeMatchWithMinutes.Groups[1].Value, out hour) &&
                int.TryParse(timeMatchWithMinutes.Groups[2].Value, out minute))
            {
                var isPm = timeMatchWithMinutes.Groups[3].Value.ToLower() == "pm";
                if (isPm && hour != 12) hour += 12;
                if (!isPm && hour == 12) hour = 0;
                if (hour >= 0 && hour < 24 && minute >= 0 && minute < 60)
                {
                    return true;
                }
            }
        }

        // Try 12-hour format with AM/PM (e.g., "2pm", "10am")
        var timeMatch = Regex.Match(timeStr, @"(\d{1,2})\s*(am|pm)", RegexOptions.IgnoreCase);
        if (timeMatch.Success)
        {
            if (int.TryParse(timeMatch.Groups[1].Value, out hour))
            {
                var isPm = timeMatch.Groups[2].Value.ToLower() == "pm";
                if (isPm && hour != 12) hour += 12;
                if (!isPm && hour == 12) hour = 0;
                if (hour >= 0 && hour < 24)
                {
                    minute = 0;
                    return true;
                }
            }
        }

        // Try just hour number (assume 24-hour format if > 12, otherwise assume PM)
        if (int.TryParse(timeStr, out hour))
        {
            if (hour >= 0 && hour < 24)
            {
                minute = 0;
                return true;
            }
        }

        return false;
    }

    private async Task<AIActionResult> SearchPatientActionAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!parameters.TryGetValue("patient_name", out var patientNameObj))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "J'ai besoin d'un nom de patient pour effectuer la recherche. Veuillez indiquer le nom du patient."
                };
            }

            var patientName = patientNameObj.ToString() ?? "";
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Authentification requise."
                };
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Utilisateur introuvable."
                };
            }

            var clinicId = user.ClinicId;
            // Archived patients are excluded — the assistant must not find what the UI's own search hides.
                var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
            var matchingPatients = patients.Where(p =>
                $"{p.FirstName} {p.LastName}".Contains(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.FirstName.Contains(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.LastName.Contains(patientName, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matchingPatients.Count == 0)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai trouvé aucun patient correspondant à « {patientName} » dans votre cabinet."
                };
            }

            if (matchingPatients.Count == 1)
            {
                var patient = matchingPatients[0];
                return new AIActionResult
                {
                    ShouldExecuteAction = true,
                    ActionType = "search_patient",
                    ResponseMessage = $"Patient trouvé : {patient.FirstName} {patient.LastName}\n" +
                                    $"Date de naissance : {patient.DateOfBirth:yyyy-MM-dd}\n" +
                                    $"E-mail : {patient.Email.Value}\n" +
                                    $"Téléphone : {patient.PhoneNumber.Value}"
                };
            }

            var patientList = string.Join("\n", matchingPatients.Select(p => $"- {p.FirstName} {p.LastName}"));
            return new AIActionResult
            {
                ShouldExecuteAction = true,
                ActionType = "search_patient",
                ResponseMessage = $"{matchingPatients.Count} patients correspondant à « {patientName} » :\n{patientList}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching patient via AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur lors de la recherche : {ex.Message}"
            };
        }
    }

    private async Task<AIActionResult> ViewPatientActionAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!parameters.TryGetValue("patient_name", out var patientNameObj))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "J'ai besoin d'un nom de patient pour afficher les détails. Veuillez indiquer le nom du patient."
                };
            }

            var patientName = patientNameObj.ToString() ?? "";
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Authentification requise."
                };
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Utilisateur introuvable."
                };
            }

            var clinicId = user.ClinicId;
            // Archived patients are excluded — the assistant must not find what the UI's own search hides.
                var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
            var patient = patients.FirstOrDefault(p =>
                $"{p.FirstName} {p.LastName}".Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.FirstName.Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.LastName.Equals(patientName, StringComparison.OrdinalIgnoreCase));

            if (patient == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai trouvé aucun patient nommé « {patientName} » dans votre cabinet."
                };
            }

            var details = $"Détails du patient :\n" +
                         $"Nom : {patient.FirstName} {patient.LastName}\n" +
                         $"Date de naissance : {patient.DateOfBirth:yyyy-MM-dd}\n" +
                         $"Sexe : {patient.Gender ?? "Non précisé"}\n" +
                         $"E-mail : {patient.Email.Value}\n" +
                         $"Téléphone : {patient.PhoneNumber.Value}";

            if (!string.IsNullOrEmpty(patient.MedicalHistory))
            {
                details += $"\nAntécédents médicaux : {patient.MedicalHistory}";
            }

            if (!string.IsNullOrEmpty(patient.Allergies))
            {
                details += $"\nAllergies : {patient.Allergies}";
            }

            return new AIActionResult
            {
                ShouldExecuteAction = true,
                ActionType = "view_patient",
                ResponseMessage = details
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error viewing patient via AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur : {ex.Message}"
            };
        }
    }

    private async Task<AIActionResult> ListAppointmentsActionAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Authentification requise."
                };
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Utilisateur introuvable."
                };
            }

            var clinicId = user.ClinicId;
            DateTime? startDate = null;
            DateTime? endDate = null;
            Guid? patientId = null;

            // Parse date if provided
            if (parameters.TryGetValue("date", out var dateObj))
            {
                var dateStr = dateObj.ToString() ?? "";
                if (TryParseDate(dateStr, out var targetDate))
                {
                    startDate = targetDate.Date;
                    endDate = targetDate.Date.AddDays(1);
                }
            }

            // Find patient if provided
            if (parameters.TryGetValue("patient_name", out var patientNameObj))
            {
                var patientName = patientNameObj.ToString() ?? "";
                // Archived patients are excluded — the assistant must not find what the UI's own search hides.
                var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
                var patient = patients.FirstOrDefault(p =>
                    $"{p.FirstName} {p.LastName}".Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                    p.FirstName.Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                    p.LastName.Equals(patientName, StringComparison.OrdinalIgnoreCase));

                if (patient != null)
                {
                    patientId = patient.Id;
                }
            }

            var appointments = await _appointmentRepository.GetByClinicIdAsync(clinicId, startDate, endDate, cancellationToken: cancellationToken);

            if (patientId.HasValue)
            {
                appointments = appointments.Where(a => a.PatientId == patientId.Value);
            }

            var appointmentList = appointments.OrderBy(a => a.AppointmentDateTime).ToList();

            if (appointmentList.Count == 0)
            {
                var dateInfo = startDate.HasValue ? $" pour le {startDate.Value:yyyy-MM-dd}" : "";
                var patientInfo = patientId.HasValue ? " pour le patient indiqué" : "";
                return new AIActionResult
                {
                    ShouldExecuteAction = true,
                    ActionType = "list_appointments",
                    ResponseMessage = $"Aucun rendez-vous trouvé{dateInfo}{patientInfo}."
                };
            }

            var response = $"{appointmentList.Count} rendez-vous trouvé(s) :\n\n";
            foreach (var appointment in appointmentList)
            {
                var patientName = appointment.Patient?.GetFullName() ?? "Occupé";
                var dateTime = appointment.AppointmentDateTime.ToString("yyyy-MM-dd HH:mm");
                var procedure = !string.IsNullOrEmpty(appointment.ProcedureType?.Name) ? $" ({appointment.ProcedureType.Name})" : "";
                response += $"• {patientName} - {dateTime}{procedure}\n";
            }

            return new AIActionResult
            {
                ShouldExecuteAction = true,
                ActionType = "list_appointments",
                ResponseMessage = response.Trim()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing appointments via AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur : {ex.Message}"
            };
        }
    }

    private async Task<AIActionResult> CancelAppointmentActionAsync(
        Dictionary<string, object> parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!parameters.TryGetValue("patient_name", out var patientNameObj))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "J'ai besoin d'un nom de patient pour annuler un rendez-vous. Veuillez indiquer le nom du patient."
                };
            }

            var patientName = patientNameObj.ToString() ?? "";
            var userId = _clinicContext.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Authentification requise."
                };
            }

            var user = await _userRepository.GetByAuth0SubAsync(userId, cancellationToken);
            if (user == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = "Utilisateur introuvable."
                };
            }

            var clinicId = user.ClinicId;

            // Find patient
            // Archived patients are excluded — the assistant must not find what the UI's own search hides.
                var patients = await _patientRepository.GetByClinicIdAsync(clinicId, cancellationToken: cancellationToken);
            var patient = patients.FirstOrDefault(p =>
                $"{p.FirstName} {p.LastName}".Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.FirstName.Equals(patientName, StringComparison.OrdinalIgnoreCase) ||
                p.LastName.Equals(patientName, StringComparison.OrdinalIgnoreCase));

            if (patient == null)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai trouvé aucun patient nommé « {patientName} » dans votre cabinet."
                };
            }

            // Find appointment(s)
            DateTime? targetDate = null;
            int? targetHour = null;
            int? targetMinute = null;

            if (parameters.TryGetValue("date", out var dateObj))
            {
                var dateStr = dateObj.ToString() ?? "";
                if (TryParseDate(dateStr, out var parsedDate))
                {
                    targetDate = parsedDate;
                }
            }

            if (parameters.TryGetValue("time", out var timeObj))
            {
                var timeStr = timeObj.ToString() ?? "";
                if (TryParseTime(timeStr, out var hour, out var minute))
                {
                    targetHour = hour;
                    targetMinute = minute;
                }
            }

            var appointments = await _appointmentRepository.GetByPatientIdAsync(patient.Id, cancellationToken);
            var matchingAppointments = appointments.Where(a =>
                (!targetDate.HasValue || a.AppointmentDateTime.Date == targetDate.Value.Date) &&
                (!targetHour.HasValue || a.AppointmentDateTime.Hour == targetHour.Value) &&
                a.Status != Domain.Enums.AppointmentStatus.Cancelled &&
                a.Status != Domain.Enums.AppointmentStatus.Completed).ToList();

            if (matchingAppointments.Count == 0)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai trouvé aucun rendez-vous à annuler pour {patientName}."
                };
            }

            if (matchingAppointments.Count > 1)
            {
                var appointmentList = string.Join("\n", matchingAppointments.Select(a =>
                    $"- {a.AppointmentDateTime:yyyy-MM-dd HH:mm}"));
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Plusieurs rendez-vous trouvés pour {patientName} :\n{appointmentList}\n\nVeuillez préciser la date et l'heure."
                };
            }

            var appointment = matchingAppointments[0];
            var updateCommand = new UpdateAppointmentCommand
            {
                Id = appointment.Id,
                Status = "Cancelled"
            };

            var result = await _mediator.Send(updateCommand, cancellationToken);

            if (result.IsFailure)
            {
                return new AIActionResult
                {
                    ShouldExecuteAction = false,
                    ResponseMessage = $"Je n'ai pas pu annuler le rendez-vous : {result.Error}"
                };
            }

            return new AIActionResult
            {
                ShouldExecuteAction = true,
                ActionType = "cancel_appointment",
                ResponseMessage = $"✅ Rendez-vous annulé avec succès !\n\n" +
                                $"Patient : {patientName}\n" +
                                $"Date et heure : {appointment.AppointmentDateTime:yyyy-MM-dd HH:mm}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling appointment via AI action");
            return new AIActionResult
            {
                ShouldExecuteAction = false,
                ResponseMessage = $"J'ai rencontré une erreur : {ex.Message}"
            };
        }
    }
}
