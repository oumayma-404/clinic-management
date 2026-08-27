namespace ClinicManagement.Application.Common.Authorization;

/// <summary>
/// Where the request pipeline publishes the caller's role <b>as the database holds it</b>, for
/// <c>RoleAuthorizationHandler</c> to prefer over the JWT claim.
///
/// <para>
/// ⚠️ <b>The claim is not authoritative — it is a copy, taken when the token was minted.</b> A role lives on the
/// <c>User</c> row and a token lasts twelve hours, so between <c>ChangeUserRoleCommand</c> and the holder's next
/// sign-in the two disagree, and the claim is the stale half. Reading the row the pipeline has already loaded
/// removes the propagation question entirely: there is nothing to keep in step.
/// </para>
///
/// <para>
/// ⚠️ <b>This is defence in depth, not the revocation.</b> <c>User.ChangeRole</c> bumps <c>TokenVersion</c> and
/// <c>EnforcesTokenState</c> re-checks it per request, so a demoted admin's token is refused outright. That was
/// not always the whole story: it was written when the role reached the token from a third-party identity
/// provider this product could not update, where a demoted admin kept passing <c>AdminOnly</c> indefinitely. That
/// deployment kind is retired, and the mechanism is kept because « the database is authoritative about who you
/// are » should not depend on which of two guards is load-bearing this month.
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
/// ordinary state of onboarding — <c>POST /clinics</c> and <c>/clinics/join</c> are reached before any row
/// exists — which is exactly why the role-less <c>Authenticated</c> policy exists. Refusing here would break
/// signing up rather than protect anything.
/// </para>
/// </summary>
public static class EffectiveRole
{
    /// <summary>The <c>HttpContext.Items</c> key carrying the DB-resolved role, when one is known.</summary>
    public const string HttpContextItemKey = "clinic-management.effective-role";
}
