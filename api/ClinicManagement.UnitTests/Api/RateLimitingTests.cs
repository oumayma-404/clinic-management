using ClinicManagement.API.Startup;
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
}
