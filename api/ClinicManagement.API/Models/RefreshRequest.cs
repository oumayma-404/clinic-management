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
