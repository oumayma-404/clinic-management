using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using ClinicManagement.Domain.Repositories;

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
