using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Auth;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// Local-mode only: enforces account state on every authenticated request. The app-issued JWT is
/// stateless and long-lived, so without this a deactivated user's existing token would keep working
/// until it expired (AC-5.3), and a user with a pending admin reset could skip the forced password
/// change by calling the API directly (AC-5.2). The database is the source of truth, so this also
/// catches an admin who deactivates/resets a user who is already logged in. Registered only when
/// <c>Auth:Mode = Local</c>; Cloud requests never reach it.
/// </summary>
public class LocalAuthEnforcementMiddleware
{
    private const string ChangePasswordPath = "/api/auth/change-password";

    private readonly RequestDelegate _next;

    public LocalAuthEnforcementMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository users)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var subject = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            if (!string.IsNullOrEmpty(subject))
            {
                var account = await users.GetByAuth0SubAsync(subject, context.RequestAborted);
                if (account is not null && account.IsLocalAccount())
                {
                    // Token revocation (US-5 / AC-5.1). Rides the account lookup this middleware already
                    // performs, so it costs no extra query. A token whose version no longer matches — or
                    // which carries no version at all, i.e. was issued before this shipped (AC-5.15) — is
                    // dead, even though the JWT signature and lifetime are still perfectly valid.
                    if (!HasCurrentTokenVersion(context.User, account.TokenVersion))
                    {
                        await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                            "Votre session n'est plus valide. Veuillez vous reconnecter.");
                        return;
                    }

                    if (!account.IsActive)
                    {
                        await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                            "This account has been deactivated.");
                        return;
                    }

                    if (account.MustChangePassword && !IsChangePasswordRequest(context.Request))
                    {
                        await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                            "You must change your password before continuing.", "must_change_password");
                        return;
                    }
                }
            }
        }

        await _next(context);
    }

    /// <summary>
    /// True only when the principal carries a <c>token_version</c> claim that parses and matches
    /// <paramref name="currentVersion"/>. Fails closed on a missing or unparseable claim: a token that cannot
    /// prove its version is not trusted, which is precisely how the pre-upgrade tokens are retired.
    /// </summary>
    private static bool HasCurrentTokenVersion(ClaimsPrincipal principal, int currentVersion)
    {
        var claim = principal.FindFirst(LocalAuthClaims.TokenVersion)?.Value;

        return int.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenVersion)
               && tokenVersion == currentVersion;
    }

    private static bool IsChangePasswordRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(ChangePasswordPath, StringComparison.OrdinalIgnoreCase);

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string error, string? code = null)
    {
        context.Response.StatusCode = statusCode;
        return code is null
            ? context.Response.WriteAsJsonAsync(new { error })
            : context.Response.WriteAsJsonAsync(new { error, code });
    }
}
