using System.Security.Cryptography;
using ClinicManagement.Application.Common;
using Microsoft.Extensions.Caching.Memory;

namespace ClinicManagement.Infrastructure.Security;

/// <summary>
/// <see cref="IStepUpConfirmations"/> over <see cref="IMemoryCache"/>
/// (<c>hosted-security-hardening</c> FR-1.8), on the Google OAuth <c>state</c> cache's pattern.
///
/// <para>⚠️ <b>Must be registered <c>AddSingleton</c></b> — see the interface's own note. A scoped registration
/// makes every guarded action refuse, silently.</para>
/// </summary>
public class StepUpConfirmations : IStepUpConfirmations
{
    /// <summary>Long enough to complete the action it authorises, short enough that a walk-away expires it.</summary>
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The failure counter outlives a burst of attempts and then clears itself.</summary>
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly IMemoryCache _cache;

    public StepUpConfirmations(IMemoryCache cache)
    {
        _cache = cache;
    }

    public int MaxAttempts => 3;

    public string Issue(string userId, string action)
    {
        // 32 bytes of CSPRNG, URL-safe. Unguessable is the whole requirement: possession of the token IS the
        // proof, so it must not be derivable from the user, the action or the moment.
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        _cache.Set(
            ConfirmationKey(userId, action, token),
            true,
            // ABSOLUTE, never sliding — see the interface's note.
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ConfirmationLifetime });

        return token;
    }

    public bool Consume(string userId, string action, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var key = ConfirmationKey(userId, action, token);
        if (!_cache.TryGetValue(key, out _))
        {
            return false;
        }

        // Spent on use: one re-authentication authorises one action.
        _cache.Remove(key);
        return true;
    }

    public bool RecordFailureAndCheckExhausted(string userId)
    {
        var key = FailureKey(userId);
        var attempts = _cache.TryGetValue<int>(key, out var current) ? current + 1 : 1;

        _cache.Set(
            key,
            attempts,
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = FailureWindow });

        return attempts >= MaxAttempts;
    }

    public void ClearFailures(string userId) => _cache.Remove(FailureKey(userId));

    // The token is part of the key rather than the value, so a wrong token is a miss instead of a comparison —
    // there is nothing to compare in non-constant time.
    private static string ConfirmationKey(string userId, string action, string token) =>
        $"stepup:{userId}:{action}:{token}";

    private static string FailureKey(string userId) => $"stepup-failures:{userId}";
}
