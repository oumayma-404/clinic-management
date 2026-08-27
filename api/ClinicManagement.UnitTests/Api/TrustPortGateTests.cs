using ClinicManagement.API.Startup;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The port-scoped restriction that makes the cleartext trust listener safe (P8, AC-44 / risk R-11).
///
/// This is the guard for the one way P8 could quietly undo a Phase-4 hardening decision. Binding
/// <c>ListenAnyIP(trustPort)</c> publishes <b>every</b> route on that port — Kestrel listeners are not scoped
/// to a subset of endpoints — so without this gate the trust port would expose the whole cleartext API on the
/// LAN, <c>POST /api/auth/login</c> included. That is exactly the exposure <c>ListenLocalhost(5000)</c> exists
/// to prevent, so the assertion that matters here is the <b>negative</b> one: a non-trust path on the trust
/// port is refused.
/// </summary>
public class TrustPortGateTests
{
    private const int TrustPort = 5080;
    private const int HttpsPort = 5001;

    [Theory] // [AC-44] the trust page and its assets are the ONLY things reachable on the cleartext port
    [InlineData("/api/trust")]
    [InlineData("/api/trust/ca.crt")]
    [InlineData("/api/trust/profile.mobileconfig")]
    [InlineData("/api/trust/qr.png")]
    public void Trust_paths_are_served_on_the_trust_port(string path)
    {
        Assert.False(TrustPortGate.ShouldRefuse(TrustPort, TrustPort, new PathString(path)));
    }

    [Theory] // [R-11] the whole point: nothing else answers in cleartext on the LAN
    [InlineData("/api/auth/login")]
    [InlineData("/api/patients")]
    [InlineData("/api/invoices")]
    [InlineData("/hub/clinic")]
    [InlineData("/hangfire")]
    [InlineData("/")]
    [InlineData("/login")]
    public void Every_other_path_is_refused_on_the_trust_port(string path)
    {
        Assert.True(TrustPortGate.ShouldRefuse(TrustPort, TrustPort, new PathString(path)));
    }

    [Fact] // a prefix that merely shares letters is a different endpoint, and must not slip through
    public void A_path_that_only_shares_the_prefix_text_is_refused()
    {
        Assert.True(TrustPortGate.ShouldRefuse(TrustPort, TrustPort, new PathString("/api/trusted-devices")));
    }

    [Fact] // casing is not a bypass
    public void The_prefix_match_is_case_insensitive()
    {
        Assert.False(TrustPortGate.ShouldRefuse(TrustPort, TrustPort, new PathString("/API/Trust/ca.crt")));
    }

    [Theory] // the restriction is one-way: the front door keeps serving the whole application
    [InlineData("/api/auth/login")]
    [InlineData("/api/patients")]
    [InlineData("/api/trust")]
    public void Nothing_is_refused_on_the_https_front_door(string path)
    {
        Assert.False(TrustPortGate.ShouldRefuse(HttpsPort, TrustPort, new PathString(path)));
    }

    [Theory] // Hosting:TrustPort = 0 switches the feature off; the gate must then never refuse anything
    [InlineData(0)]
    [InlineData(-1)]
    public void A_disabled_trust_port_gates_nothing(int disabled)
    {
        Assert.False(TrustPortGate.ShouldRefuse(HttpsPort, disabled, new PathString("/api/patients")));
        Assert.False(TrustPortGate.ShouldRefuse(disabled, disabled, new PathString("/api/patients")));
    }

    [Fact] // the bind and the page that advertises its own address must agree on the default
    public void The_default_port_does_not_collide_with_the_other_bound_ports()
    {
        Assert.NotEqual(5000, TrustPortGate.DefaultPort); // API HTTP (loopback)
        Assert.NotEqual(5001, TrustPortGate.DefaultPort); // HTTPS front door
        Assert.NotEqual(3000, TrustPortGate.DefaultPort); // Next web server (loopback)
        Assert.NotEqual(5432, TrustPortGate.DefaultPort); // PostgreSQL
        Assert.True(TrustPortGate.DefaultPort > 0);
    }
}
