using System.Security.Cryptography;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Users;

/// <summary>What an invitation produced: whether the e-mail left, and the link itself (shown to the admin once).</summary>
public sealed record StaffInvitationIssued(bool Emailed, string Url);

/// <summary>
/// Issuing a colleague's first-password link (<c>guided-first-days</c> C) — one owner for the account creation and
/// « Renvoyer l'e-mail », so the token, the link and the message cannot differ between the two.
///
/// <para>⚠️ <b>The link is returned to the administrator as well as e-mailed</b>, once: a cabinet whose mail did not
/// leave (no SMTP, a typo in the address, a spam folder) still has a way to get the colleague in — « Copier le lien ».
/// The token is never stored, only its hash, so a second copy cannot be read back later; « Renvoyer » re-arms.</para>
/// </summary>
public static class StaffInvitations
{
    /// <summary>The unique refusal a used, expired, re-armed or unknown link meets — one sentence, one code.</summary>
    public const string UnusableLink =
        "Ce lien d'invitation n'est plus valable. Demandez à l'administrateur du cabinet de vous le renvoyer.";

    public const string InvalidCode = "invitation_invalid";

    /// <summary>Well under the browser's 20 s request timeout, so a hung mail server still lets the admin copy the link.</summary>
    public static readonly TimeSpan SendBudget = TimeSpan.FromSeconds(8);

    /// <summary>32 bytes from the OS CSPRNG, URL-safe — the password reset's token, for the same reason.</summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>In the fragment, never the query: a fragment is not sent to the server or written to an access log.</summary>
    public static string LinkFor(string baseUrl, string rawToken) =>
        $"{baseUrl.TrimEnd('/')}/invitation#t={Uri.EscapeDataString(rawToken)}";

    /// <summary>
    /// Creates or re-arms the account's invitation (staged, the caller saves) and returns the raw token. A re-arm kills
    /// the previous link: its hash is overwritten.
    /// </summary>
    public static async Task<string> ArmAsync(
        IStaffInvitationRepository invitations, string userId, string adminUserId, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var token = NewToken();
        var hash = StaffInvitation.HashToken(token);
        var existing = await invitations.GetByUserIdAsync(userId, cancellationToken);
        if (existing is null)
        {
            await invitations.AddAsync(StaffInvitation.Create(userId, adminUserId, hash, nowUtc), cancellationToken);
        }
        else
        {
            existing.Rearm(hash, adminUserId, nowUtc);
            await invitations.UpdateAsync(existing, cancellationToken);
        }

        return token;
    }

    /// <summary>After the commit, best-effort: a failed e-mail is reported to the admin, never thrown.</summary>
    public static async Task<bool> SendAsync(
        ITransactionalEmailSender emailSender,
        string to,
        StaffInvitationFacts facts,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
        {
            logger.LogWarning("Staff invitation e-mail not sent: the transactional e-mail sender is not configured.");
            return false;
        }

        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(SendBudget);
            var sent = await emailSender.SendAsync(to, Subject(facts.AdminName), Compose(facts), budget.Token);
            if (sent.Outcome == TransactionalEmailOutcome.Sent)
            {
                return true;
            }

            logger.LogWarning("Staff invitation e-mail not sent: {Outcome} {Reason}", sent.Outcome, sent.Error);
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Staff invitation e-mail not sent within {Budget}.", SendBudget);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Staff invitation e-mail failed.");
            return false;
        }
    }

    /// <summary>
    /// « {Admin} vous invite sur APEXA ». A name is typed text, so control characters go (a line break in a mail
    /// header is refused by the sender, or worse) and it is capped; an empty result falls back to « Votre cabinet ».
    /// </summary>
    public static string Subject(string adminName)
    {
        var spaced = new string((adminName ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        var name = string.Join(' ', spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 60) name = name[..60].TrimEnd();
        return $"{(name.Length == 0 ? "Votre cabinet" : name)} vous invite sur APEXA";
    }

    public static EmailContent Compose(StaffInvitationFacts facts) => new()
    {
        Title = "Vous êtes invité(e) sur APEXA",
        Preheader = $"{facts.AdminName} vous a créé un compte au cabinet {facts.ClinicName}.",
        Greeting = EmailGreeting.For(facts.FullName),
        Intro =
        [
            $"{facts.AdminName} vous a créé un compte APEXA au cabinet {facts.ClinicName}. "
            + "Choisissez votre mot de passe pour commencer."
        ],
        Action = new EmailAction("Choisir mon mot de passe", facts.InvitationUrl),
        Details =
        [
            new EmailDetail("Votre identifiant", facts.Email),
            new EmailDetail("Au cabinet", facts.LoginUrl, IsLink: true),
            new EmailDetail("Sur votre téléphone", ApexaPublicLinks.InstallPage, IsLink: true),
            new EmailDetail("Le lien, si le bouton ne marche pas", facts.InvitationUrl, IsLink: true)
        ],
        Outro =
        [
            "Sur un téléphone Android, installez l'application depuis la page ci-dessus. Sur un iPhone, ouvrez "
            + "l'adresse du cabinet dans Safari, touchez « Partager » puis « Sur l'écran d'accueil »."
        ],
        Note = "Le lien est valable 7 jours et ne sert qu'une fois."
    };
}

/// <summary>What the invitation e-mail says, gathered by its caller.</summary>
public sealed record StaffInvitationFacts(
    string AdminName,
    string ClinicName,
    string FullName,
    string Email,
    string InvitationUrl,
    string LoginUrl);
