using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicManagement.API.Startup;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Auth;

namespace ClinicManagement.API.Middleware;

/// <summary>
/// The console's per-request account-state check: a deactivated <see cref="PlatformAccount"/>, a stale
/// <c>token_version</c> or a pending forced password change is refused on the <b>next request</b>
/// (<c>platform-console</c> AC-1.6, AC-8.1).
///
/// <para><b>It exists because console requests skip the product's only two live-state readers.</b>
/// <see cref="AccountStateMiddleware"/> and <see cref="LocalAuthEnforcementMiddleware"/> both resolve the
/// caller's <c>User</c> row, and a console principal has none — so both are no-ops for it. Without this
/// middleware the AC-8.5 deactivation command would leave a revoked account with full cross-cabinet read access
/// until its token expired, which is exactly what AC-1.6 forbids. Skipping those two is not free; this is the
/// price, and it lands in the same change.</para>
///
/// <para>⚠️ <b>401, not 403</b>, for the same reason the clinic side does it: the credential is no longer valid,
/// not merely insufficient. A 403 would tell a revoked console account it is still authenticated.</para>
///
/// <para>⚠️ <b>The loaded row is cached on <see cref="HttpContext.Items"/></b> so anything downstream that needs
/// it — today the forced-password-change branch, tomorrow the access ledger's writer — reuses one query instead
/// of issuing its own, the same shape <see cref="RequestAccount"/> already gives the clinic side.</para>
///
/// <para>⚠️ <b>Ordering: after <c>UseAuthentication</c>.</b> Before it there is no principal to read, so the
/// middleware would pass every request through and the revocation would silently not exist —
/// <c>PlatformAccountStateTests</c> asserts the ordering against <c>Program.cs</c>'s own source for that reason,
/// as <c>AccountStateEnforcementTests</c> does for its own.</para>
/// </summary>
public class PlatformAccountStateMiddleware
{
    /// <summary>Where the resolved console account is cached for the rest of the request.</summary>
    public const string AccountItemKey = "clinic-management.platform-account";

    /// <summary>The one path a console account with a pending password change may still reach (AC-8.6).</summary>
    public const string ChangePasswordPath = "/api/platform/auth/password";

    private readonly RequestDelegate _next;

    public PlatformAccountStateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IPlatformAccountRepository accounts)
    {
        // Console paths only. Every other request in the process is a clinic one, and re-reading its principal
        // here would cost a lookup on every call in the product for a population of two or three accounts.
        if (!ConsolePortGate.IsConsolePath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var accountId = ConsoleAccountId(context.User);
        if (accountId is null)
        {
            // Anonymous (login, enrolment, recovery) or a non-console principal that somehow reached a console
            // path: neither is this middleware's business, and the policy is what refuses the second.
            await _next(context);
            return;
        }

        var account = await accounts.GetForStateCheckAsync(accountId.Value, context.RequestAborted);

        if (account is null || !account.IsActive)
        {
            await RefuseAsync(context, StatusCodes.Status401Unauthorized, "Ce compte a été désactivé.");
            return;
        }

        if (!HasCurrentTokenVersion(context.User, account.TokenVersion))
        {
            await RefuseAsync(context, StatusCodes.Status401Unauthorized,
                "Votre session n'est plus valide. Veuillez vous reconnecter.");
            return;
        }

        if (account.MustChangePassword && !IsChangePasswordRequest(context.Request))
        {
            await RefuseAsync(context, StatusCodes.Status403Forbidden,
                "Vous devez changer votre mot de passe avant de continuer.", "must_change_password");
            return;
        }

        context.Items[AccountItemKey] = account;
        await _next(context);
    }

    /// <summary>
    /// The console account this principal is, or null for anything else.
    ///
    /// <para>Gated on the token-kind claim rather than on the subject's shape, for the reason
    /// <see cref="IPlatformSessionContext"/> states: both token kinds carry a <c>sub</c>, and only the console's
    /// own issuer emits this claim.</para>
    /// </summary>
    private static Guid? ConsoleAccountId(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var kind = principal.FindFirst(IPlatformSessionContext.TokenKindClaim)?.Value;
        if (!string.Equals(kind, IPlatformSessionContext.PlatformTokenKind, StringComparison.Ordinal))
        {
            return null;
        }

        var subject = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        return Guid.TryParse(subject, out var id) ? id : null;
    }

    /// <summary>
    /// True only when the principal carries a <c>token_version</c> that parses and matches. <b>Fails closed</b> on
    /// a missing or unparseable claim — a token that cannot prove its version is not trusted, which is the same
    /// rule that retires the clinic side's pre-upgrade tokens.
    /// </summary>
    private static bool HasCurrentTokenVersion(ClaimsPrincipal principal, int currentVersion)
    {
        var claim = principal.FindFirst(LocalAuthClaims.TokenVersion)?.Value;

        return int.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokenVersion)
               && tokenVersion == currentVersion;
    }

    private static bool IsChangePasswordRequest(HttpRequest request) =>
        request.Path.StartsWithSegments(ChangePasswordPath, StringComparison.OrdinalIgnoreCase);

    private static Task RefuseAsync(HttpContext context, int statusCode, string error, string? code = null)
    {
        context.Response.StatusCode = statusCode;
        return code is null
            ? context.Response.WriteAsJsonAsync(new { error })
            : context.Response.WriteAsJsonAsync(new { error, code });
    }
}
