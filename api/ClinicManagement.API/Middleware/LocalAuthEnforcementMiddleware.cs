using System.Globalization;
using System.Security.Claims;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Auth;

using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;

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
        // Console requests carry their own token version and forced-password-change rules, enforced by
        // PlatformAccountStateMiddleware against PlatformAccount rather than User. See that class.
        if (ClinicManagement.API.Startup.ConsolePortGate.IsConsolePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Shared with TenantScopeMiddleware, which needs the same row for the clinic it scopes to — one lookup
        // per request rather than two.
        var account = await RequestAccount.ResolveAsync(context, users);

        if (account is not null && account.IsLocalAccount())
        {
            // Token revocation (US-5 / AC-5.1). A token whose version no longer matches — or which carries no
            // version at all, i.e. was issued before this shipped (AC-5.15) — is dead, even though the JWT
            // signature and lifetime are still perfectly valid.
            if (!HasCurrentTokenVersion(context.User, account.TokenVersion))
            {
                await WriteErrorAsync(context, StatusCodes.Status401Unauthorized,
                    "Votre session n'est plus valide. Veuillez vous reconnecter.");
                return;
            }

            // The active-account check moved to AccountStateMiddleware, which runs unconditionally in every
            // profile — it was skipped entirely on CloudBrowser from here, so deactivating a user did nothing.

            if (account.MustChangePassword && !IsChangePasswordRequest(context.Request))
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                    "You must change your password before continuing.", "must_change_password");
                return;
            }

            // ── The second-factor requirement, re-checked PER REQUEST (hosted-security-hardening FR-1.2) ──
            //
            // ⚠️ Without this a session established *before* the requirement existed — or before this account
            // was promoted to administrator — outlives it: the login ladder would refuse a fresh sign-in while
            // the token already in the browser keeps working for its full lifetime. The requirement has to be
            // a property of every request, not of the moment a token was minted.
            //
            // ⚠️ **After the forced-password-change gate, deliberately** (FR-1.7a): an account owing both is
            // sent to the screen that unblocks it, and enrolment is checked once that is done. Reversing the
            // two would route the user to enrolment and leave them stuck, since every call still 403s.
            var policy = context.RequestServices.GetService<ISecondFactorPolicy>();
            if (policy?.RequiresAdminSecondFactor == true
                && account.IsAdmin()
                && !account.IsTotpEnrolled
                && !IsSecondFactorRequest(context.Request))
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden,
                    ClinicAuthRefusals.MessageFor(ClinicAuthRefusals.TotpEnrolmentRequired)!,
                    ClinicAuthRefusals.TotpEnrolmentRequired);
                return;
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

    /// <summary>
    /// The paths an account that still owes an enrolment must be able to reach, or the refusal above has no
    /// destination and the app is « usable-looking and dead ».
    ///
    /// <para>The whole of <c>/api/auth</c>: enrolment, code verification, recovery redemption and step-up all
    /// live there, as does signing out. Scoping it to the exact four actions would mean a list to keep in step
    /// with the controller, and every other action on it is already anonymous or harmless.</para>
    /// </summary>
    private static bool IsSecondFactorRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    private static Task WriteErrorAsync(HttpContext context, int statusCode, string error, string? code = null)
    {
        context.Response.StatusCode = statusCode;
        return code is null
            ? context.Response.WriteAsJsonAsync(new { error })
            : context.Response.WriteAsJsonAsync(new { error, code });
    }
}
