using ClinicManagement.Domain.Common;

namespace ClinicManagement.Domain.Entities;

/// <summary>How a presented refresh credential relates to the family that issued it.</summary>
public enum SessionCredentialMatch
{
    /// <summary>Belongs to no live family, or is older than the immediate predecessor — a replay.</summary>
    None = 0,

    /// <summary>The credential this family last minted.</summary>
    Current = 1,

    /// <summary>The one before it. Accepted, because two tabs exchanging at once must both keep working.</summary>
    Previous = 2
}

/// <summary>
/// One device's chain of refresh credentials (<c>hosted-security-hardening</c> FR-1.6).
///
/// <para><b>What it is for.</b> The refresh token is a stateless JWT and nothing stored it, so a stolen one could
/// be replayed until its own expiry with nothing able to notice. A family records which credential is current, so
/// presenting an <i>older</i> one is evidence the chain forked — either the user's copy or the thief's — and the
/// family is ended.</para>
///
/// <para>⚠️ <b>Ending a family ends ONE device's session, never the account.</b> Revoking globally would be an
/// invitation to denial-of-service: anyone holding one stale credential could sign a whole practice out at will,
/// mid-consultation. <c>User.TokenVersion</c> remains the account-wide lever and is deliberately untouched here.</para>
///
/// <para>⚠️ <b>The predecessor is accepted on purpose</b>, and it is what makes the sliding session survivable.
/// Two tabs (or a shell and a browser) legitimately exchange within moments of each other: the loser of that race
/// holds the credential the winner just superseded, and refusing it would sign a working user out for using the
/// product normally. The cost is one generation of slack in replay detection, which FR-1.6 states as its own
/// tolerance — the rule is about <i>ordering</i>, not elapsed time.</para>
///
/// <para>⚠️ <b>This type must be listed in <c>ApplicationDbContext.SkipsConcurrencyToken</c>.</b> That loop maps
/// <see cref="Entity{TId}.Version"/> onto PostgreSQL's <c>xmin</c> for every <see cref="Entity{TId}"/>, so two
/// tabs refreshing at once would both UPDATE this row, the loser would raise
/// <c>DbUpdateConcurrencyException</c> → <c>ConflictException</c>, and <c>/api/auth/refresh</c> would answer
/// <b>409</b> to precisely the case above. The opt-out's required argument is satisfied: a lost rotation loses no
/// information a user typed.</para>
/// </summary>
public class SessionFamily : Entity<Guid>
{
    private SessionFamily() { } // For EF Core

    public SessionFamily(string userId, string credentialHash, DateTime expiresAtUtc, string? deviceLabel = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Une session doit appartenir à un compte.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(credentialHash))
        {
            throw new ArgumentException("Une session doit porter un identifiant de jeton.", nameof(credentialHash));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        CurrentCredentialHash = credentialHash;
        DeviceLabel = Trimmed(deviceLabel);
        CreatedAt = DateTime.UtcNow;
        LastRotatedAt = CreatedAt;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    /// The owning account. A <c>string</c>, because <see cref="User"/>'s key is.
    ///
    /// <para>⚠️ <b>There is deliberately no <c>ClinicId</c> here</b>, which is <c>NotificationRead</c>'s shape and
    /// for a stronger reason than symmetry. A column named <c>ClinicId</c> is what
    /// <c>TenantScopeFilterTests</c> derives « clinic-owned » from, so carrying one would demand a query filter —
    /// and this table's only hot read is <b>by credential hash on the refresh path</b>, which runs before any
    /// clinic scope has been established. Under a filter that read matches nothing, replay detection never fires,
    /// and every legitimate refresh looks like an unknown credential. A row is reached by its hash or by
    /// <see cref="UserId"/>, and a user belongs to exactly one clinic.</para>
    /// </summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The credential this family last minted. Unique across the table — it is the lookup key.</summary>
    public string CurrentCredentialHash { get; private set; } = string.Empty;

    /// <summary>The one before it, accepted for the reason in the class remarks. Null on a fresh family.</summary>
    public string? PreviousCredentialHash { get; private set; }

    /// <summary>Free text for the « d'où venait cette session ? » line in the notification. Never a credential.</summary>
    public string? DeviceLabel { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime LastRotatedAt { get; private set; }

    /// <summary>When the family's own credential lifetime runs out. Drives the purge; never a revocation.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? EndedAtUtc { get; private set; }

    /// <summary>Why it ended — a replay, or an ordinary sign-out. Recorded so the feed row can say which.</summary>
    public string? EndedReason { get; private set; }

    public bool IsLive => EndedAtUtc is null;

    public User User { get; private set; } = null!;

    /// <summary>
    /// Where a presented credential sits in this family's chain.
    ///
    /// <para>An ended family matches nothing: once a replay has been detected, even the credential that was
    /// current at that moment is refused — the whole point is that the device starts again.</para>
    /// </summary>
    public SessionCredentialMatch Match(string credentialHash)
    {
        if (!IsLive || string.IsNullOrWhiteSpace(credentialHash))
        {
            return SessionCredentialMatch.None;
        }

        if (string.Equals(CurrentCredentialHash, credentialHash, StringComparison.Ordinal))
        {
            return SessionCredentialMatch.Current;
        }

        if (PreviousCredentialHash is not null
            && string.Equals(PreviousCredentialHash, credentialHash, StringComparison.Ordinal))
        {
            return SessionCredentialMatch.Previous;
        }

        return SessionCredentialMatch.None;
    }

    /// <summary>
    /// Advances the chain: previous ← current, current ← the newly minted credential.
    ///
    /// <para>Called on <b>every</b> successful exchange, including one that presented the predecessor — the
    /// racing tab is a legitimate user and gets a working credential of its own.</para>
    /// </summary>
    public void Rotate(string newCredentialHash, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(newCredentialHash))
        {
            throw new ArgumentException("Une rotation doit porter un nouvel identifiant.", nameof(newCredentialHash));
        }

        if (!IsLive)
        {
            throw new InvalidOperationException("Cette session a été interrompue.");
        }

        PreviousCredentialHash = CurrentCredentialHash;
        CurrentCredentialHash = newCredentialHash;
        LastRotatedAt = DateTime.UtcNow;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>Ends this family and only this one. Idempotent — a second call keeps the first reason.</summary>
    public void End(string reason)
    {
        if (!IsLive)
        {
            return;
        }

        EndedAtUtc = DateTime.UtcNow;
        EndedReason = Trimmed(reason);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
