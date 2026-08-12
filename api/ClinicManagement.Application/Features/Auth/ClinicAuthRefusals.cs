namespace ClinicManagement.Application.Features.Auth;

/// <summary>
/// Every refusal the clinic sign-in surface can answer with, and the French sentence for each
/// (<c>hosted-security-hardening</c> FR-1.1 – FR-1.5). <c>PlatformAuthRefusals</c>' shape, for the other
/// identity population.
///
/// <para>⚠️ <b>The code and its sentence live in the SAME file, deliberately.</b> Three copies is how a reworded
/// message silently stops matching the code it was paired with — the <c>Contains("déjà facturée")</c> defect this
/// repository deleted. The client branches on the <b>code</b> and never on the prose.</para>
///
/// <para>⚠️ <b>The status code is not here.</b> It is chosen by the controller
/// (<c>AuthController.StatusForRefusal</c>), the same split <c>PlatformAuthController.StatusFor</c> uses: a
/// <c>Result.Code</c> says what happened, and how that maps onto HTTP is a presentation decision.</para>
/// </summary>
public static class ClinicAuthRefusals
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string TotpRequired = "totp_required";
    public const string TotpEnrolmentRequired = "totp_enrolment_required";
    public const string TotpInvalid = "totp_invalid";
    public const string TotpAlreadyEnrolled = "totp_already_enrolled";
    public const string TotpNotEnrolled = "totp_not_enrolled";
    public const string AccountDisabled = "account_disabled";
    public const string TooManyAttempts = "too_many_attempts";
    public const string PasswordPolicy = "password_policy";

    /// <summary>
    /// The French sentence for a code, or null when the code is not one of ours.
    ///
    /// <para>Null rather than a generic fallback: a caller passing an unknown code is a bug in this file's own
    /// vocabulary, and <c>ClinicTotpAuthTests</c> asserts every declared constant resolves to a sentence.</para>
    /// </summary>
    public static string? MessageFor(string code) => code switch
    {
        InvalidCredentials => "Identifiants invalides.",
        TotpRequired => "Code de vérification requis.",
        TotpEnrolmentRequired =>
            "Ce compte doit d'abord enrôler son second facteur. Vous pouvez le faire depuis l'écran de connexion.",
        TotpInvalid => "Code de vérification invalide.",
        TotpAlreadyEnrolled => "Le second facteur est déjà enrôlé pour ce compte.",
        TotpNotEnrolled => "Le second facteur n'est pas enrôlé pour ce compte.",
        AccountDisabled => "Ce compte a été désactivé. Veuillez contacter l'administrateur de votre clinique.",
        TooManyAttempts => "Trop de tentatives. Réessayez dans quelques minutes.",
        PasswordPolicy => $"Le mot de passe doit contenir au moins {Common.PasswordPolicy.MinLength} caractères.",
        _ => null
    };

    /// <summary>Every code declared here, derived from the constants so a new one cannot escape the tests.</summary>
    public static IReadOnlyList<string> AllCodes { get; } = typeof(ClinicAuthRefusals)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .OrderBy(c => c, StringComparer.Ordinal)
        .ToList();
}
