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

    /// <summary>
    /// « The person signing in said this device is theirs », so the session runs on the long lifetime rather
    /// than the ordinary one.
    ///
    /// <para>⚠️ <b>It grants nothing on its own.</b> The claim's only readers are the BFF — which uses it to pick
    /// how long the browser waits before locking or signing out — and this issuer, which reads the fact back off
    /// the <c>SessionFamily</c> row, never off the token. A forged claim therefore cannot lengthen a session: the
    /// credential's own <c>exp</c> was stamped when it was minted, and the next rotation asks the database, not
    /// the JWT. It is on the token so a cold page load can size its idle timer from the cookie alone, with no
    /// round trip.</para>
    ///
    /// <para>Only on the <b>refresh</b> token, like <see cref="SessionFamily"/>: the access token is not the
    /// thing whose lifetime this changes.</para>
    /// </summary>
    public const string SessionTrusted = "session_trusted";

    /// <summary>
    /// Narrows a token to one purpose. <b>Absent on an ordinary sign-in token</b> — its presence is what marks a
    /// token as restricted, and <c>ScopedTokenFilter</c> then refuses every endpoint that has not named the
    /// scope as acceptable.
    ///
    /// <para>⚠️ <b>The direction is deliberate and it is the whole design.</b> An allow-list keyed on the claim's
    /// presence fails <i>closed</i>: a new controller action is unreachable by a scoped token on the day it is
    /// written, with no decision required from its author. The obvious alternative — endpoints declaring which
    /// scopes they refuse — fails open, and the endpoint nobody thought about is exactly the one an
    /// over-broad token reaches.</para>
    ///
    /// <para>Only ever minted by <c>ExchangeArchiveGrant</c> today, which used to hand an unattended clinic PC
    /// an ordinary <b>clinic-admin token with the whole API surface</b> in exchange for a device secret.</para>
    /// </summary>
    public const string Scope = "clinic_scope";
}

/// <summary>
/// The scopes a restricted token may carry. One constant per purpose, so the issuer and the endpoint that
/// accepts it are one statement rather than two matching string literals.
/// </summary>
public static class LocalAuthScopes
{
    /// <summary>
    /// « This token may download the cabinet's own archive, and do nothing else. » Minted for an authorised
    /// unattended workstation from its device grant.
    /// </summary>
    public const string ClinicArchive = "clinic-archive";
}
