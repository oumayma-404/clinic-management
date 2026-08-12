namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// Claim names the locally-issued JWT carries beyond the registered ones. Shared by the issuer
/// (<see cref="LocalAuthService"/>) and the per-request validator (the API's
/// <c>LocalAuthEnforcementMiddleware</c>) so the two can never drift on a spelling — the same reason
/// <see cref="LocalAuthConfig"/> owns the signing key for both sides.
/// </summary>
public static class LocalAuthClaims
{
    /// <summary>
    /// The account's token version. Present on every token issued from this release onward; its <b>absence</b>
    /// is what marks a token as pre-upgrade and therefore invalid (security-hardening AC-5.15).
    /// </summary>
    public const string TokenVersion = "token_version";

    /// <summary>
    /// Which <c>SessionFamily</c> — one device's chain of refresh credentials — this token belongs to
    /// (<c>hosted-security-hardening</c> FR-1.6).
    ///
    /// <para>⚠️ <b>Carried as a claim rather than derived by hashing the token</b>, and that is what makes
    /// replay detection possible at all. Hashing finds a family only while the credential is still the current
    /// one or its immediate predecessor; a credential <i>three</i> generations back — exactly the replay this
    /// exists to catch — hashes to nothing, and « unknown credential » and « stolen credential » become the same
    /// answer. With the family named, an older credential is recognised as belonging to a live chain and that
    /// chain alone is ended.</para>
    ///
    /// <para>Only on the <b>refresh</b> token: an access token is never exchanged, so it has no chain.</para>
    /// </summary>
    public const string SessionFamily = "family_id";
}
