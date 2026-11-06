using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Platform;

/// <summary>A news e-mail's content once cleaned: a subject and 1–12 emoji + text rows.</summary>
public sealed record PlatformNewsDraft(string Subject, IReadOnlyList<PlatformNewsItemInput> Items)
{
    public static PlatformNewsDraft From(PlatformNewsBroadcast broadcast) =>
        new(broadcast.Subject, broadcast.OrderedItems().Select(i => new PlatformNewsItemInput(i.Emoji, i.Text)).ToList());
}

/// <summary>What the form may contain, and the refusals. Shared by the preview, the test and the send.</summary>
public static partial class PlatformNewsRules
{
    public const string InvalidCode = "news_invalid";
    public const string NoAudienceCode = "news_no_audience";
    public const string EmailNotConfiguredCode = "email_not_configured";

    public const string DefaultEmoji = "✨";

    public const string EmailNotConfigured =
        "L'envoi d'e-mails n'est pas configuré sur ce serveur : aucune nouveauté ne peut partir.";

    public const string NoAudience = "Aucun cabinet actif ou en essai n'a d'administrateur avec une adresse e-mail.";

    /// <summary>Cleans the form for sending: blank rows dropped, whitespace collapsed, every rule enforced.</summary>
    public static Result<PlatformNewsDraft> Normalize(string? subject, IReadOnlyList<PlatformNewsItemRequest>? items)
    {
        var cleanSubject = Collapse(subject);
        if (cleanSubject.Length == 0)
        {
            return Fail("Saisissez l'objet de l'e-mail.");
        }

        if (cleanSubject.Length > PlatformNewsBroadcast.SubjectMaxLength)
        {
            return Fail($"L'objet fait {PlatformNewsBroadcast.SubjectMaxLength} caractères au plus.");
        }

        var rows = new List<PlatformNewsItemInput>();
        foreach (var item in items ?? [])
        {
            var text = Collapse(item.Text);
            if (text.Length == 0)
            {
                continue;
            }

            if (text.Length > PlatformNewsItem.TextMaxLength)
            {
                return Fail($"Une nouveauté fait {PlatformNewsItem.TextMaxLength} caractères au plus.");
            }

            var emoji = Collapse(item.Emoji);
            if (emoji.Length > PlatformNewsItem.EmojiMaxLength)
            {
                return Fail("Choisissez un seul emoji par nouveauté.");
            }

            rows.Add(new PlatformNewsItemInput(emoji.Length == 0 ? DefaultEmoji : emoji, text));
        }

        if (rows.Count == 0)
        {
            return Fail("Ajoutez au moins une nouveauté.");
        }

        if (rows.Count > PlatformNewsBroadcast.MaxItems)
        {
            return Fail($"{PlatformNewsBroadcast.MaxItems} nouveautés au plus par e-mail.");
        }

        return Result<PlatformNewsDraft>.Success(new PlatformNewsDraft(cleanSubject, rows));
    }

    /// <summary>The preview never refuses: an empty form shows placeholders, an over-long one is cut.</summary>
    public static PlatformNewsDraft ForPreview(string? subject, IReadOnlyList<PlatformNewsItemRequest>? items)
    {
        var cleanSubject = Collapse(subject);
        var rows = (items ?? [])
            .Select(i => (Emoji: Collapse(i.Emoji), Text: Collapse(i.Text)))
            .Where(i => i.Text.Length > 0)
            .Take(PlatformNewsBroadcast.MaxItems)
            .Select(i => new PlatformNewsItemInput(
                i.Emoji.Length is 0 or > PlatformNewsItem.EmojiMaxLength ? DefaultEmoji : i.Emoji,
                Cut(i.Text, PlatformNewsItem.TextMaxLength)))
            .ToList();

        return new PlatformNewsDraft(
            cleanSubject.Length == 0 ? "Objet de l'e-mail" : Cut(cleanSubject, PlatformNewsBroadcast.SubjectMaxLength),
            rows.Count == 0 ? [new PlatformNewsItemInput(DefaultEmoji, "Votre première nouveauté apparaîtra ici.")] : rows);
    }

    // Collapsing every whitespace run also removes CR/LF, which a mail subject must never carry.
    private static string Collapse(string? value) => Whitespace().Replace(value ?? string.Empty, " ").Trim();

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    private static Result<PlatformNewsDraft> Fail(string message) =>
        Result<PlatformNewsDraft>.Failure(message, InvalidCode);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Who one send reaches: every admin address once, the cabinets reached, the ones with no address.</summary>
public sealed record PlatformNewsAudiencePlan(
    IReadOnlyList<PlatformNewsRecipientInput> Recipients, int Clinics, int ClinicsWithoutAdminEmail)
{
    public static PlatformNewsAudiencePlan From(IReadOnlyList<PlatformNewsAudienceClinic> clinics)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recipients = new List<PlatformNewsRecipientInput>();
        var withoutAddress = 0;

        foreach (var clinic in clinics)
        {
            if (clinic.Admins.Count == 0)
            {
                withoutAddress++;
                continue;
            }

            foreach (var admin in clinic.Admins)
            {
                if (seen.Add(admin.Email))
                {
                    recipients.Add(new PlatformNewsRecipientInput(clinic.ClinicId, clinic.Name, admin.Email, admin.FullName));
                }
            }
        }

        // Counted from the recipients, so the confirm and the history (distinct cabinets among rows) agree.
        return new PlatformNewsAudiencePlan(
            recipients, recipients.Select(r => r.ClinicId).Distinct().Count(), withoutAddress);
    }
}

/// <summary>The one composer of a news e-mail — the preview, the test and every real send go through it.</summary>
public static class PlatformNewsEmail
{
    public const string Kicker = "Happy Apexing ! 🦷✨";
    public const string Intro = "Voici les nouveautés d'Apexa :";
    public const string Closing = "Apexa, toujours à vos côtés 💙";
    public const string Signature = "L'équipe Apexa";
    public const string ActionLabel = "Ouvrir Apexa";

    /// <summary>The cabinet named in the small print of a preview or a test, which goes to no cabinet.</summary>
    public const string SampleClinicName = "Cabinet Exemple";

    public const string TestSubjectPrefix = "[Test] ";

    public static EmailContent Compose(PlatformNewsDraft draft, string? recipientName, string clinicName, string? appUrl) =>
        new()
        {
            Kicker = Kicker,
            Title = draft.Subject,
            Preheader = $"{Intro} {draft.Items[0].Text}",
            Greeting = EmailGreeting.For(recipientName),
            Intro = [Intro],
            Bullets = draft.Items.Select(i => new EmailBullet(i.Emoji, i.Text)).ToList(),
            Action = string.IsNullOrWhiteSpace(appUrl) ? null : new EmailAction(ActionLabel, appUrl),
            Outro = [Closing, Signature],
            Note = $"Vous recevez cet e-mail en tant qu'administrateur du cabinet {clinicName}.",
        };

    /// <summary>Where « Ouvrir Apexa » leads, or null (no button) on an install with no public address.</summary>
    public static string? AppLink(IPublicAppUrlProvider appUrl) =>
        appUrl.IsConfigured ? $"{appUrl.BaseUrl.TrimEnd('/')}/login" : null;

    /// <summary>The real HTML with the lockup inlined as a data URI, since a browser cannot resolve <c>cid:</c>.</summary>
    public static string PreviewHtml(EmailContent content) =>
        EmailLayout.Html(content).Replace(
            $"cid:{EmailLayout.LogoContentId}",
            "data:image/png;base64," + Convert.ToBase64String(EmailLayout.LogoPng.Span),
            StringComparison.Ordinal);
}
