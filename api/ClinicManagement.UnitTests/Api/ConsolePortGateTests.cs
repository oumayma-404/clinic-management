using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The console's two-way boundary (<c>platform-console</c> FR-2, AC-1.7, AC-1.8, EC-4).
///
/// <para><b>Why a pure predicate can carry this much weight.</b> Binding a port is not scoping a surface — every
/// endpoint answers on every bound port — so the whole of « the console's routes are absent from the public
/// address, and the clinic's are absent from the console's » is <see cref="ConsolePortGate.ShouldRefuse"/>. The
/// interesting cases are all boundary ones, and each is a plain assertion here rather than something only a
/// tunnelled walk could show.</para>
/// </summary>
public class ConsolePortGateTests
{
    private const int PublicPort = 5000;
    private const int ConsolePort = 5443;

    // [AC-1.7] The direction the product does not have anywhere else: a console path must not answer on the
    // address clinics reach. This is the one that publishes cross-cabinet reads to the internet if it regresses.
    [Theory]
    [InlineData("/api/platform/summary")]
    [InlineData("/api/platform/auth/login")]
    [InlineData("/api/platform/clinics")]
    public void A_console_path_on_the_public_port_is_refused(string path)
    {
        Assert.True(ConsolePortGate.ShouldRefuse(PublicPort, ConsolePort, path));
    }

    // [FR-2] And the mirror: the console's listener serves the console and nothing else, so a clinic route
    // reached through the tunnel is refused too. TrustPortGate is one-way; this one is not.
    [Theory]
    [InlineData("/api/patients")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/billing/caisse")]
    [InlineData("/health")]
    public void A_clinic_path_on_the_console_port_is_refused(string path)
    {
        Assert.True(ConsolePortGate.ShouldRefuse(ConsolePort, ConsolePort, path));
    }

    [Fact]
    public void A_console_path_on_the_console_port_is_allowed()
    {
        Assert.False(ConsolePortGate.ShouldRefuse(ConsolePort, ConsolePort, "/api/platform/auth/login"));
    }

    [Fact]
    public void A_clinic_path_on_the_public_port_is_untouched()
    {
        Assert.False(ConsolePortGate.ShouldRefuse(PublicPort, ConsolePort, "/api/patients"));
    }

    // The prefix trap TrustPortGate already documents. `/api/platform-ish` shares the prefix as TEXT while being
    // a different endpoint, so a StartsWith would let it answer on the console port — reopening the hole by typo.
    [Theory]
    [InlineData("/api/platform-ish")]
    [InlineData("/api/platformer")]
    [InlineData("/api/platform-console")]
    public void A_path_that_merely_starts_with_the_same_letters_is_not_a_console_path(string path)
    {
        Assert.False(ConsolePortGate.IsConsolePath(path));

        // …so on the console port it is refused like any other non-console path,
        Assert.True(ConsolePortGate.ShouldRefuse(ConsolePort, ConsolePort, path));
        // …and on the public port it is ordinary traffic.
        Assert.False(ConsolePortGate.ShouldRefuse(PublicPort, ConsolePort, path));
    }

    [Fact]
    public void The_prefix_match_is_case_insensitive()
    {
        Assert.True(ConsolePortGate.IsConsolePath("/API/Platform/Summary"));
    }

    // [AC-1.8] OFF MEANS ABSENT, and this is where this gate deliberately parts company with TrustPortGate,
    // whose 0 refuses nothing. With no console bound, a console path must 404 EVERYWHERE — otherwise the routes
    // are still mapped and still answering on the public listener, which is « present and refusing » at best and
    // « present and serving » in fact.
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void With_the_console_off_every_console_path_is_refused_and_nothing_else_is(int consolePort)
    {
        Assert.True(ConsolePortGate.ShouldRefuse(PublicPort, consolePort, "/api/platform/summary"));
        Assert.False(ConsolePortGate.ShouldRefuse(PublicPort, consolePort, "/api/patients"));
    }
}

/// <summary>
/// The two ports a hosted deployment binds when the console is on (<c>platform-console</c> EC-4, risk R-3a).
///
/// <para><b>The load-bearing case is <see cref="The_public_port_is_resolved_from_the_url_list_the_deployment_already_uses"/>.</b>
/// In <c>HostedMultiTenant</c> nothing calls <c>ConfigureKestrel</c> today and <c>ASPNETCORE_URLS</c> alone binds
/// 5000 — and an explicit Kestrel endpoint overrides that configuration wholesale. So if this resolver failed to
/// find the public port, the console would bind alone and take the entire product offline while working
/// perfectly itself.</para>
/// </summary>
public class ConsoleListenerPlanTests
{
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    // The wildcard hosts Kestrel accepts — and exactly what deploy/docker-compose.hosted.yml sets. `Uri` cannot
    // parse either, which is why the resolver reads the port off the last colon instead.
    [Theory]
    [InlineData("http://+:5000", 5000)]
    [InlineData("http://*:5000", 5000)]
    [InlineData("http://0.0.0.0:8080", 8080)]
    [InlineData("http://+:5000;https://+:5001", 5000)]
    public void The_public_port_is_resolved_from_the_url_list_the_deployment_already_uses(string urls, int expected)
    {
        Assert.Equal(expected, ConsoleListenerPlanning.ResolvePublicPort(Configuration(("ASPNETCORE_URLS", urls))));
    }

    [Fact]
    public void Hosting_Urls_wins_over_ASPNETCORE_URLS()
    {
        var configuration = Configuration(
            ("Hosting:Urls", "http://+:7000"),
            ("ASPNETCORE_URLS", "http://+:5000"));

        Assert.Equal(7000, ConsoleListenerPlanning.ResolvePublicPort(configuration));
    }

    [Fact]
    public void Hosting_HttpPort_is_the_third_source_and_5000_the_last()
    {
        Assert.Equal(5100, ConsoleListenerPlanning.ResolvePublicPort(Configuration(("Hosting:HttpPort", "5100"))));
        Assert.Equal(
            ConsoleListenerPlanning.DefaultPublicPort,
            ConsoleListenerPlanning.ResolvePublicPort(Configuration()));
    }

    // An unparseable URL list must not be the thing that stops a deployment starting when the console is merely
    // being switched on — it falls through to the next source, which is what the host itself would end up using.
    [Theory]
    [InlineData("not-a-url")]
    [InlineData("http://+")]
    [InlineData("")]
    public void An_unparsable_url_list_falls_through_rather_than_throwing(string urls)
    {
        var configuration = Configuration(("ASPNETCORE_URLS", urls), ("Hosting:HttpPort", "5100"));

        Assert.Equal(5100, ConsoleListenerPlanning.ResolvePublicPort(configuration));
    }

    [Fact]
    public void The_plan_binds_both_ports()
    {
        var plan = ConsoleListenerPlanning.Resolve(
            Configuration(("ASPNETCORE_URLS", "http://+:5000")), consoleEnabled: true, consolePort: 5443);

        Assert.NotNull(plan);
        Assert.Equal(5000, plan!.PublicPort);
        Assert.Equal(5443, plan.ConsolePort);
    }

    // [EC-4] And the collision refuses startup. ⚠️ Derived from the port actually resolved for binding, NOT from
    // Hosting:HttpPort/HttpsPort/WebPort — none of those three keys is set in the hosted compose file, so a check
    // written against them would pass cheerfully while the two listeners genuinely collided, i.e. an EC-4 guard
    // that cannot fire in the one profile the console exists on. This case is that guard firing there.
    [Fact]
    public void A_collision_with_the_port_in_ASPNETCORE_URLS_refuses_startup()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ConsoleListenerPlanning.Resolve(
                Configuration(("ASPNETCORE_URLS", "http://+:5000")), consoleEnabled: true, consolePort: 5000));

        // The message must name both settings, or an operator cannot act on it.
        Assert.Contains("Console:Port", exception.Message);
        Assert.Contains("ASPNETCORE_URLS", exception.Message);
    }

    [Fact]
    public void A_collision_with_Hosting_HttpPort_also_refuses_startup()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConsoleListenerPlanning.Resolve(
                Configuration(("Hosting:HttpPort", "5000")), consoleEnabled: true, consolePort: 5000));
    }

    // Off ⇒ no plan ⇒ Program.cs touches no Kestrel configuration at all, which is what keeps SelfHostedLan and
    // CloudBrowser byte-for-byte as they were.
    [Theory]
    [InlineData(false, 5443)]
    [InlineData(true, 0)]
    public void With_the_console_off_there_is_no_plan(bool enabled, int consolePort)
    {
        Assert.Null(ConsoleListenerPlanning.Resolve(Configuration(), enabled, consolePort));
    }
}
