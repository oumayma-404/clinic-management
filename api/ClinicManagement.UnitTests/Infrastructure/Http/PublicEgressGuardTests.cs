using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Domain.Common;
using ClinicManagement.Infrastructure.Http;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Http;

/// <summary>
/// The connect-time half of the SSRF rule — « the half that is owed », as
/// <see cref="OutboundEndpoint"/>'s own docstring had said since it was written.
///
/// <para><b>What it closes.</b> <c>OutboundEndpoint</c> checks literals, and <c>IPAddress.TryParse</c> returns
/// false for every hostname — so a hostname passed <b>unconditionally</b>. On the hosted deployment public
/// signup makes anyone a clinic admin, and a clinic admin could point <c>smtpHost</c> at
/// <c>127.0.0.1.nip.io</c> and have the API container dial its own loopback. The HTTP integrations were covered
/// only by the accident that <c>https</c> is forced; SMTP had nothing at all.</para>
///
/// <para>⚠️ <b>These tests use names that resolve without a network, or no name at all.</b> A test that asks a
/// real resolver about a real domain fails on a machine with no DNS and passes for the wrong reason on a
/// machine behind a captive portal — and CI is both, at different times. So the address rule is exercised
/// through <see cref="OutboundEndpoint.IsPublicAddress"/> (pure) and the guard itself through IP literals,
/// which it answers without a lookup.</para>
/// </summary>
public class PublicEgressGuardTests
{
    // ⚠️ The literal forms `OutboundEndpoint` already refuses at save time are NOT the interesting ones here —
    // this class is about what reaches the socket. What matters is that both halves agree, which they do by
    // construction: the guard calls IsPublicAddress rather than re-deriving the ranges.
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.9")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")] // cloud metadata — the one that pays for an attacker
    [InlineData("100.64.0.1")]      // CGNAT
    [InlineData("::1")]
    [InlineData("fd00::1")]         // unique-local
    [InlineData("::ffff:127.0.0.1")] // IPv4-mapped loopback, which must not walk through the v6 rules
    public void A_Private_Address_Is_Refused(string literal)
    {
        Assert.False(OutboundEndpoint.IsPublicAddress(IPAddress.Parse(literal)));
    }

    [Theory]
    [InlineData("41.226.11.5")]  // a Tunisian public range
    [InlineData("8.8.8.8")]
    [InlineData("2001:4860:4860::8888")]
    public void A_Public_Address_Is_Allowed(string literal)
    {
        Assert.True(OutboundEndpoint.IsPublicAddress(IPAddress.Parse(literal)));
    }

    /// <summary>
    /// ⚠️ <b>The load-bearing case.</b> A private target reaching the socket must be refused there, because
    /// save-time validation structurally cannot see it: DNS is mutable, and a name that resolved publicly when
    /// the admin pressed « Enregistrer » can resolve to the metadata address an hour later when the job dials
    /// it. If this ever passes, the rebind is open again whatever the settings screen says.
    /// </summary>
    [Fact]
    public async Task The_Guard_Refuses_A_Private_Target_At_Connect_Time()
    {
        var connect = PublicEgressGuard.ConnectCallback(allowPrivateNetwork: false);
        Assert.NotNull(connect);

        await Assert.ThrowsAsync<HttpRequestException>(() => EnsurePublic("169.254.169.254"));
    }

    /// <summary>
    /// The refusal repeats the host <b>as the caller gave it</b> and adds nothing else — in particular not the
    /// addresses it resolved to. Reporting those back to the tenant who chose the name turns every refusal into
    /// a DNS-driven scanner of the operator's private network: « which of my names comes back internal » is
    /// precisely the question this must not answer, and the natural « … (10.0.3.7) » that a later author would
    /// add to make the message helpful is the whole leak.
    ///
    /// <para>Asserted as an <b>exact</b> string rather than as an absence, deliberately: an absence assertion
    /// cannot be written for a hostname without doing real DNS in a unit test, and with an IP literal the host
    /// and the address are the same text — so « does not contain the address » is unfalsifiable there. Equality
    /// fails the moment anything is appended, which is the property that actually matters.</para>
    /// </summary>
    [Fact]
    public async Task The_Refusal_Adds_Nothing_To_The_Host_The_Caller_Named()
    {
        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => EnsurePublic("10.11.12.13"));

        Assert.Equal(
            "Le nom « 10.11.12.13 » désigne une adresse interne : la connexion est refusée.",
            thrown.Message);
    }

    /// <summary>
    /// ⚠️ The other half, and it matters as much: on a clinic's own machine a private relay on the practice's
    /// own LAN is the <b>normal</b> arrangement. There is no tenant boundary to defend on an install serving
    /// one practice from its own reception desk, and refusing would break the working case — so the guard is
    /// absent entirely rather than merely lenient.
    /// </summary>
    [Fact]
    public async Task On_A_Clinics_Own_Machine_A_Private_Relay_Is_Left_Alone()
    {
        Assert.Null(PublicEgressGuard.ConnectCallback(allowPrivateNetwork: true));

        // And the SMTP-side check is a no-op there too, rather than throwing on the practice's own box.
        await PublicEgressGuard.EnsureHostResolvesPublicAsync("192.168.1.50", allowPrivateNetwork: true);
    }

    [Fact]
    public async Task A_Public_Target_Passes_The_Smtp_Side_Check()
    {
        await PublicEgressGuard.EnsureHostResolvesPublicAsync("41.226.11.5", allowPrivateNetwork: false);
    }

    private static Task EnsurePublic(string host) =>
        PublicEgressGuard.EnsureHostResolvesPublicAsync(host, allowPrivateNetwork: false, CancellationToken.None);
}
