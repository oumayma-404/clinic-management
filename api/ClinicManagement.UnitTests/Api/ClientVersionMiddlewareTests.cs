using ClinicManagement.API.Controllers;
using ClinicManagement.API.Middleware;
using ClinicManagement.API.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The client-version floor (<c>mobile-native-shells</c> Part 3, AC-29…AC-32, AC-34).
///
/// <para>Two thirds of this class asserts what the middleware must <b>not</b> do, and that is where its value is.
/// A floor that refuses too much is far worse than no floor: this runs in front of authentication in every
/// deployment profile, so a wrong « below » verdict takes the whole API away from every browser in the clinic —
/// and the symptom (a 426 nothing in the web app has ever handled) reads as the server being down. Hence the
/// theories over absent, blank, malformed, equal and newer versions, and over an unset or typo'd floor.</para>
///
/// <para>The middleware is exercised through a real <see cref="DefaultHttpContext"/> rather than mocked: the two
/// things most likely to be wrong are the <b>path scoping</b> and the <b>response shape</b>, and neither is
/// visible in an interface.</para>
/// </summary>
public class ClientVersionMiddlewareTests
{
    private const string Floor = "1.2.0";

    /// <summary>An ordinary guarded route — anything under <c>/api</c> that is not the meta one.</summary>
    private const string GuardedPath = "/api/patients";

    private static IConfiguration Configuration(string? minimumShellVersion) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clients:MinimumShellVersion"] = minimumShellVersion,
                ["Clients:CurrentShellVersion"] = "1.4.0",
                ["Clients:StoreUrls:Android"] = "https://play.google.com/store/apps/details?id=test",
                ["Clients:StoreUrls:Ios"] = "https://apps.apple.com/app/id000000000",
            })
            .Build();

    /// <summary>
    /// Runs the middleware over one request and reports whether the pipeline continued. The body is captured
    /// because AC-30 is about the shape of the refusal, not only its status.
    /// </summary>
    private static async Task<(int Status, string Body, bool ReachedNext)> InvokeAsync(
        string path,
        string? clientVersion,
        string? floor = Floor)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (clientVersion is not null)
        {
            context.Request.Headers[ClientVersionMiddleware.HeaderName] = clientVersion;
        }

        var body = new MemoryStream();
        context.Response.Body = body;

        var reachedNext = false;
        var middleware = new ClientVersionMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, Configuration(floor));

        body.Position = 0;
        return (context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reachedNext);
    }

    [Fact] // [AC-30] a shell below the floor is refused, with the canonical { error } body plus the code
    public async Task A_client_below_the_floor_is_refused_with_426_and_the_code()
    {
        var (status, body, reachedNext) = await InvokeAsync(GuardedPath, "1.1.9");

        Assert.Equal(StatusCodes.Status426UpgradeRequired, status);
        Assert.False(reachedNext);
        Assert.Contains("\"code\":\"client_too_old\"", body);
        Assert.Contains("\"error\":", body);
        Assert.Contains("jour", body); // French, not a framework status line
    }

    [Fact] // [AC-30] the refusal reaches every guarded route, not just the one above
    public async Task Every_api_route_is_guarded()
    {
        foreach (var path in new[] { "/api/auth/login", "/api/invoices", "/api/dashboard", "/api/billing/caisse" })
        {
            var (status, _, reachedNext) = await InvokeAsync(path, "1.0.0");

            Assert.Equal(StatusCodes.Status426UpgradeRequired, status);
            Assert.False(reachedNext);
        }
    }

    [Theory] // [AC-32] anything unreadable is accepted UNCHANGED — the browser and every BFF hop live here
    [InlineData(null)]        // no header at all: every browser, and every server-side proxy leg
    [InlineData("")]          // present but empty
    [InlineData("   ")]
    [InlineData("nightly")]   // malformed
    [InlineData("v1.0.0")]    // malformed: a leading v is not a Version
    [InlineData("1.0.0-beta")]
    public async Task An_absent_or_malformed_version_passes(string? clientVersion)
    {
        var (status, _, reachedNext) = await InvokeAsync(GuardedPath, clientVersion);

        Assert.True(reachedNext);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory] // [AC-30] the floor is a floor: at it and above it are both fine
    [InlineData("1.2.0")]
    [InlineData("1.2.1")]
    [InlineData("1.3.0")]
    [InlineData("2.0.0")]
    [InlineData("1.2.0.1")]
    public async Task A_client_at_or_above_the_floor_passes(string clientVersion)
    {
        var (_, _, reachedNext) = await InvokeAsync(GuardedPath, clientVersion);

        Assert.True(reachedNext);
    }

    [Fact] // [AC-29] the one route that says WHERE to update must stay readable by the clients being refused
    public async Task The_meta_route_is_exempt_from_the_floor_it_publishes()
    {
        var (status, _, reachedNext) = await InvokeAsync(MetaController.ClientRequirementsPath, "0.0.1");

        Assert.True(reachedNext);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory] // [AC-34] an unset or unusable floor refuses NOTHING — the safe direction for operator-owned config
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public async Task An_unset_or_unparseable_floor_refuses_nothing(string? floor)
    {
        var (_, _, reachedNext) = await InvokeAsync(GuardedPath, "0.0.1", floor);

        Assert.True(reachedNext);
    }

    [Theory] // the web app itself is served through the same Kestrel front door; 426-ing it would replace the
             // French update state with raw JSON, and the hub and BFF legs are AC-32's "unaffected" cases
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/_next/static/chunk.js")]
    [InlineData("/bff/auth/token")]
    [InlineData("/hub/clinic")]
    public async Task Nothing_outside_the_api_prefix_is_refused(string path)
    {
        var (_, _, reachedNext) = await InvokeAsync(path, "0.0.1");

        Assert.True(reachedNext);
    }

    [Fact] // [AC-28] what the exempt route publishes is read from the same object the refusal is measured against
    public void The_published_requirements_come_from_operator_configuration()
    {
        var requirements = ClientRequirements.Read(Configuration(Floor));

        Assert.Equal(Floor, requirements.MinimumShellVersion);
        Assert.Equal("1.4.0", requirements.CurrentShellVersion);
        Assert.StartsWith("https://play.google.com/", requirements.StoreUrls.Android);
        Assert.StartsWith("https://apps.apple.com/", requirements.StoreUrls.Ios);
    }

    [Fact] // absent configuration yields empty strings, never null — the DTO is serialized straight to a client
    public void Absent_configuration_publishes_empty_strings()
    {
        var requirements = ClientRequirements.Read(new ConfigurationBuilder().Build());

        Assert.Equal(string.Empty, requirements.MinimumShellVersion);
        Assert.Equal(string.Empty, requirements.CurrentShellVersion);
        Assert.Equal(string.Empty, requirements.StoreUrls.Android);
        Assert.Equal(string.Empty, requirements.StoreUrls.Ios);
    }
}
