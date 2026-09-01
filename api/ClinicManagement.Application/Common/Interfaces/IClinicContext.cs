namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Service to extract clinic and user information from JWT claims
/// </summary>
public interface IClinicContext
{
    /// <summary>
    /// Gets the current user's clinic ID from JWT claims
    /// </summary>
    Guid? GetClinicId();

    /// <summary>
    /// Gets the current user's role from JWT claims
    /// </summary>
    string? GetUserRole();

    /// <summary>
    /// Gets the current user's Auth0 sub (user ID)
    /// </summary>
    string? GetUserId();

    /// <summary>
    /// Gets the current user's email from JWT claims
    /// </summary>
    string? GetUserEmail();

    /// <summary>
    /// Which <c>SessionFamily</c> — one device's chain — the caller's token belongs to, when it says so.
    ///
    /// <para>This is what lets « Mes appareils » mark one row « cet appareil ». Without it the screen lists a
    /// set of indistinguishable sessions beside a button that ends one, and the most likely mistake is a user
    /// signing themselves out while trying to remove a device they no longer have.</para>
    ///
    /// <para>⚠️ <b>Null is ordinary, not an error.</b> A token minted before the claim existed carries none, and
    /// so does the scoped archive token, which has no chain at all. Every caller must treat « unknown » as « do
    /// not mark anything » rather than as a failure.</para>
    /// </summary>
    Guid? GetSessionFamilyId();
}



