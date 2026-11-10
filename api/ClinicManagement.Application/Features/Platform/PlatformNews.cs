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

/// <summary>A news e-mail's content once cleaned: a subject and 1–12 emoji + text rows, each with its optional guide.</summary>
public sealed record PlatformNewsDraft(string Subject, IReadOnlyList<PlatformNewsItemInput> Items)
{
    /// <summary>What a resumed send composes from. ⚠️ Carries the guide: a copy stopping at the text would mail it bare.</summary>
    public static PlatformNewsDraft From(PlatformNewsBroadcast broadcast) =>
        new(
            broadcast.Subject,
            broadcast.OrderedItems()
                .Select(i => new PlatformNewsItemInput(i.Emoji, i.Text, i.Description, i.Steps.ToList()))
                .ToList());
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
            var description = Collapse(item.Description);
            var steps = GuideSteps(item.Steps);

            if (text.Length == 0)
            {
                // A blank row is dropped — but not one carrying a guide, which would vanish with it unseen.
                if (description.Length > 0 || steps.Count > 0)
                {
                    return Fail("Une nouveauté a un guide mais pas de texte : écrivez sa ligne ou videz son guide.");
                }

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

            if (description.Length > PlatformNewsItem.DescriptionMaxLength)
            {
                return Fail($"La description d'un guide fait {PlatformNewsItem.DescriptionMaxLength} caractères au plus.");
            }

            if (steps.Count > PlatformNewsItem.MaxSteps)
            {
                return Fail($"Un guide a {PlatformNewsItem.MaxSteps} étapes au plus.");
            }

            if (steps.Any(s => s.Length > PlatformNewsItem.StepMaxLength))
            {
                return Fail($"Une étape d'un guide fait {PlatformNewsItem.StepMaxLength} caractères au plus.");
            }

            rows.Add(new PlatformNewsItemInput(
                emoji.Length == 0 ? DefaultEmoji : emoji, text, description.Length == 0 ? null : description, steps));
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
            .Select(i => (Emoji: Collapse(i.Emoji), Text: Collapse(i.Text), Description: Collapse(i.Description), i.Steps))
            .Where(i => i.Text.Length > 0)
            .Take(PlatformNewsBroadcast.MaxItems)
            .Select(i => new PlatformNewsItemInput(
                i.Emoji.Length is 0 or > PlatformNewsItem.EmojiMaxLength ? DefaultEmoji : i.Emoji,
                Cut(i.Text, PlatformNewsItem.TextMaxLength),
                i.Description.Length == 0 ? null : Cut(i.Description, PlatformNewsItem.DescriptionMaxLength),
                GuideSteps(i.Steps)
                    .Take(PlatformNewsItem.MaxSteps)
                    .Select(s => Cut(s, PlatformNewsItem.StepMaxLength))
                    .ToList()))
            .ToList();

        return new PlatformNewsDraft(
            cleanSubject.Length == 0 ? "Objet de l'e-mail" : Cut(cleanSubject, PlatformNewsBroadcast.SubjectMaxLength),
            rows.Count == 0 ? [new PlatformNewsItemInput(DefaultEmoji, "Votre première nouveauté apparaîtra ici.")] : rows);
    }

    // Collapsing every whitespace run also removes CR/LF, which a mail subject must never carry.
    private static string Collapse(string? value) => Whitespace().Replace(value ?? string.Empty, " ").Trim();

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>A guide's steps, each collapsed to one line, blank ones dropped, in the order typed.</summary>
    private static List<string> GuideSteps(IReadOnlyList<string?>? steps) =>
        (steps ?? []).Select(Collapse).Where(s => s.Length > 0).ToList();

    private static Result<PlatformNewsDraft> Fail(string message) =>
        Result<PlatformNewsDraft>.Failure(message, InvalidCode);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Who one send reaches: every admin address once, the cabinets reached, the ones with no address.</summary>
public sealed record PlatformNewsAudiencePlan(
    IReadOnlyList<PlatformNewsRecipientInput> Recipients, int Clinics, IReadOnlyList<string> SkippedClinics)
{
    public int ClinicsWithoutAdminEmail => SkippedClinics.Count;

    public static PlatformNewsAudiencePlan From(IReadOnlyList<PlatformNewsAudienceClinic> clinics)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recipients = new List<PlatformNewsRecipientInput>();
        var withoutAddress = new List<string>();

        foreach (var clinic in clinics)
        {
            if (clinic.Admins.Count == 0)
            {
                withoutAddress.Add(clinic.Name);
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

/// <summary>
/// The vendor's WhatsApp line, closing every news: the number as configured, and its <c>wa.me</c> link.
/// <para>Read from « Abonnement »'s own settings (<c>Subscription:WhatsAppPhone</c>, else <c>ContactPhone</c>) — the
/// number the app's « Aide » menu opens — so the e-mail and the app can never name two different lines.</para>
/// </summary>
/// <param name="Number">The number as the operator wrote it (« +216 51 230 456 »), for reading.</param>
/// <param name="Link">The <c>wa.me</c> link: E.164 digits only, the format's own rule.</param>
public sealed record PlatformNewsContact(string Number, string Link)
{
    /// <summary>Null when no number is configured or none parses — never a link onto WhatsApp's error page.</summary>
    public static PlatformNewsContact? From(ISubscriptionPricing pricing)
    {
        var number = string.IsNullOrWhiteSpace(pricing.WhatsAppPhone) ? pricing.ContactPhone : pricing.WhatsAppPhone;
        var e164 = PhoneNumber.ToE164(number);
        return e164 is null ? null : new PlatformNewsContact(number!.Trim(), $"https://wa.me/{e164.TrimStart('+')}");
    }

    /// <summary>The link with the message the « Aide » menu pre-fills, so the vendor knows which cabinet writes.</summary>
    public string UrlFor(string clinicName) =>
        $"{Link}?text={Uri.EscapeDataString($"Bonjour, ici {clinicName}.")}";
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

    /// <summary>The heading over a row's numbered steps; shown only when the row has steps.</summary>
    public const string GuideHeading = "Comment ça marche :";

    /// <summary>The closing line of every news, above the WhatsApp link (2026-11-10).</summary>
    public const string ContactPrompt = "Une question ? Écrivez-nous sur WhatsApp";

    /// <param name="whatsApp">The vendor's WhatsApp line, or null — then no closing contact is rendered at all.</param>
    public static EmailContent Compose(
        PlatformNewsDraft draft, string? recipientName, string clinicName, string? appUrl, PlatformNewsContact? whatsApp) =>
        new()
        {
            Kicker = Kicker,
            Title = draft.Subject,
            Preheader = $"{Intro} {draft.Items[0].Text}",
            Greeting = EmailGreeting.For(recipientName),
            Intro = [Intro],
            Bullets = draft.Items.Select(ToBullet).ToList(),
            Action = string.IsNullOrWhiteSpace(appUrl) ? null : new EmailAction(ActionLabel, appUrl),
            Contact = whatsApp is null
                ? null
                : new EmailContact(ContactPrompt, $"💬 WhatsApp {whatsApp.Number}", whatsApp.UrlFor(clinicName)),
            Outro = [Closing, Signature],
            Note = $"Vous recevez cet e-mail en tant qu'administrateur du cabinet {clinicName}.",
        };

    // A row with no guide is the two-field bullet every news e-mail had before guides existed (AC-10).
    private static EmailBullet ToBullet(PlatformNewsItemInput item)
    {
        var steps = item.Steps ?? [];
        return new EmailBullet(
            item.Emoji, item.Text, item.Description, steps.Count > 0 ? GuideHeading : null, steps);
    }

    /// <summary>Where « Ouvrir Apexa » leads, or null (no button) on an install with no public address.</summary>
    public static string? AppLink(IPublicAppUrlProvider appUrl) =>
        appUrl.IsConfigured ? $"{appUrl.BaseUrl.TrimEnd('/')}/nouveautes" : null;

    /// <summary>The real HTML with the lockup inlined as a data URI, since a browser cannot resolve <c>cid:</c>.</summary>
    public static string PreviewHtml(EmailContent content) =>
        EmailLayout.Html(content).Replace(
            $"cid:{EmailLayout.LogoContentId}",
            "data:image/png;base64," + Convert.ToBase64String(EmailLayout.LogoPng.Span),
            StringComparison.Ordinal);
}
