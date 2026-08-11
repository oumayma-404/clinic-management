namespace ClinicManagement.Application.Features.Platform.Auth;

/// <summary>
/// Every way a console sign-in can be refused, as a <b>code plus its French sentence</b> — declared once so the
/// handler, the controller's status mapping and the tests read the same table.
///
/// <para><b>Why codes at all here, when most of this product's refusals are prose.</b> The spec's API section
/// gives four of these distinct <i>HTTP statuses</i> (401 · 403 · 400 · 409), and the controller has to choose one.
/// Recovering that from the French message would be the <c>Contains("déjà facturée")</c> defect this codebase
/// deleted in <c>adoption-gaps-remediation</c>: rewording a sentence would silently change a status. So the code is
/// the contract and the sentence is the display.</para>
///
/// <para>⚠️ <b><see cref="InvalidCredentials"/> deliberately covers four different facts</b>: no such account, a
/// wrong password, a wrong one-time code, and a wrong recovery code. Each is a separate branch in the handler and
/// they must be indistinguishable to the caller, or the endpoint becomes an oracle for which half of a two-factor
/// credential was correct — and, worse, an account-enumeration oracle for a population of two or three addresses.</para>
///
/// <para>⚠️ <b><see cref="TotpEnrolmentRequired"/> carries nothing else</b> (AC-1.3, EC-2). No secret, no recovery
/// codes, no session. Returning the secret here would hand the second factor to whoever already has the password,
/// which is the single thing it exists to prevent — the secret comes from the bootstrap verb, out of band.</para>
/// </summary>
public static class PlatformAuthRefusals
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string TotpRequired = "totp_required";
    public const string TotpEnrolmentRequired = "totp_enrolment_required";
    public const string TotpInvalid = "totp_invalid";
    public const string TotpAlreadyEnrolled = "totp_already_enrolled";
    public const string AccountDisabled = "account_disabled";
    public const string TooManyAttempts = "too_many_attempts";
    public const string PasswordPolicy = "password_policy";
    public const string NoSession = "no_session";

    /// <summary>
    /// The French sentence for a code, or null when the code is not one of ours. Null rather than a fallback
    /// sentence on purpose: a caller passing an unknown code is a bug in this file's own vocabulary, and
    /// <c>PlatformAuthRefusalTests</c> asserts every declared constant resolves.
    /// </summary>
    public static string? MessageFor(string code) => code switch
    {
        InvalidCredentials => "Identifiants invalides.",
        TotpRequired => "Code de vérification requis.",
        TotpEnrolmentRequired => "Ce compte doit d'abord enrôler son second facteur.",
        TotpInvalid => "Code de vérification invalide.",
        TotpAlreadyEnrolled => "Le second facteur est déjà enrôlé pour ce compte.",
        AccountDisabled => "Ce compte a été désactivé.",
        TooManyAttempts => "Trop de tentatives. Réessayez dans quelques minutes.",
        PasswordPolicy => $"Le mot de passe doit contenir au moins {Common.PasswordPolicy.MinLength} caractères.",
        NoSession => "Session de console requise.",
        _ => null
    };

    /// <summary>Every code this file declares, derived from its own constants so a new one cannot escape the tests.</summary>
    public static IReadOnlyList<string> AllCodes { get; } = typeof(PlatformAuthRefusals)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(c => c, StringComparer.Ordinal)
        .ToList();
}
