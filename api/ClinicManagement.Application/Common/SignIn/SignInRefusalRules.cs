using System.Text.RegularExpressions;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Platform.Auth;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Common.SignIn;

/// <summary>
/// What counts as a refused sign-in, how its address is shown, and when the vendor hears of it. Pure, so each rule is
/// a test rather than a reading of the recorder that applies it.
/// </summary>
public static partial class SignInRefusalRules
{
    /// <summary>How long a run of refusals is remembered — the per-source lockout's own window.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>Refusals on one existing account before the vendor is told: where the per-source lockout bites.</summary>
    public const int AccountThreshold = 5;

    /// <summary>Refusals from one address, whatever the account.</summary>
    public const int AddressThreshold = 10;

    /// <summary>Refusals for one address that has no account.</summary>
    public const int UnknownEmailThreshold = 3;

    /// <summary>A ceiling on e-mails, so an attack spread over many addresses is a handful of messages, not an inbox.</summary>
    public const int MaxAlertsPerHour = 10;

    /// <summary>What a blank address reads as.</summary>
    public const string Blank = "(vide)";

    /// <summary>What text that is not an address reads as: nothing of it is written down.</summary>
    public const string NotAnAddress = "(pas une adresse e-mail)";

    public const string ConsoleAccountLabel = "compte console";

    // The longest address SMTP carries (RFC 5321).
    private const int MaxAddressLength = 254;

    // Both populations spell these the same today; a set keeps the console's refusals recorded if one side renames.
    private static readonly HashSet<string> WrongSecretCodes = new(StringComparer.Ordinal)
    {
        ClinicAuthRefusals.InvalidCredentials,
        ClinicAuthRefusals.TotpInvalid,
        PlatformAuthRefusals.InvalidCredentials,
        PlatformAuthRefusals.TotpInvalid,
    };

    private static readonly HashSet<string> LockedCodes = new(StringComparer.Ordinal)
    {
        ClinicAuthRefusals.TooManyAttempts,
        PlatformAuthRefusals.TooManyAttempts,
    };

    private static readonly HashSet<string> DisabledCodes = new(StringComparer.Ordinal)
    {
        ClinicAuthRefusals.AccountDisabled,
        PlatformAuthRefusals.AccountDisabled,
    };

    public static bool IsConsole(this SignInDoor door) =>
        door is SignInDoor.ConsoleSignIn or SignInDoor.ConsoleSecondFactorSetup or SignInDoor.ConsoleRecoveryCode;

    /// <summary>
    /// False for an answer that is a step of signing in rather than a refusal — « code requis », « enrôlez-vous » —
    /// or a rule that has nothing to do with credentials, such as a retired PC de secours.
    /// </summary>
    public static bool IsRecorded(string code) =>
        WrongSecretCodes.Contains(code) || LockedCodes.Contains(code) || DisabledCodes.Contains(code);

    public static SignInRefusalReason? Classify(string code, bool emailBlank, bool accountExists, bool pendingActivation)
    {
        if (LockedCodes.Contains(code))
        {
            return SignInRefusalReason.Locked;
        }

        if (DisabledCodes.Contains(code))
        {
            return pendingActivation ? SignInRefusalReason.PendingActivation : SignInRefusalReason.Deactivated;
        }

        if (WrongSecretCodes.Contains(code))
        {
            if (emailBlank)
            {
                return SignInRefusalReason.EmptyFields;
            }

            return accountExists ? SignInRefusalReason.WrongSecret : SignInRefusalReason.UnknownEmail;
        }

        return null;
    }

    /// <summary>
    /// The typed address as the log and the vendor's e-mail write it: in full when it reads as one
    /// (« karim.benali@gmail.com »), <see cref="NotAnAddress"/> otherwise — that is most often a password typed into the
    /// wrong box. Stricter than <see cref="EmailAddressInput"/> for that reason: the provider needs a dot and a
    /// letters-only ending, so « Karim@Dentiste2024 » is not written down. Letters, digits and <c>. _ % + -</c> only, so
    /// what is shown cannot forge a log line or carry markup.
    /// </summary>
    public static string ShownEmail(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return Blank;
        }

        var normalized = EmailNormalization.Normalize(typed);
        return normalized.Length <= MaxAddressLength && Address().IsMatch(normalized) ? normalized : NotAnAddress;
    }

    /// <summary>Whose account it is, for the vendor: « Cabinet Bouka · secrétaire ».</summary>
    public static string AccountLabel(string? clinicName, string? role)
    {
        var who = role?.Trim().ToLowerInvariant() switch
        {
            User.RoleAdmin => "administrateur",
            User.RoleDoctor => "médecin",
            User.RoleSecretary => "secrétaire",
            { Length: > 0 } other => other,
            _ => "compte",
        };

        return string.IsNullOrWhiteSpace(clinicName) ? who : $"{clinicName.Trim()} · {who}";
    }

    /// <summary>
    /// The alerts this refusal makes due, given how many refusals its e-mail and its address have had in the window,
    /// this one included. <paramref name="sameAddress"/> is 0 when the address is unknown.
    /// </summary>
    public static IReadOnlyList<DueSignInAlert> Due(SignInRefusal refusal, string clientAddress, int sameEmail, int sameAddress)
    {
        var due = new List<DueSignInAlert>();

        if (refusal.Door.IsConsole())
        {
            due.Add(new DueSignInAlert(
                SignInAlertKind.ConsoleRefused, $"console:{refusal.EmailKey}:{clientAddress}", TimeSpan.FromHours(1),
                sameEmail));
        }
        else
        {
            switch (refusal.Reason)
            {
                case SignInRefusalReason.Deactivated:
                    due.Add(new DueSignInAlert(
                        SignInAlertKind.DeactivatedAccountTried, $"deactivated:{refusal.EmailKey}", TimeSpan.FromDays(1),
                        sameEmail));
                    break;
                case SignInRefusalReason.WrongSecret or SignInRefusalReason.Locked when sameEmail >= AccountThreshold:
                    due.Add(new DueSignInAlert(
                        SignInAlertKind.AccountUnderAttack, $"account:{refusal.EmailKey}", TimeSpan.FromHours(1),
                        sameEmail));
                    break;
                case SignInRefusalReason.UnknownEmail when sameEmail >= UnknownEmailThreshold:
                    due.Add(new DueSignInAlert(
                        SignInAlertKind.UnknownEmailRepeated, $"unknown:{refusal.EmailKey}", TimeSpan.FromDays(1),
                        sameEmail));
                    break;
            }
        }

        if (sameAddress >= AddressThreshold)
        {
            due.Add(new DueSignInAlert(
                SignInAlertKind.AddressGuessing, $"address:{clientAddress}", TimeSpan.FromHours(1), sameAddress));
        }

        return due;
    }

    // \z, not $: « $ » also matches before a final line break.
    [GeneratedRegex(@"^[a-z0-9._%+-]{1,64}@(?:[a-z0-9-]{1,63}\.)+[a-z]{2,24}\z")]
    private static partial Regex Address();
}
