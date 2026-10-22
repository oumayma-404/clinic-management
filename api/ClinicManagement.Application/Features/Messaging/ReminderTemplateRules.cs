namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// What a cabinet may save as its reminder wording and timing — checked at save so that, at send, the renderer only
/// ever shortens a name or an act and never the doctor's own text. Each refusal carries its sentence and its code.
/// </summary>
public static class ReminderTemplateRules
{
    public const string UnknownVariableCode = "reminder_template_unknown_variable";
    public const string NeedsDateCode = "reminder_template_needs_date";
    public const string TooLongCode = "reminder_template_too_long";
    public const string LeadTimeCode = "reminder_lead_time_invalid";

    /// <summary>The times a cabinet may choose between (hours before the appointment). Each one sent is one SMS.</summary>
    public static readonly IReadOnlyList<int> AllowedLeadTimeHours = [48, 24, 12, 6, 3, 2];

    /// <summary>At most this many reminders per appointment.</summary>
    public const int MaxLeadTimes = 2;

    /// <summary>A refusal: the French sentence the screen shows, and the code a client branches on.</summary>
    public sealed record Refusal(string Sentence, string Code);

    /// <summary>
    /// Null when <paramref name="template"/> may be saved for a clinic named <paramref name="clinicName"/>. A blank
    /// template is always accepted: it means « the default wording », which fits by construction.
    /// </summary>
    public static Refusal? CheckTemplate(string? template, string clinicName)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return null;
        }

        var unknown = ReminderText.UnknownVariables(template);
        if (unknown.Count > 0)
        {
            return new Refusal(
                $"Variable inconnue dans le message : {string.Join(", ", unknown)}. "
                + $"Utilisez {string.Join(", ", ReminderText.Variables)}.",
                UnknownVariableCode);
        }

        if (!template.Contains(ReminderText.DateVariable, StringComparison.Ordinal))
        {
            return new Refusal(
                $"Le message doit contenir {ReminderText.DateVariable} : un rappel sans la date du rendez-vous n'apprend rien au patient.",
                NeedsDateCode);
        }

        var worstCase = ReminderText.WorstCaseLength(template, clinicName);
        if (worstCase > ReminderText.SmsMaxLength)
        {
            return new Refusal(
                $"Le message dépasse {ReminderText.SmsMaxLength} caractères ({worstCase}). Raccourcissez le texte.",
                TooLongCode);
        }

        return null;
    }

    /// <summary>Null when the chosen times may be saved; null or empty means « the installation's default ».</summary>
    public static Refusal? CheckLeadTimes(IReadOnlyList<int>? leadTimeHours)
    {
        if (leadTimeHours is null || leadTimeHours.Count == 0)
        {
            return null;
        }

        var distinct = leadTimeHours.Distinct().ToList();
        if (distinct.Count > MaxLeadTimes || distinct.Any(h => !AllowedLeadTimeHours.Contains(h)))
        {
            return new Refusal(
                $"Choisissez au plus {MaxLeadTimes} moments d'envoi parmi : "
                + string.Join(", ", AllowedLeadTimeHours.Select(h => $"{h} h")) + " avant le rendez-vous.",
                LeadTimeCode);
        }

        return null;
    }
}
