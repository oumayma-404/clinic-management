using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// <see cref="ITotpReplayGuard"/> over the shared <see cref="IMemoryCache"/>.
///
/// <para><b>Deliberately in-memory, like <see cref="LoginAttemptTracker"/>.</b> What has to be remembered is a
/// pair that stops mattering ~90 seconds later, so a durable table would add a write to an unauthenticated
/// endpoint on every successful sign-in to hold data that is worthless by the next minute. A restart forgets the
/// spent codes, which returns the window to its pre-existing behaviour for at most one step — and an attacker
/// cannot restart the service.</para>
///
/// <para><b>Known limit, stated rather than solved:</b> the cache is per process, so on a multi-instance hosted
/// deployment a replay landing on a different instance is not caught. The same limit the lockout tracker
/// documents, for the same reason, and it still closes the case that matters — a single-server clinic install,
/// and every replay that happens to be routed to the same instance.</para>
///
/// <para><b>Why the code and not a derived counter.</b> The step is not recoverable here without the secret, and
/// re-deriving it would mean a second HMAC and a second copy of the window rule. Remembering the digits for
/// slightly longer than the whole accepted window (<see cref="Retention"/>) is equivalent in effect: within that
/// span the same digits cannot be presented twice, and once it lapses the digits themselves no longer verify.</para>
/// </summary>
public sealed class TotpReplayGuard : ITotpReplayGuard
{
    /// <summary>
    /// How long a spent code is remembered. One 30-second step, plus the one step either side the verifier
    /// accepts, plus a step of slack for clock drift — so a code is forgotten only once it can no longer verify.
    /// </summary>
    public static readonly TimeSpan Retention = TimeSpan.FromSeconds(120);

    private const string KeyPrefix = "totp-spent";

    private readonly IMemoryCache _cache;

    public TotpReplayGuard(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryConsume(string userId, string code)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var key = $"{KeyPrefix}:{userId}:{code.Trim()}";

        // ⚠️ An ABSOLUTE expiry, never sliding: a sliding window would be refreshed by each replay attempt, so a
        // caller hammering the same code would keep it remembered for ever — harmless here, but the opposite
        // mistake (a sliding window on the thing being guarded) is how a guard quietly stops expiring at all.
        if (_cache.TryGetValue(key, out _))
        {
            return false;
        }

        _cache.Set(key, true, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention });
        return true;
    }
}
