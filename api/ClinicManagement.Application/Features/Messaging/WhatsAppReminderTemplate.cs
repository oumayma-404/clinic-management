namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// The French reminder template this product submits on a cabinet's behalf (FR-7, AC-1.3) — the one definition of
/// its name, language, category and body, so the service that submits it, the poll that reads it back and the
/// column the sender resolves its name from cannot disagree.
///
/// <para><b>⚠️ Exactly one body variable, and it is neither first nor last.</b> Meta documents a body opening or
/// closing on a variable as a rejection cause, and <c>WhatsAppSender</c> sends <b>one</b> parameter carrying the
/// whole pre-rendered French sentence — a second variable would be « #132000 number of params does not match » on
/// every send. Formatting inside the sender instead would move the wording away from <c>ReminderScheduler</c>,
/// which is where <c>ReminderMessage.AnnouncesStaleMoment</c> reads it from to catch a moved appointment (L3b).</para>
///
/// <para>⚠️ The name is stored per cabinet on submission rather than left to inherit
/// <c>Reminders:WhatsApp:TemplateName</c>: a compiled constant that only works while an operator's config happens
/// to match it is a drift waiting to happen, and each cabinet's WABA holds its own copy of this template.</para>
/// </summary>
public static class WhatsAppReminderTemplate
{
    /// <summary>Meta template names are lowercase, digits and underscores only.</summary>
    public const string Name = "rappel_rendez_vous";

    public const string Language = "fr";

    /// <summary>
    /// The category we ask for. Meta may <b>approve it as something else</b> (auto-recategorisation, 9 April 2025),
    /// which is why the granted category is stored and reported rather than assumed — see FR-7b.
    /// </summary>
    public const string Category = "UTILITY";

    /// <summary><c>{{1}}</c> receives the rendered reminder text.</summary>
    public const string Body = "Bonjour, {{1}} À bientôt, votre cabinet dentaire.";

    /// <summary>What Meta shows as the sample value while reviewing the template.</summary>
    public const string BodyExample =
        "Rappel : Ahmed Ben Salah, vous avez un rendez-vous le 12/08/2026 à 09:00 chez Cabinet Dentaire Al Amal.";
}
