namespace ClinicManagement.API.Models;

/// <summary>
/// Body of <c>POST /api/auth/refresh</c> (security-hardening US-5).
///
/// The refresh token is passed in the <b>body</b>, not as a bearer header, on purpose: the API's JWT
/// validation requires the access-token audience, so a refresh token in an <c>Authorization</c> header would
/// be rejected by the authentication layer before the endpoint ever ran. The BFF reads it from its HttpOnly
/// cookie and posts it server-side, so it never travels through browser JavaScript.
/// </summary>
public class RefreshRequest
{
    public string RefreshToken { get; set; } = string.Empty;
}

/// <summary>
/// Body of <c>POST /api/auth/logout</c>: the credential to retire, and why.
///
/// <para><b>Why the reason is on the wire at all.</b> Only the client knows whether a person pressed « Se
/// déconnecter » or whether its inactivity limit ran out — both end the same session through the same endpoint.
/// Until now both were stamped « Déconnexion demandée par l'utilisateur », so the one table that could answer
/// « how often is the timeout actually signing people out? » could not distinguish them, and the question had to
/// be answered by reading the browser's source instead of the practice's own records.</para>
///
/// <para>⚠️ <b>It is a closed vocabulary, not the sentence.</b> This endpoint is anonymous, so the value is
/// attacker-controlled, and it is persisted and later rendered on « Mes appareils » and in the journal. An
/// unrecognised or absent value falls back to the deliberate case rather than being refused — a sign-out must
/// never fail over a label.</para>
/// </summary>
public class LogoutRequest
{
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary><c>"inactivity"</c>, or anything else for a deliberate sign-out.</summary>
    public string? Reason { get; set; }

    /// <summary>The wire value that means « the limit ran out », matched case-insensitively.</summary>
    public const string InactivityReason = "inactivity";
}
