namespace ClinicManagement.Application.Common;

/// <summary>
/// Who reset an account's second factor. Carried into the notice so the sentence the affected person reads names
/// the actor that really acted.
///
/// <para>⚠️ <b>An enum rather than a <c>bool byVendor</c>.</b> A third actor is entirely plausible — the console
/// verb run on the server is already a fourth path in the codebase — and a boolean would answer « was it the
/// vendor? » where the notice needs « who was it? ». It is also the shape this codebase uses for exactly this
/// (<c>DeploymentProfile</c>'s « a capability per question, never a boolean »).</para>
/// </summary>
public enum SecondFactorResetBy
{
    /// <summary>An administrator of the person's own clinic (<c>ResetUserTotpCommand</c>).</summary>
    ClinicAdministrator = 0,

    /// <summary>
    /// The software vendor, from the platform console (<c>platform-console</c>) or its console verb.
    ///
    /// <para>Its own member because the two sentences must differ in <b>where to complain</b>: telling somebody to
    /// warn their administrator about an action their administrator did not take, and cannot see, sends the one
    /// person who noticed to the one person who can do nothing.</para>
    /// </summary>
    Vendor = 1
}

/// <summary>
/// What the person whose second factor was reset is told, in-app and by e-mail
/// (<c>hosted-security-hardening</c> FR-1.4, <c>platform-console</c>).
///
/// <para><b>One home for four sentences.</b> The in-app row is written by <c>NotificationGenerator</c> and the
/// e-mail by whichever command performed the reset, so before this the wording existed twice — and the vendor path
/// would have made it four times. That is this repository's dominant defect shape, and here it has a sharp edge:
/// the copy that drifts is a security notice, and the reader's next action depends on which actor it names.</para>
///
/// <para>⚠️ <b>Every version says the same three things</b>, because each one is load-bearing: the factor is gone,
/// a new one must be enrolled at the next sign-in, and — if you did not ask for this — here is who to tell. The
/// last is the whole reason the notice exists: without it, stripping somebody's protection is a step a stolen
/// session could take unobserved before signing in as them.</para>
/// </summary>
public static class SecondFactorResetNotice
{
    /// <summary>The in-app row's title, and the e-mail's subject differs from it deliberately (see below).</summary>
    public const string Title = "Second facteur réinitialisé";

    /// <summary>
    /// The e-mail subject. Second person, unlike <see cref="Title"/>: an in-app row is already addressed to the
    /// person reading it, while a subject line arrives among a hundred others and has to say whose account it is
    /// about.
    /// </summary>
    public const string EmailSubject = "Votre second facteur a été réinitialisé";

    /// <summary>The in-app notification body.</summary>
    public static string InApp(SecondFactorResetBy by) =>
        Actor(by) + " a réinitialisé votre second facteur. Vous devrez en enrôler un nouveau à votre prochaine "
        + "connexion. " + WhoToTell(by);

    /// <summary>
    /// The e-mail body. Longer than the in-app row on purpose — it is read outside the application, by somebody who
    /// may not be able to sign in at all, which is precisely the situation this message is about.
    /// </summary>
    public static string EmailBody(SecondFactorResetBy by) =>
        Actor(by) + " a réinitialisé le second facteur d'authentification de votre compte. Votre application "
        + "d'authentification actuelle ne fonctionne plus et vos anciens codes de récupération ont été annulés. "
        + "À votre prochaine connexion, il vous sera demandé d'enrôler un nouveau second facteur : conservez la "
        + "nouvelle série de codes de récupération hors de votre téléphone. " + WhoToTell(by);

    private static string Actor(SecondFactorResetBy by) => by switch
    {
        SecondFactorResetBy.Vendor => "Le support technique de votre logiciel",
        _ => "Un administrateur de votre cabinet"
    };

    /// <summary>
    /// Where an unexpected reset gets reported. ⚠️ <b>Not the same address for both actors.</b> « Prévenez votre
    /// administrateur » is the right instruction when an administrator did it — they can see the action and undo
    /// its consequences. When the <i>vendor</i> did it, the administrator has no record of it and no power over
    /// it, so the report has to go back to the vendor, and the cabinet's own administrator is told as well because
    /// somebody at the practice needs to know a support action touched an account.
    /// </summary>
    private static string WhoToTell(SecondFactorResetBy by) => by switch
    {
        SecondFactorResetBy.Vendor =>
            "Si vous n'avez pas demandé cette réinitialisation, contactez immédiatement le support de votre "
            + "logiciel et prévenez l'administrateur de votre cabinet.",
        _ =>
            "Si vous n'êtes pas à l'origine de cette demande, prévenez immédiatement votre administrateur."
    };
}
