using ClinicManagement.API.Middleware;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The baseline browser-protection headers, and the two switches on them: HSTS (security-hardening US-12) and —
/// added by multi-tenant-cloud US-6 — <c>Security:EnforceCsp</c>.
///
/// <para><b>The CSP flag's default is the whole point.</b> An enforcing policy that has never been walked risks a
/// visually broken screen for a clinic instead of a console warning, so the header must stay report-only until an
/// operator says otherwise — <i>in every profile</i>. It is deliberately not derived from the deployment kind:
/// what makes enforcing safe is that somebody walked these pages in this deployment, and no capability knows
/// that.</para>
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    private const string Enforcing = "Content-Security-Policy";
    private const string ReportOnly = "Content-Security-Policy-Report-Only";

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    /// <summary>
    /// A response feature that <b>keeps</b> the <c>OnStarting</c> callbacks so a test can fire them.
    ///
    /// <para>Needed because <c>DefaultHttpContext</c>'s own <c>StartAsync</c> never invokes them — that is
    /// Kestrel's job — so without this the middleware would look like it writes no headers at all. It is worth
    /// exercising the real callback rather than extracting the decision into a pure function: the middleware's own
    /// docstring says writing on response start (and not after <c>next()</c>) is load-bearing, because by then the
    /// response may already be streaming.</para>
    /// </summary>
    private sealed class RecordingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new();

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state) { }

        public async Task FireStartingAsync()
        {
            HasStarted = true;
            foreach (var (callback, state) in _onStarting)
            {
                await callback(state);
            }
        }
    }

    private static async Task<IHeaderDictionary> HeadersFor(IConfiguration configuration, bool https = true)
    {
        var response = new RecordingResponseFeature();
        var context = Context(response, https);

        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, configuration);
        await middleware.InvokeAsync(context);
        await response.FireStartingAsync();

        return response.Headers;
    }

    private static DefaultHttpContext Context(RecordingResponseFeature response, bool https = true)
    {
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature { Scheme = https ? "https" : "http" });
        features.Set<IHttpResponseFeature>(response);
        features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(Stream.Null));

        return new DefaultHttpContext(features);
    }

    [Fact]
    public async Task The_policy_is_report_only_by_default()
    {
        var headers = await HeadersFor(Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
                                              ("DataProtection:KeyRingPath", "/keys")));

        Assert.True(headers.ContainsKey(ReportOnly));
        Assert.False(headers.ContainsKey(Enforcing));
    }

    [Fact]
    public async Task The_flag_promotes_it_to_enforcing()
    {
        var headers = await HeadersFor(Config(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.HostedMultiTenant)),
            ("DataProtection:KeyRingPath", "/keys"),
            (SecurityHeadersMiddleware.EnforceCspKey, "true")));

        Assert.True(headers.ContainsKey(Enforcing));
        Assert.False(headers.ContainsKey(ReportOnly));
    }

    [Theory]
    [InlineData(nameof(DeploymentKind.SelfHostedLan))]
    [InlineData(nameof(DeploymentKind.CloudBrowser))]
    public async Task Report_only_is_the_default_in_every_profile(string profile)
    {
        // Not derived from the topology — see the class summary. A hosted profile serving only JSON through this
        // middleware is *safer* to enforce, and it still defaults off, because the operator is the one who knows.
        var headers = await HeadersFor(Config((DeploymentProfile.ProfileKey, profile)));

        Assert.True(headers.ContainsKey(ReportOnly));
    }

    [Fact]
    public async Task The_policy_is_the_same_string_either_way()
    {
        var reportOnly = await HeadersFor(Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.CloudBrowser))));
        var enforced = await HeadersFor(Config(
            (DeploymentProfile.ProfileKey, nameof(DeploymentKind.CloudBrowser)),
            (SecurityHeadersMiddleware.EnforceCspKey, "true")));

        // The flag changes only the header NAME. A second policy string for the enforcing case would mean the
        // report-only walk had validated something other than what gets enforced.
        Assert.Equal(reportOnly[ReportOnly].ToString(), enforced[Enforcing].ToString());
    }

    [Fact]
    public async Task A_policy_an_upstream_component_already_set_is_never_overwritten()
    {
        // Two CSP headers make the browser enforce their INTERSECTION rather than either one (plan risk R-13).
        var response = new RecordingResponseFeature();
        response.Headers[Enforcing] = new StringValues("default-src 'none'");
        var context = Context(response);

        var middleware = new SecurityHeadersMiddleware(
            _ => Task.CompletedTask,
            Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.CloudBrowser))));

        await middleware.InvokeAsync(context);
        await response.FireStartingAsync();

        Assert.Equal("default-src 'none'", response.Headers[Enforcing].ToString());
        Assert.False(response.Headers.ContainsKey(ReportOnly));
    }

    [Fact]
    public async Task The_baseline_headers_are_always_present()
    {
        var headers = await HeadersFor(Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.CloudBrowser))));

        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
    }

    [Fact]
    public async Task Hsts_is_never_sent_over_plain_http()
    {
        // The Next BFF's loopback hop is plain HTTP; HSTS is meaningless there and would be recorded against
        // localhost.
        var headers = await HeadersFor(
            Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.CloudBrowser))), https: false);

        Assert.False(headers.ContainsKey("Strict-Transport-Security"));
    }

    /// <summary>
    /// ⚠️ <b>Behind a reverse proxy the edge owns HSTS, and this is the case that keeps it true</b>
    /// (hosted-security-hardening Part 2, step 7). It could not fail before that part, because
    /// <c>Request.IsHttps</c> was false for every proxied request; once <c>UseForwardedHeaders</c> makes it true,
    /// emitting here puts a <b>second</b> <c>Strict-Transport-Security</c> beside <c>deploy/Caddyfile</c>'s —
    /// Caddy appends rather than replaces, which was verified over the wire.
    /// </summary>
    [Theory]
    [InlineData(nameof(DeploymentKind.HostedMultiTenant))]
    [InlineData(nameof(DeploymentKind.CloudBrowser))]
    public async Task Hsts_Is_Left_To_The_Reverse_Proxy_On_Every_Hosted_Kind(string kind)
    {
        // Even over HTTPS — which is what a forwarded X-Forwarded-Proto now makes a proxied request look like —
        // and even with the operator's opt-in set, because the edge is not this process.
        var headers = await HeadersFor(
            Config(
                (DeploymentProfile.ProfileKey, kind),
                (SecurityHeadersMiddleware.EnableHstsKey, "true")),
            https: true);

        Assert.False(
            headers.ContainsKey("Strict-Transport-Security"),
            "deploy/Caddyfile sets HSTS at the edge; emitting it here too sends the client two headers whose "
            + "values differ, and RFC 6797 § 8.1 makes it honour only the first.");
    }

    // The other direction, so the change above cannot widen into « never emit HSTS ». Where the front door IS
    // this process there is no edge in front of it, and the opt-in is what turns it on.
    [Fact]
    public async Task Where_This_Process_Is_The_Edge_The_Operators_Opt_In_Turns_Hsts_On()
    {
        var withOptIn = await HeadersFor(
            Config(
                (DeploymentProfile.ProfileKey, nameof(DeploymentKind.SelfHostedLan)),
                (SecurityHeadersMiddleware.EnableHstsKey, "true")),
            https: true);

        var withoutOptIn = await HeadersFor(
            Config((DeploymentProfile.ProfileKey, nameof(DeploymentKind.SelfHostedLan))),
            https: true);

        Assert.Equal("max-age=31536000", withOptIn["Strict-Transport-Security"].ToString());
        Assert.False(withoutOptIn.ContainsKey("Strict-Transport-Security"));
    }
}
