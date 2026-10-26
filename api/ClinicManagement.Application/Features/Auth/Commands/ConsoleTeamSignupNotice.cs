using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Auth.Commands;

/// <summary>Who just created a cabinet, as the vendor team needs it to call them back.</summary>
public sealed record SignupNoticeFacts(string ClinicName, string FullName, string Email, string? Phone);

/// <summary>
/// Tells the vendor team a cabinet has just signed itself up.
/// ⚠️ Recipients are the active console accounts ONLY — never the visitor, never a clinic user, never a config list.
/// </summary>
public static class ConsoleTeamSignupNotice
{
    // Fixed on purpose: the cabinet name is a stranger's input and stays out of the mail headers.
    public const string Subject = "Nouveau cabinet inscrit sur APEXA";

    /// <summary>Best-effort: the cabinet is already committed, so nothing here may throw.</summary>
    public static async Task SendAsync(
        IPlatformAccountRepository platformAccounts,
        ITransactionalEmailSender emailSender,
        SignupNoticeFacts facts,
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
            logger.LogError(ex, "Could not read the console accounts for the new-cabinet notice.");
            return;
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("New-cabinet notice not sent: no active console account.");
            return;
        }

        var content = Compose(facts);

        // One message per recipient, so no team member's address is shown to another.
        foreach (var recipient in recipients)
        {
            try
            {
                var sent = await emailSender.SendAsync(recipient, Subject, content, cancellationToken);
                if (sent.Outcome != TransactionalEmailOutcome.Sent)
                {
                    logger.LogWarning(
                        "New-cabinet notice not sent to a console account: {Outcome} {Reason}",
                        sent.Outcome, sent.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "New-cabinet notice failed for a console account.");
            }
        }
    }

    public static EmailContent Compose(SignupNoticeFacts facts) => new()
    {
        Title = "Nouveau cabinet inscrit",
        Preheader = $"{facts.ClinicName} vient de créer son compte APEXA.",
        Intro =
        [
            "Un cabinet vient de confirmer son inscription. Message interne, envoyé aux comptes de la "
            + "console uniquement."
        ],
        Details =
        [
            new EmailDetail("Cabinet", facts.ClinicName),
            new EmailDetail("Nom", facts.FullName),
            new EmailDetail("E-mail", facts.Email),
            new EmailDetail("Téléphone", string.IsNullOrWhiteSpace(facts.Phone) ? "non renseigné" : facts.Phone.Trim())
        ]
    };
}
