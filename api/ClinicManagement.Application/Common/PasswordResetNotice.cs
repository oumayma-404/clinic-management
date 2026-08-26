namespace ClinicManagement.Application.Common;

/// <summary>
/// Who replaced an account's password <b>on its owner's behalf</b>. Carried into the notice so the sentence the
/// affected person reads names the actor that really acted.
///
/// <para>⚠️ <b>An enum rather than a <c>bool byVendor</c></b>, for <see cref="SecondFactorResetBy"/>'s reasons: a
/// boolean answers « was it the vendor? » where the notice needs « who was it? », and this codebase's own rule is a
/// capability — or here an actor — per question.</para>
///
/// <para>⚠️ <b>There is deliberately no <c>SelfService</c> member.</b> A person who resets their own password from
/// the login screen chose it themselves and gets a different message entirely (« votre mot de passe a été
/// modifié », in <c>CompletePasswordResetCommand</c>) — no temporary credential exists, nothing has to be relayed,
/// and « quelqu'un a réinitialisé votre mot de passe » would be an alarm about the reader's own action. Folding the
/// two into one home would be the tidier-looking mistake.</para>
/// </summary>
public enum PasswordResetBy
{
    /// <summary>An administrator of the person's own clinic (<c>ResetUserPasswordCommand</c>).</summary>
    ClinicAdministrator = 0,

    /// <summary>
    /// The software vendor, from the platform console (<c>ResetClinicUserPasswordFromConsoleCommand</c>) or the
    /// <c>reset-admin-password</c> verb.
    ///
    /// <para>Its own member because the two sentences must differ in <b>where to complain</b>: telling somebody to
    /// warn their administrator about an action their administrator did not take, and cannot see, sends the one
    /// person who noticed to the one person who can do nothing.</para>
    /// </summary>
    Vendor = 1
}

/// <summary>
/// What the person whose password was reset for them is told, in-app and by e-mail.
///
/// <para><b>One home for four sentences</b>, on <see cref="SecondFactorResetNotice"/>'s precedent and for the
/// reason that file states: the in-app row is written by <c>NotificationGenerator</c> and the e-mail by whichever
/// command performed the reset, so without this the wording would exist twice per actor — this repository's
/// dominant defect shape, with a sharp edge here because the copy that drifts is a security notice and the reader's
/// next action depends on which actor it names.</para>
///
/// <para>⚠️ <b>Every version says the same three things</b>, each load-bearing: the old password no longer works,
/// a temporary one has been issued and must be relayed by the person who performed the reset (never by e-mail —
/// which is why no version of this message contains it), and — if you did not ask for this — here is who to tell.
/// The last is the whole reason the notice exists: without it, taking over an account by ringing support is a step
/// nobody at the practice would observe.</para>
/// </summary>
public static class PasswordResetNotice
{
    /// <summary>The in-app row's title. The e-mail subject differs from it deliberately (see below).</summary>
    public const string Title = "Mot de passe réinitialisé";

    /// <summary>
    /// The e-mail subject. Second person, unlike <see cref="Title"/>: an in-app row is already addressed to the
    /// person reading it, while a subject line arrives among a hundred others and has to say whose account it is
    /// about.
    /// </summary>
    public const string EmailSubject = "Votre mot de passe a été réinitialisé";

    /// <summary>The in-app notification body.</summary>
    public static string InApp(PasswordResetBy by) =>
        Actor(by) + " a réinitialisé le mot de passe de votre compte. Un mot de passe temporaire vous sera "
        + "communiqué directement, et il vous sera demandé d'en choisir un nouveau à la connexion. " + WhoToTell(by);

    /// <summary>
    /// The e-mail body. Longer than the in-app row on purpose — it is read outside the application, by somebody who
    /// by definition cannot sign in, which is precisely the situation this message is about.
    ///
    /// <para>⚠️ <b>It carries no password</b>, and no version of it ever may. The temporary credential is shown once
    /// to the person who performed the reset, to be relayed by voice; mailing it would put a live credential in the
    /// mailbox an attacker most likely already holds, and would make this very notice the delivery mechanism for the
    /// takeover it exists to reveal.</para>
    /// </summary>
    public static string EmailBody(PasswordResetBy by) =>
        Actor(by) + " a réinitialisé le mot de passe de votre compte. Votre ancien mot de passe ne fonctionne plus "
        + "et vos autres appareils ont été déconnectés. Un mot de passe temporaire a été remis à "
        + Relay(by) + " : il vous sera communiqué de vive voix, jamais par e-mail, et vous devrez choisir votre "
        + "propre mot de passe dès votre première connexion. Votre code de vérification à six chiffres reste "
        + "exigé et n'a pas été modifié. " + WhoToTell(by);

    private static string Actor(PasswordResetBy by) => by switch
    {
        PasswordResetBy.Vendor => "Le support technique de votre logiciel",
        _ => "Un administrateur de votre clinique"
    };

    /// <summary>Who is holding the temporary password — i.e. who to go and ask for it.</summary>
    private static string Relay(PasswordResetBy by) => by switch
    {
        PasswordResetBy.Vendor => "la personne du support qui a traité votre demande",
        _ => "l'administrateur qui a effectué l'opération"
    };

    /// <summary>
    /// Where an unexpected reset gets reported. ⚠️ <b>Not the same address for both actors</b>, for the reason
    /// <see cref="SecondFactorResetNotice"/> states: an administrator can see and undo their own action, while a
    /// vendor action leaves the cabinet's administrator no record and no power — so that report goes back to the
    /// vendor, and the administrator is told as well because somebody at the practice needs to know a support
    /// action touched an account.
    /// </summary>
    private static string WhoToTell(PasswordResetBy by) => by switch
    {
        PasswordResetBy.Vendor =>
            "Si vous n'avez pas demandé cette réinitialisation, contactez immédiatement le support de votre "
            + "logiciel et prévenez l'administrateur de votre cabinet.",
        _ =>
            "Si vous n'êtes pas à l'origine de cette demande, prévenez immédiatement votre administrateur."
    };
}
