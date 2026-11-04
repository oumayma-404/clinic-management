using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Feedback;

/// <summary>What the console team is told about one message (« Retours » keeps it too).</summary>
public sealed record FeedbackNoticeFacts(
    string ClinicName,
    string AuthorName,
    string AuthorRole,
    string? AuthorEmail,
    ProductFeedback Feedback);

/// <summary>
/// E-mails the console team the moment a cabinet's person sends feedback (guided-first-days E) —
/// <c>ConsoleTeamSignupNotice</c>'s shape: one message per active console account, best-effort, never thrown, the
/// person's address only when they agreed to be contacted, and no address in a log template.
/// </summary>
public static class ConsoleTeamFeedbackNotice
{
    public const string Subject = "Nouvel avis sur APEXA";

    public static async Task SendAsync(
        IPlatformAccountRepository platformAccounts,
        ITransactionalEmailSender emailSender,
        FeedbackNoticeFacts facts,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> recipients;
        try
        {
            recipients = await platformAccounts.GetActiveEmailsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the console accounts for the feedback notice.");
            return;
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("Feedback notice not sent: no active console account.");
            return;
        }

        var content = Compose(facts);
        foreach (var recipient in recipients)
        {
            try
            {
                var sent = await emailSender.SendAsync(recipient, Subject, content, cancellationToken);
                if (sent.Outcome != TransactionalEmailOutcome.Sent)
                {
                    logger.LogWarning(
                        "Feedback notice not sent to a console account: {Outcome} {Reason}", sent.Outcome, sent.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Feedback notice failed for a console account.");
            }
        }
    }

    public static EmailContent Compose(FeedbackNoticeFacts facts)
    {
        var f = facts.Feedback;
        var details = new List<EmailDetail>
        {
            new("Cabinet", facts.ClinicName),
            new("De", $"{facts.AuthorName} ({FeedbackRules.RoleLabel(facts.AuthorRole)})"),
            new("Type", FeedbackRules.KindLabels.GetValueOrDefault(f.Kind, f.Kind)),
        };

        if (f.HelpRating is { } help) details.Add(new("APEXA m'aide", $"{help} / 5"));
        if (f.EaseRating is { } ease) details.Add(new("Facile à utiliser", $"{ease} / 5"));
        if (f.LikedAreas().Count > 0) details.Add(new("Aimé", Labels(f.LikedAreas())));
        if (f.LikedOther is { } likedOther) details.Add(new("Aimé (autre)", likedOther));
        if (f.HardAreas().Count > 0) details.Add(new("Difficile", Labels(f.HardAreas())));
        if (f.HardOther is { } hardOther) details.Add(new("Difficile (autre)", hardOther));
        if (f.Message is { } message) details.Add(new("Message", message));
        if (f.FromScreen is { } screen) details.Add(new("Écran", screen));
        details.Add(new("Recontacter", f.MayContact && facts.AuthorEmail is { } email ? email : "non"));

        return new EmailContent
        {
            Title = "Nouvel avis",
            Preheader = $"{facts.ClinicName} a envoyé un avis.",
            Intro = ["Un cabinet vient d'envoyer un avis. Message interne, envoyé aux comptes de la console uniquement."],
            Details = details,
        };
    }

    private static string Labels(IEnumerable<string> areas) =>
        string.Join(", ", areas.Select(a => FeedbackRules.AreaLabels.GetValueOrDefault(a, a)));
}
