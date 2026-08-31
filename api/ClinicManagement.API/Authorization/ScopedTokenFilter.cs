using ClinicManagement.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClinicManagement.API.Authorization;

/// <summary>
/// Names the scopes an action will accept on a <b>restricted</b> token. An action without this attribute
/// accepts no scoped token at all.
///
/// <para>⚠️ It does <b>not</b> grant anything. Every ordinary authorization still applies — the role policy, the
/// subscription gate, the step-up confirmation. All this says is « a token narrowed to this purpose is not
/// refused <i>here</i> », which is only ever a subtraction from what the token could otherwise reach.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class AcceptsScopedTokenAttribute : Attribute
{
    public AcceptsScopedTokenAttribute(params string[] scopes)
    {
        Scopes = scopes;
    }

    public IReadOnlyList<string> Scopes { get; }
}

/// <summary>
/// Refuses a scope-narrowed token on every endpoint that has not named its scope.
///
/// <para><b>What this closes.</b> <c>POST /api/backup/archive-grants/token</c> is anonymous by necessity — the
/// device secret in the header <i>is</i> the credential — and it exchanged that secret for an ordinary
/// 30-minute <b>clinic-admin access token with the entire API surface</b>. A workstation authorised only to
/// fetch a nightly archive could read every patient record, issue invoices and manage users. The grant now
/// mints a token carrying <c>clinic_scope</c>, and this filter is what makes that claim mean something.</para>
///
/// <para>⚠️ <b>Registered globally and failing closed, which is the entire design.</b> The check is « does this
/// endpoint name the scope? », not « does this endpoint forbid it? » — so a controller action written next
/// month is out of reach of a scoped token on the day it is written, and its author has to decide nothing. The
/// inverse (endpoints declaring what they refuse) is one forgotten attribute away from the hole this replaces,
/// and the forgotten endpoint is exactly the one an over-broad token finds.</para>
///
/// <para>⚠️ It runs as an authorization filter so it precedes model binding and every action filter: a refused
/// token must not reach a handler, and it must not be able to spend a rate-limit budget or a database read on
/// the way.</para>
///
/// <para>A token with <b>no</b> scope claim is untouched — that is every ordinary sign-in, and this filter is
/// not a second authentication.</para>
/// </summary>
public sealed class ScopedTokenFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var scope = context.HttpContext.User?.FindFirst(LocalAuthClaims.Scope)?.Value;

        // No claim: an ordinary token, and none of this filter's business.
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        var accepted = (context.ActionDescriptor as ControllerActionDescriptor)
            ?.MethodInfo
            .GetCustomAttributes(typeof(AcceptsScopedTokenAttribute), inherit: true)
            .Cast<AcceptsScopedTokenAttribute>()
            .SelectMany(a => a.Scopes)
            ?? Enumerable.Empty<string>();

        if (accepted.Contains(scope, StringComparer.Ordinal))
        {
            return;
        }

        // 403 rather than 401: the credential is valid and the caller is authenticated — it simply may not do
        // this. A 401 would send the shell off to renew a token that is working exactly as intended, and it
        // would retry for ever.
        context.Result = new ObjectResult(new
        {
            error = "Ce jeton est limité à une seule opération et ne permet pas cette action.",
        })
        {
            StatusCode = StatusCodes.Status403Forbidden,
        };
    }
}
