namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>
/// Tracks failed login attempts <b>per account and per source</b> (security-hardening US-4 / AC-4.2).
///
/// <para><b>Why per source.</b> The durable counter on <c>User</c> is keyed on the account alone, so anyone who
/// can reach the login endpoint could burn five attempts against every staff account in turn and keep the
/// entire clinic — admin included — locked out indefinitely. A per-IP rate limiter alone does not fix that:
/// five attempts sits below any sane rate limit. Keying the lockout on (account, source) means a hostile host
/// locks out only itself, while a colleague signing in from another machine is unaffected.</para>
///
/// <para>The durable per-account counter is kept as a much higher backstop, so a genuinely distributed
/// guessing attack is still stopped — but no single source can trip it.</para>
///
/// <para>The source is resolved by the implementation (from the request), not passed in, so callers in the
/// Application layer stay free of HTTP concerns.</para>
/// </summary>
public interface ILoginAttemptTracker
{
    /// <summary>
    /// True when <i>this request's source</i> has already used up its attempts against
    /// <paramref name="userId"/>. Checked before the password is verified, so a brute-force attempt is
    /// actually stopped rather than merely counted.
    /// </summary>
    bool IsLockedOutForCurrentSource(string userId);

    /// <summary>Records one failed attempt against <paramref name="userId"/> from this request's source.</summary>
    void RecordFailure(string userId);

    /// <summary>
    /// Clears this source's failures against <paramref name="userId"/> after a successful login, so a user
    /// who simply mistyped is not carrying a penalty into their next session.
    /// </summary>
    void ClearForCurrentSource(string userId);
}
