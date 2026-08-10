using ClinicManagement.API.Startup;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// [AC-1.5] The console's sign-in is inside the deployment's <b>anonymous-authentication</b> bounds, per account
/// and per address — not the loose general API ceiling.
///
/// <para><b>Why this needs asserting rather than reading.</b> A route prefix the limiter does not recognise gets
/// the API window (600 / 60 s) instead of the auth window (30 / 300 s per account): a 20× looser bound, applied
/// silently, on the product's highest-privilege credential. Nothing fails, nothing logs, and the console simply
/// has a brute-force surface nobody chose.</para>
///
/// <para>⚠️ <b>The limiter and the account capture must widen together</b>, and the test that matters is
/// <see cref="The_capture_and_the_limiter_agree_about_what_an_auth_attempt_is"/>: <c>AuthAttemptAccount</c> asks
/// <c>RateLimiting.IsAnonymousAuthAttempt</c> rather than repeating its terms, so one predicate carries both —
/// but a future edit could reintroduce the second copy, and then the window would be per account while the
/// capture wrote nothing, i.e. every console attempt sharing one address bucket again.</para>
/// </summary>
public class PlatformRateLimitingTests
{
    private static HttpContext Post(string path, string? contentType = "application/json", long? length = 64)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.ContentType = contentType;
        context.Request.ContentLength = length;
        return context;
    }

    [Theory]
    [InlineData("/api/platform/auth/login")]
    [InlineData("/api/platform/auth/totp/enrol")]
    [InlineData("/api/platform/auth/recovery")]
    [InlineData("/api/platform/auth/password")]
    public void The_console_auth_prefix_is_an_anonymous_auth_path(string path)
    {
        Assert.True(RateLimiting.IsAnonymousAuthPath(path));
    }

    // The clinic's own prefix is untouched — this widened the predicate, it did not move it.
    [Fact]
    public void The_clinic_auth_prefix_still_matches()
    {
        Assert.True(RateLimiting.IsAnonymousAuthPath("/api/auth/login"));
    }

    // And the rest of the console is NOT inside the tight auth window: those are authenticated reads, and the
    // tight window on a portfolio the vendor is paging through would refuse ordinary work.
    [Theory]
    [InlineData("/api/platform/clinics")]
    [InlineData("/api/platform/summary")]
    [InlineData("/api/patients")]
    public void Everything_outside_the_two_auth_prefixes_is_not(string path)
    {
        Assert.False(RateLimiting.IsAnonymousAuthPath(path));
    }

    // The method test the clinic side already relies on: a GET is not a brute-force surface, and dropping reads
    // into the auth window is a 20× cut in sustained rate on routes that are not the attack.
    [Fact]
    public void Only_a_POST_counts_as_an_attempt()
    {
        var get = new DefaultHttpContext();
        get.Request.Method = HttpMethods.Get;
        get.Request.Path = "/api/platform/auth/login";

        Assert.False(RateLimiting.IsAnonymousAuthAttempt(get));
        Assert.True(RateLimiting.IsAnonymousAuthAttempt(Post("/api/platform/auth/login")));
    }

    // ⚠️ The one that keeps the two halves together. If a future edit gave AuthAttemptAccount its own copy of the
    // prefix list, the window could be per account while the capture wrote nothing for the console — reinstating
    // the shared-address bucket US-6 removed, silently.
    [Fact]
    public void The_capture_and_the_limiter_agree_about_what_an_auth_attempt_is()
    {
        var context = Post("/api/platform/auth/login");

        Assert.True(RateLimiting.IsAnonymousAuthAttempt(context));
        Assert.True(AuthAttemptAccount.ShouldCapture(context));
    }

    // The exemption list must not have swallowed the console: it is inside /api, so the global limiter applies.
    [Fact]
    public void The_console_is_not_exempt_from_the_global_limiter()
    {
        Assert.False(RateLimiting.IsExempt(Post("/api/platform/auth/login")));
        Assert.False(RateLimiting.IsExempt(Post("/api/platform/clinics")));
    }

    // An account key can never collide with an address key — an email is caller-supplied text, and the two forms
    // are prefixed for exactly that reason.
    [Fact]
    public void A_submitted_console_account_partitions_separately_from_a_bare_address()
    {
        var withAccount = Post("/api/platform/auth/login");
        withAccount.Items[RateLimiting.SubmittedAccountItemKey] = "ops@editeur.tn";

        var proxies = TrustedProxies.FromConfiguration(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());

        var accountKey = RateLimiting.AuthAttemptPartitionKey(withAccount, proxies);
        var addressKey = RateLimiting.AuthAttemptPartitionKey(Post("/api/platform/auth/login"), proxies);

        Assert.StartsWith("account:", accountKey);
        Assert.StartsWith("ip:", addressKey);
        Assert.NotEqual(accountKey, addressKey);
    }
}
