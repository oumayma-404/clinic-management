using System.Globalization;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Common.SignIn;

/// <summary>
/// The vendor's e-mail about a refused sign-in, sent through the alert channel the PC de secours and the backups
/// already use (<see cref="VendorAlertRecipients"/>) — one inbox for every vendor alert.
/// ⚠️ It prints <see cref="SignInRefusal.ShownEmail"/>, never <see cref="SignInRefusal.EmailKey"/>: what was typed may
/// be a password.
/// </summary>
public static class SignInAlertNotice
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    // Fixed per kind: the address and the IP are a stranger's input and stay out of the mail headers.
    public static string Subject(SignInAlertKind kind) => $"APEXA — {Title(kind)}";

    public static string Title(SignInAlertKind kind) => kind switch
    {
        SignInAlertKind.ConsoleRefused => "connexion refusée sur la console",
        SignInAlertKind.AccountUnderAttack => "essais répétés sur un compte",
        SignInAlertKind.AddressGuessing => "essais de connexion en série",
        SignInAlertKind.UnknownEmailRepeated => "essais avec un e-mail sans compte",
        SignInAlertKind.DeactivatedAccountTried => "un compte désactivé a essayé de se connecter",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string DoorLabel(SignInDoor door) => door switch
    {
        SignInDoor.ClinicSignIn => "connexion à l'application",
        SignInDoor.ClinicSecondFactorSetup => "activation du code de vérification (application)",
        SignInDoor.ClinicRecoveryCode => "code de secours (application)",
        SignInDoor.ConsoleSignIn => "connexion à la console",
        SignInDoor.ConsoleSecondFactorSetup => "activation du code de vérification (console)",
        SignInDoor.ConsoleRecoveryCode => "code de secours (console)",
        _ => throw new ArgumentOutOfRangeException(nameof(door), door, null),
    };

    public static string ReasonLabel(SignInRefusalReason reason) => reason switch
    {
        SignInRefusalReason.EmptyFields => "champs vides",
        SignInRefusalReason.UnknownEmail => "aucun compte avec cet e-mail",
        SignInRefusalReason.WrongSecret => "mot de passe ou code incorrect",
        SignInRefusalReason.Locked => "trop d'essais, bloqué pour l'instant",
        SignInRefusalReason.Deactivated => "compte désactivé",
        SignInRefusalReason.PendingActivation => "compte en attente d'activation",
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    };

    public static EmailContent Compose(SignInAlert alert)
    {
        var refusal = alert.Refusal;
        var (intro, outro) = alert.Kind switch
        {
            SignInAlertKind.ConsoleRefused => (
                "Une connexion à la console a été refusée.",
                "Si ce n'était pas vous, changez le mot de passe de la console."),
            SignInAlertKind.AccountUnderAttack => (
                $"{alert.Attempts} essais refusés sur le même compte en moins de 15 minutes.",
                "Après 5 essais, cette adresse est bloquée sur ce compte pendant 15 minutes. Si c'est la personne "
                + "elle-même, « Mot de passe oublié » lui permet de rentrer."),
            SignInAlertKind.AddressGuessing => (
                $"{alert.Attempts} connexions refusées depuis la même adresse en moins de 15 minutes.",
                "Plusieurs comptes ou e-mails essayés depuis un même endroit : souvent un robot. Le serveur freine "
                + "de lui-même une adresse qui insiste."),
            SignInAlertKind.UnknownEmailRepeated => (
                $"{alert.Attempts} essais en moins de 15 minutes avec un e-mail qui n'a pas de compte.",
                "Souvent une personne qui se trompe d'adresse, ou qui ne s'est pas encore inscrite."),
            SignInAlertKind.DeactivatedAccountTried => (
                "Un compte désactivé a essayé de se connecter avec le bon mot de passe. L'accès a été refusé.",
                "Si ce n'est pas normal, prévenez le cabinet."),
            _ => throw new ArgumentOutOfRangeException(nameof(alert), alert.Kind, null),
        };

        return new EmailContent
        {
            Title = char.ToUpper(Title(alert.Kind)[0], French) + Title(alert.Kind)[1..],
            Preheader = intro,
            Intro = [intro],
            Details = Details(alert).ToList(),
            Outro = [outro],
            Note = "Message interne, envoyé aux comptes de la console uniquement. Au plus un message par heure pour un même cas.",
        };
    }

    private static IEnumerable<EmailDetail> Details(SignInAlert alert)
    {
        var refusal = alert.Refusal;
        yield return new EmailDetail(
            "Quand", ClinicClock.ToClinicLocal(refusal.OccurredAtUtc).ToString("dd/MM/yyyy 'à' HH:mm", French));
        yield return new EmailDetail("Où", DoorLabel(refusal.Door));
        yield return new EmailDetail("E-mail saisi", refusal.ShownEmail);

        // With no account, the reason below already says so.
        if (refusal.AccountLabel is not null)
        {
            yield return new EmailDetail("Compte", refusal.AccountLabel);
        }

        yield return new EmailDetail("Adresse IP", alert.ClientAddress);
        yield return new EmailDetail("Raison", ReasonLabel(refusal.Reason));
    }

    /// <summary>Best-effort: the refusal has long been answered, so nothing here may throw.</summary>
    public static async Task SendAsync(
        IPlatformAccountRepository accounts,
        ITransactionalEmailSender emailSender,
        string? configuredRecipients,
        SignInAlert alert,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
        {
            logger.LogWarning("Sign-in alert {Kind} not e-mailed: no SMTP is configured on this deployment.", alert.Kind);
            return;
        }

        IReadOnlyList<string> recipients;
        try
        {
            recipients = await VendorAlertRecipients.ResolveAsync(configuredRecipients, accounts, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not read the console accounts for a sign-in alert.");
            return;
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("Sign-in alert {Kind} not e-mailed: {Key} is empty and there is no active console account.",
                alert.Kind, VendorAlertRecipients.AlertEmailKey);
            return;
        }

        var subject = Subject(alert.Kind);
        var content = Compose(alert);

        // One message per recipient, so no address is shown to another.
        foreach (var recipient in recipients)
        {
            try
            {
                var sent = await emailSender.SendAsync(recipient, subject, content, cancellationToken);
                if (sent.Outcome != TransactionalEmailOutcome.Sent)
                {
                    logger.LogWarning("Sign-in alert not sent to a console recipient: {Outcome} {Reason}",
                        sent.Outcome, sent.Error);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Sign-in alert failed for a console recipient.");
            }
        }
    }
}
