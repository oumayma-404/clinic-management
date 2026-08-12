using System.Net;
using ClinicManagement.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The two loopback-only gates stay a property of the real TCP peer (hosted-security-hardening Part 2, FR-2.4,
/// risk R-5).
///
/// <para><b>What changed and why it needs holding.</b> Part 2 registers <c>UseForwardedHeaders</c> on the hosted
/// kinds, which <i>overwrites</i> <c>Connection.RemoteIpAddress</c> with whatever a trusted hop forwarded. Two
/// gates read that field — first-run <c>setup</c> and the Hangfire dashboard — and both must refuse an address a
/// header can claim, whatever the trusted-proxy bound happens to be. <see cref="OriginalPeer"/> is captured
/// before the substitution and <see cref="LocalRequest"/> reads the capture.</para>
///
/// <para>⚠️ <b>The load-bearing case is the last one</b>, asserted against <c>Program.cs</c>'s own source on
/// <c>SubscriptionGateMiddlewareTests</c>' precedent: capture-after-substitution compiles, passes every
/// behavioural case in this file, and silently makes both gates forgeable. Only the ordering can see it.</para>
/// </summary>
public class OriginalPeerTests
{
    [Fact]
    public void The_Capture_Records_The_Peer_As_It_Was()
    {
        var context = ContextFrom("203.0.113.7");

        OriginalPeer.Capture(context);

        Assert.Equal(IPAddress.Parse("203.0.113.7"), OriginalPeer.Of(context));
    }

    // ⚠️ First-writer-wins, not last: a second Capture() after the substitution would overwrite the truth with
    // the forwarded value, which is the whole failure this type exists to prevent.
    [Fact]
    public void A_Later_Capture_Does_Not_Overwrite_The_Original_Peer()
    {
        var context = ContextFrom("172.20.0.5");
        OriginalPeer.Capture(context);

        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        OriginalPeer.Capture(context);

        Assert.Equal(IPAddress.Parse("172.20.0.5"), OriginalPeer.Of(context));
    }

    // Where nothing captured — a hub invocation, a hand-built context, SelfHostedLan — the live peer IS the
    // original peer, so the pre-Part-2 behaviour is reproduced exactly rather than degraded to "unknown".
    [Fact]
    public void With_No_Capture_The_Live_Peer_Is_Returned()
    {
        var context = ContextFrom("127.0.0.1");

        Assert.Equal(IPAddress.Loopback, OriginalPeer.Of(context));
        Assert.True(LocalRequest.IsLoopback(context));
    }

    // The defect the capture prevents, stated as a test: the substituted address says loopback and the gate must
    // not believe it.
    [Fact]
    public void A_Substituted_Loopback_Address_Does_Not_Open_The_Loopback_Gate()
    {
        var context = ContextFrom("203.0.113.7");
        OriginalPeer.Capture(context);

        // What UseForwardedHeaders does to a request carrying `X-Forwarded-For: 127.0.0.1`.
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        Assert.False(
            LocalRequest.IsLoopback(context),
            "LocalRequest must read the captured peer, not the substituted address, or a forged "
            + "X-Forwarded-For opens first-run setup and the Hangfire dashboard.");
    }

    [Fact]
    public void A_Genuine_Loopback_Peer_Still_Opens_The_Gate_After_Capture()
    {
        var context = ContextFrom("127.0.0.1");
        OriginalPeer.Capture(context);

        Assert.True(LocalRequest.IsLoopback(context));
    }

    // Fail closed. Kestrel over TCP always populates the peer, so a null means an unexpected topology — exactly
    // where guessing "it must be local" is wrong.
    [Fact]
    public void An_Unknown_Peer_Is_Not_Local()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;
        OriginalPeer.Capture(context);

        Assert.Null(OriginalPeer.Of(context));
        Assert.False(LocalRequest.IsLoopback(context));
    }

    /// <summary>
    /// ⚠️ <b>The ordering obligation, which no behavioural test can see.</b> The capture has to be registered
    /// before <c>UseForwardedHeaders</c>; after it, the original peer is unrecoverable and both loopback gates
    /// are decided by a header. Asserted against <c>Program.cs</c> itself.
    /// </summary>
    [Fact]
    public void The_Peer_Is_Captured_Before_Forwarded_Headers_Are_Honoured()
    {
        var program = File.ReadAllText(Path.Combine(
            Common.SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        var capture = program.IndexOf("UseOriginalPeerCapture()", StringComparison.Ordinal);
        var forwarded = program.IndexOf("app.UseForwardedHeaders(", StringComparison.Ordinal);

        Assert.True(capture > 0, "UseOriginalPeerCapture() is no longer registered in Program.cs.");
        Assert.True(forwarded > 0, "UseForwardedHeaders is no longer registered in Program.cs.");
        Assert.True(
            capture < forwarded,
            "The original peer must be captured BEFORE UseForwardedHeaders substitutes RemoteIpAddress, or "
            + "LocalRequest.IsLoopback — which gates first-run setup and /hangfire — becomes decidable by a "
            + "forged X-Forwarded-For header.");
    }

    /// <summary>
    /// The forwarded-header registration is bounded by the trusted-proxy set and by the deployment kind, and
    /// both halves are asserted here because either one missing is a security change: an unbounded registration
    /// trusts any caller's header, and registering it where the front door is self-hosted would alter the one
    /// path FR-2.7 says must not change.
    /// </summary>
    [Fact]
    public void Forwarded_Headers_Are_Registered_Only_Behind_A_Bounded_Proxy_Set()
    {
        var program = File.ReadAllText(Path.Combine(
            Common.SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        var forwarded = program.IndexOf("app.UseForwardedHeaders(", StringComparison.Ordinal);
        var gate = program.LastIndexOf("if (!profile.SelfHostsFrontDoor)", forwarded, StringComparison.Ordinal);
        var boundedBy = program.LastIndexOf("TrustedProxies.FromConfiguration", forwarded, StringComparison.Ordinal);

        Assert.True(gate > 0, "UseForwardedHeaders must sit inside a !SelfHostsFrontDoor branch (FR-2.7).");
        Assert.True(
            boundedBy > gate,
            "UseForwardedHeaders must be bounded by TrustedProxies.FromConfiguration — the same parsed set the "
            + "rate limiter and the login lockout believe. Two parsers of one setting is how the header "
            + "middleware and the limiter end up trusting different hops.");
    }

    private static DefaultHttpContext ContextFrom(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }
}
