using ClinicManagement.Domain.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Common;

/// <summary>
/// The rule for an outbound endpoint a <b>tenant</b> may name (<c>SECURITY_REVIEW_2026-08</c>, finding A).
///
/// <para>
/// These endpoints are typed in by a clinic admin and dialled by a background job running inside the API
/// container, so without this rule each one is a server-side request primitive aimed wherever the tenant chose —
/// the container's loopback (which the Hangfire dashboard trusts), a sibling service on the compose network, or a
/// cloud metadata address.
/// </para>
/// </summary>
public class OutboundEndpointTests
{
    private const string Label = "L'URL de la passerelle SMS";

    // Absent is not invalid: clearing an endpoint hands the channel back to the install default.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_Is_Not_Configured_Rather_Than_Refused(string? value)
    {
        Assert.Null(OutboundEndpoint.ValidateUrl(value, Label, allowPrivateNetwork: false));
        Assert.Null(OutboundEndpoint.ValidateHost(value, Label, allowPrivateNetwork: false));
    }

    [Fact]
    public void A_Public_Https_Url_Is_Accepted()
    {
        Assert.Equal(
            "https://gateway.example.com/send",
            OutboundEndpoint.ValidateUrl("  https://gateway.example.com/send  ", Label, allowPrivateNetwork: false));
    }

    // Plain HTTP is refused on a hosted deployment: the credential travels with the request.
    [Fact]
    public void Plain_Http_Is_Refused_When_Private_Endpoints_Are_Not_Allowed()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => OutboundEndpoint.ValidateUrl("http://gateway.example.com", Label, allowPrivateNetwork: false));
        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The addresses that make this a security rule rather than a validation nicety. Each is reachable from
    /// inside the API container and none belongs to the tenant.
    /// </summary>
    [Theory]
    [InlineData("https://127.0.0.1/send")]            // the loopback LocalRequest.IsLoopback trusts
    [InlineData("https://localhost/send")]
    [InlineData("https://169.254.169.254/latest")]    // cloud instance metadata
    [InlineData("https://10.0.0.5/send")]             // RFC1918
    [InlineData("https://192.168.1.10/send")]
    [InlineData("https://172.16.0.9/send")]
    [InlineData("https://minio/send")]                // a compose service name — single label, never public
    [InlineData("https://api.internal/send")]
    [InlineData("https://[::1]/send")]
    public void Internal_Targets_Are_Refused(string url)
    {
        Assert.Throws<ArgumentException>(
            () => OutboundEndpoint.ValidateUrl(url, Label, allowPrivateNetwork: false));
    }

    /// <summary>
    /// An IPv4-mapped IPv6 literal must be re-checked as IPv4 — otherwise <c>::ffff:127.0.0.1</c> walks straight
    /// past the v4 rules by being the wrong address family.
    /// </summary>
    [Fact]
    public void An_IPv4_Mapped_Loopback_Cannot_Slip_Through_As_IPv6()
    {
        Assert.Throws<ArgumentException>(
            () => OutboundEndpoint.ValidateUrl("https://[::ffff:127.0.0.1]/send", Label, allowPrivateNetwork: false));
    }

    // On a clinic's own PC the private range IS the clinic's network, so the same targets are legitimate.
    [Fact]
    public void A_Lan_Install_May_Name_A_Private_Endpoint()
    {
        Assert.Equal(
            "http://192.168.1.50:8080/send",
            OutboundEndpoint.ValidateUrl("http://192.168.1.50:8080/send", Label, allowPrivateNetwork: true));
    }

    // SMTP is configured as a bare host, so anything that would change what is actually dialled is refused.
    [Theory]
    [InlineData("https://smtp.example.com")]
    [InlineData("smtp.example.com:587")]
    [InlineData("smtp.example.com/path")]
    [InlineData("user@smtp.example.com")]
    public void An_Smtp_Host_Must_Be_A_Host_Alone(string host)
    {
        Assert.Throws<ArgumentException>(
            () => OutboundEndpoint.ValidateHost(host, "Le serveur SMTP", allowPrivateNetwork: false));
    }

    [Fact]
    public void A_Public_Smtp_Host_Is_Accepted()
    {
        Assert.Equal(
            "smtp-relay.brevo.com",
            OutboundEndpoint.ValidateHost("smtp-relay.brevo.com", "Le serveur SMTP", allowPrivateNetwork: false));
    }
}
