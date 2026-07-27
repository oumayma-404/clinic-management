using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace ClinicManagement.Infrastructure.Auth;

/// <summary>
/// <see cref="ILoginAttemptTracker"/> over the shared <see cref="IMemoryCache"/>, partitioned by
/// (account, source address) — see the interface for why the source matters.
///
/// <para><b>Deliberately in-memory.</b> Lockout state is transient (a sliding <see cref="Window"/>), so a
/// durable table would buy little while adding a write to an unauthenticated endpoint on every failed
/// attempt. A restart clears the counters, which is acceptable: an attacker cannot restart the service, and
/// the durable per-account backstop on <c>User</c> survives regardless.</para>
///
/// <para><b>Known limit.</b> The cache is per process, so a multi-instance Cloud deployment gives an attacker
/// N times the per-source budget. Irrelevant for a Local install (one server), and the durable per-account
/// backstop still bounds the total either way. Documented rather than solved.</para>
///
/// <para>Uses a <b>sliding</b> window on purpose: a source that keeps hammering stays locked out, rather than
/// getting a fresh allowance a fixed interval after its first attempt.</para>
/// </summary>
public sealed class LoginAttemptTracker : ILoginAttemptTracker
{
    /// <summary>Failures from one source against one account before that source is refused.</summary>
    public const int MaxAttemptsPerSource = 5;

    /// <summary>How long a source's failures are remembered, refreshed by each new failure.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private const string KeyPrefix = "login-attempts";

    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginAttemptTracker(IMemoryCache cache, IHttpContextAccessor httpContextAccessor)
    {
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsLockedOutForCurrentSource(string userId) =>
        _cache.TryGetValue(CacheKey(userId), out int failures) && failures >= MaxAttemptsPerSource;

    public void RecordFailure(string userId)
    {
        var key = CacheKey(userId);
        var failures = _cache.TryGetValue(key, out int existing) ? existing + 1 : 1;

        _cache.Set(key, failures, new MemoryCacheEntryOptions { SlidingExpiration = Window });
    }

    public void ClearForCurrentSource(string userId) => _cache.Remove(CacheKey(userId));

    /// <summary>
    /// Partition key. The source comes from <see cref="ClientIp"/>, which honours <c>X-Forwarded-For</c> only
    /// from a loopback peer (our own BFF), so a LAN client cannot vary the header to escape its own bucket.
    /// </summary>
    private string CacheKey(string userId)
    {
        var context = _httpContextAccessor.HttpContext;
        var source = context is null ? ClientIp.Unknown : ClientIp.Resolve(context);

        return $"{KeyPrefix}:{userId}:{source}";
    }
}
