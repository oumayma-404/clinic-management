namespace ClinicManagement.API.Models;

/// <summary>
/// « J'ai oublié mon mot de passe » — the address to send a reset link to.
/// </summary>
public class PasswordResetEmailRequest
{
    /// <summary>
    /// ⚠️ The property name is what <c>AuthAttemptAccount</c> lifts out of the body before model binding, so the
    /// rate limiter partitions this endpoint per submitted account rather than only per address. Renaming it
    /// silently moves every request from one NAT address back onto a shared budget — which on a door that sends
    /// mail to an address the caller chooses is the bound that matters.
    /// </summary>
    public string Email { get; set; } = string.Empty;
}

/// <summary>
/// The reset link's payload: the raw token, which exists only in the e-mail that carried it, plus the password its
/// holder has chosen.
///
/// <para>Carries no e-mail address, deliberately. The token already names the account, and accepting an address
/// beside it would invite a handler that trusts the pair — turning a token valid for one account into a lever on
/// another.</para>
/// </summary>
public class PasswordResetCompletionRequest
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
