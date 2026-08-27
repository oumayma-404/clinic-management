using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Rate-limiting policy shape (security-hardening US-4, audit § 2 finding 5).
///
/// The exemptions are as load-bearing as the limits, and each one guards a specific way the limiter could
/// break the app rather than protect it:
/// <list type="bullet">
///   <item><c>/api/connectivity</c> is polled every 15 s <b>per browser tab</b> — a 429 there makes the app
///   look offline and disables AI + Google Calendar (spec EC-5).</item>
///   <item><c>/hub</c> is a long-lived SignalR connection.</item>
///   <item>Everything outside <c>/api</c> is, in Local mode, the proxied Next application — a single page load
///   fires dozens of <c>_next</c> chunk requests through the same Kestrel front door.</item>
/// </list>
/// </summary>
public class RateLimitingTests
{
    private static HttpContext Request(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    [Theory]
    [InlineData("/api/connectivity")]           // EC-5: 15 s poll per tab
    [InlineData("/api/googlecalendar/callback")] // one-shot OAuth redirect
    [InlineData("/hub/clinic")]                  // long-lived SignalR connection
    [InlineData("/hangfire")]                    // loopback-only dashboard polling
    public void Exempt_paths_are_not_globally_limited(string path)
    {
        Assert.True(RateLimiting.IsExempt(Request(path)));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/appointments")]
    [InlineData("/_next/static/chunks/main.js")] // a page load fires dozens of these
    [InlineData("/login")]
    public void Proxied_frontend_traffic_is_not_globally_limited(string path)
    {
        // In Local mode Kestrel is the front door for ALL traffic, so limiting these would throttle page
        // loads rather than protect the API.
        Assert.True(RateLimiting.IsExempt(Request(path)));
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/appointments")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/invoices/123/payments")]
    public void Api_endpoints_are_globally_limited(string path)
    {
        Assert.False(RateLimiting.IsExempt(Request(path)));
    }

    [Fact]
    public void Exemption_matching_is_case_insensitive() // routes are not case-sensitive
    {
        Assert.True(RateLimiting.IsExempt(Request("/API/Connectivity")));
        Assert.True(RateLimiting.IsExempt(Request("/Hub/Clinic")));
    }

    [Fact]
    public void A_path_that_merely_starts_with_an_exempt_word_is_still_limited()
    {
        // Segment matching, not string prefix — /api/connectivity-report must not inherit the exemption.
        Assert.False(RateLimiting.IsExempt(Request("/api/connectivity-report")));
    }

    [Theory]
    [InlineData(1, "1 secondes")]
    [InlineData(30, "30 secondes")]
    [InlineData(59, "59 secondes")]
    public void The_message_reports_seconds_under_a_minute(int seconds, string expectedFragment)
    {
        var message = RateLimiting.TooManyRequestsMessage(seconds);

        Assert.Contains("Trop de tentatives", message, StringComparison.Ordinal);
        Assert.Contains(expectedFragment, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60, "1 minute")]
    [InlineData(90, "2 minutes")]
    [InlineData(300, "5 minutes")]
    public void The_message_rounds_up_to_minutes_past_a_minute(int seconds, string expectedFragment)
    {
        // AC-4.5: actionable — it says how long to wait, not just "try again later".
        var message = RateLimiting.TooManyRequestsMessage(seconds);

        Assert.Contains(expectedFragment, message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_message_is_french() // the whole UI is French; a 429 must not be the exception
    {
        Assert.StartsWith("Trop de tentatives", RateLimiting.TooManyRequestsMessage(120), StringComparison.Ordinal);
    }

    [Fact]
    public void The_health_endpoint_is_not_globally_limited()
    {
        // Polled every few seconds for the life of the deployment, and a 429 reads to an orchestrator exactly
        // like « unhealthy » (multi-tenant-cloud US-6).
        Assert.True(RateLimiting.IsExempt(Request(HealthChecks.Path)));
    }

    // ---- The auth limiter's partition: per (submitted ACCOUNT, address), address alone as the ceiling ----

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/setup")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/API/AUTH/LOGIN")] // routes are not case-sensitive
    public void The_anonymous_auth_surface_is_recognised(string path)
    {
        Assert.True(RateLimiting.IsAnonymousAuthPath(Request(path).Request.Path));
    }

    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/authors")]  // must not match on a mere prefix of the segment
    [InlineData("/api")]
    [InlineData("/")]
    public void Everything_else_is_not_the_anonymous_auth_surface(string path)
    {
        Assert.False(RateLimiting.IsAnonymousAuthPath(Request(path).Request.Path));
    }

    [Fact]
    public void An_attempt_that_named_an_account_is_partitioned_on_it()
    {
        // The whole point of US-6's re-key: a practice arrives through ONE public NAT address, so one colleague
        // mistyping their password must not spend their colleagues' budget.
        var first = Request("/api/auth/login");
        first.Items[RateLimiting.SubmittedAccountItemKey] = "amel@cabinet.tn";

        var second = Request("/api/auth/login");
        second.Items[RateLimiting.SubmittedAccountItemKey] = "bechir@cabinet.tn";

        Assert.NotEqual(Key(first), Key(second));
    }

    [Fact]
    public void One_account_attacked_from_elsewhere_keeps_its_owners_budget()
    {
        var fromTheClinic = Request("/api/auth/login");
        fromTheClinic.Items[RateLimiting.SubmittedAccountItemKey] = "amel@cabinet.tn";
        fromTheClinic.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");

        var fromElsewhere = Request("/api/auth/login");
        fromElsewhere.Items[RateLimiting.SubmittedAccountItemKey] = "amel@cabinet.tn";
        fromElsewhere.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("197.0.0.9");

        // The account alone used to be the whole key, which handed a permanent lockout to anyone who merely NAMED an
        // address-independent account: the permit is spent before authentication, on every attempt. The address is in
        // the key so a stranger empties only their own bucket. The hole that opens — one account from a hundred
        // addresses buying a hundred budgets — is closed by the separate per-address ceiling, not by this key.
        Assert.NotEqual(Key(fromTheClinic), Key(fromElsewhere));
    }

    [Fact]
    public void An_attempt_with_no_account_falls_back_to_the_address()
    {
        // POST auth/refresh carries no email at all, and a malformed body yields none. Those requests are bounded
        // exactly as every request was before the re-key — never exempt, and never sharing one « no-account »
        // bucket an attacker could empty for everybody.
        var context = Request("/api/auth/refresh");
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");

        Assert.Equal("ip:41.229.0.1", Key(context));
    }

    [Fact]
    public void An_account_key_can_never_collide_with_an_address_key()
    {
        var byAccount = Request("/api/auth/login");
        byAccount.Items[RateLimiting.SubmittedAccountItemKey] = "41.229.0.1";

        var byAddress = Request("/api/auth/login");
        byAddress.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");

        // An email is caller-supplied text, so without the prefixes an account literally named after an address
        // would share that address's budget.
        Assert.NotEqual(Key(byAccount), Key(byAddress));
    }

    [Fact]
    public void A_blank_captured_account_is_treated_as_no_account()
    {
        var context = Request("/api/auth/login");
        context.Items[RateLimiting.SubmittedAccountItemKey] = string.Empty;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("41.229.0.1");

        Assert.Equal("ip:41.229.0.1", Key(context));
    }

    // ---- The tight bounds apply to a POST, not to every /api/auth route (review finding 24) ----

    [Theory]
    [InlineData("POST", "/api/auth/login", true)]
    [InlineData("POST", "/api/auth/refresh", true)]
    // GET auth/mode is read on every app start by /join and /users, and change-password is authenticated: the
    // prefix alone dropped both from 600/60 s to 150/300 s, a 20× cut on routes nobody brute-forces.
    [InlineData("GET", "/api/auth/mode", false)]
    [InlineData("POST", "/api/patients", false)]
    public void Only_a_post_to_the_auth_surface_is_an_auth_attempt(string method, string path, bool expected)
    {
        var context = Request(path);
        context.Request.Method = method;

        Assert.Equal(expected, RateLimiting.IsAnonymousAuthAttempt(context));
    }

    private static string Key(HttpContext context) =>
        RateLimiting.AuthAttemptPartitionKey(context, TrustedProxies.LoopbackOnly);
}
