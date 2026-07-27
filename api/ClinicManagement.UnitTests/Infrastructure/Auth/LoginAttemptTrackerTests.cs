using System.Net;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Auth;

/// <summary>
/// Per-(account, source) failed-login tracking (security-hardening US-4 / AC-4.2).
///
/// The behaviour that matters: a hostile host locks out <b>only itself</b>, while a colleague on another
/// machine signs in normally. The previous account-only lockout let one LAN device keep every staff account —
/// admin included — permanently locked, which is the finding this closes.
/// </summary>
public sealed class LoginAttemptTrackerTests : IDisposable
{
    private const string UserId = "local|11111111-1111-1111-1111-111111111111";
    private const string OtherUserId = "local|22222222-2222-2222-2222-222222222222";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly HttpContextAccessor _accessor = new();
    private readonly LoginAttemptTracker _tracker;

    public LoginAttemptTrackerTests()
    {
        _tracker = new LoginAttemptTracker(_cache, _accessor);
    }

    public void Dispose() => _cache.Dispose();

    /// <summary>Points the tracker at a request coming from <paramref name="peer"/>.</summary>
    private void RequestFrom(string peer, string? forwardedFor = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
        if (forwardedFor is not null)
        {
            context.Request.Headers[ClientIp.ForwardedForHeader] = forwardedFor;
        }

        _accessor.HttpContext = context;
    }

    private void BurnAttempts(int count)
    {
        for (var i = 0; i < count; i++)
        {
            _tracker.RecordFailure(UserId);
        }
    }

    [Fact]
    public void A_fresh_source_is_not_locked_out()
    {
        RequestFrom("192.168.1.42");

        Assert.False(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void The_threshold_locks_the_source_out()
    {
        RequestFrom("192.168.1.42");

        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource - 1);
        Assert.False(_tracker.IsLockedOutForCurrentSource(UserId));

        _tracker.RecordFailure(UserId); // crosses it
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void A_hostile_host_locks_out_only_itself() // [AC-4.2] — the whole point
    {
        RequestFrom("192.168.1.99");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));

        // A colleague on a different machine, same account, is unaffected.
        RequestFrom("192.168.1.42");
        Assert.False(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void Attempts_against_one_account_do_not_lock_another()
    {
        RequestFrom("192.168.1.99");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);

        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));
        Assert.False(_tracker.IsLockedOutForCurrentSource(OtherUserId));
    }

    [Fact]
    public void Two_browsers_behind_the_bff_are_tracked_separately() // relies on ClientIp, not the peer
    {
        // Both arrive from loopback (the BFF hop); only the forwarded address distinguishes them. If this
        // regressed, one clinic PC's five mistakes would lock out the entire clinic.
        RequestFrom("127.0.0.1", forwardedFor: "192.168.1.99");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));

        RequestFrom("127.0.0.1", forwardedFor: "192.168.1.42");
        Assert.False(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void A_lan_client_cannot_shed_its_lockout_by_forging_the_header()
    {
        RequestFrom("192.168.1.99");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));

        // The header is only honoured from a loopback peer, so varying it changes nothing here.
        RequestFrom("192.168.1.99", forwardedFor: "10.0.0.7");
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void A_successful_login_clears_this_source() // a mistyped password must not carry a penalty
    {
        RequestFrom("192.168.1.42");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));

        _tracker.ClearForCurrentSource(UserId);
        Assert.False(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void Clearing_one_source_leaves_another_locked()
    {
        RequestFrom("192.168.1.99");
        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);

        RequestFrom("192.168.1.42");
        _tracker.ClearForCurrentSource(UserId);

        RequestFrom("192.168.1.99");
        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));
    }

    [Fact]
    public void With_no_request_in_scope_attempts_share_one_constrained_bucket()
    {
        // Background/job contexts have no HttpContext. They must not each get an unlimited allowance.
        _accessor.HttpContext = null;

        BurnAttempts(LoginAttemptTracker.MaxAttemptsPerSource);

        Assert.True(_tracker.IsLockedOutForCurrentSource(UserId));
    }
}
