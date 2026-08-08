namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// Where the request pipeline publishes the caller's role <b>as the database holds it</b>, for
/// <c>RoleAuthorizationHandler</c> to prefer over the JWT claim.
///
/// <para>
/// ⚠️ <b>The claim is not authoritative, and on one profile it was never corrected.</b> In <c>CloudBrowser</c> the
/// role reaches the token from Auth0 <c>app_metadata</c>, written by an Action outside this repository — and
/// <c>ChangeUserRoleCommand</c> updates the <c>User</c> row without telling Auth0. A demoted admin therefore kept
/// passing <c>AdminOnly</c> on every request, including freshly-issued tokens, indefinitely. Reading the row the
/// pipeline has already loaded removes the propagation question entirely: there is nothing to keep in step.
/// </para>
///
/// <para>
/// A key on <c>HttpContext.Items</c> rather than a service, because the two ends live in different projects: the
/// row is resolved by an API-layer middleware and consumed by an Application-layer authorization handler, and a
/// shared constant is a smaller seam than an interface neither layer owns.
/// </para>
///
/// <para>
/// ⚠️ <b>Absent means "fall back to the claim", never "refuse".</b> A principal with no <c>User</c> row yet is the
/// ordinary state of Cloud onboarding — <c>POST /clinics</c> and <c>/clinics/join</c> are reached before any row
/// exists — which is exactly why the role-less <c>Authenticated</c> policy exists. Refusing here would break
/// signing up rather than protect anything.
/// </para>
/// </summary>
public static class EffectiveRole
{
    /// <summary>The <c>HttpContext.Items</c> key carrying the DB-resolved role, when one is known.</summary>
    public const string HttpContextItemKey = "clinic-management.effective-role";
}
