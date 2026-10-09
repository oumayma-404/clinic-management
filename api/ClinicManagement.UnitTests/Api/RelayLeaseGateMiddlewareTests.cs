using System.Text.Json;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The PC de secours's standby gate (<c>clinic-pc-copy</c> Part 1): a copy refuses staff writes with 423
/// <c>relay_standby</c>, and nothing else is refused. Run over the <b>real</b> profile of each kind, so the cloud and the
/// LAN install are proven untouched rather than assumed.
/// </summary>
public class RelayLeaseGateMiddlewareTests
{
    private const string WritePath = "/api/patients";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed record Outcome(int Status, string Body, bool ReachedNext, int RelayReads);

    private static async Task<Outcome> InvokeAsync(
        DeploymentKind kind = DeploymentKind.ClinicRelay,
        string path = WritePath,
        string method = "POST",
        bool allowed = false,
        bool routed = true,
        bool retired = false,
        string? role = null,
        Guid? clinic = null,
        ClinicRelay? relay = null,
        bool allowedWhileFenced = false,
        bool holding = false,
        bool onlineOnly = false)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = method;
        if (role is not null)
        {
            context.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(new[] { new System.Security.Claims.Claim("sub", "local|x") }, "test"));
            context.Items[EffectiveRole.HttpContextItemKey] = role;
        }

        if (routed)
        {
            var metadata = new List<object>();
            if (allowed)
            {
                metadata.Add(new AllowedOnStandbyRelayAttribute("test"));
            }

            if (allowedWhileFenced)
            {
                metadata.Add(new AllowedWhileCloudFencedAttribute("test"));
            }

            if (onlineOnly)
            {
                metadata.Add(new OnlineOnlyAttribute("test"));
            }

            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(metadata), "routed"));
        }

        var scope = new TenantScope(Microsoft.Extensions.Logging.Abstractions.NullLogger<TenantScope>.Instance);
        if (clinic is { } clinicId)
        {
            scope.UseClinic(clinicId);
        }

        var relays = new Mock<IClinicRelayRepository>();
        relays.Setup(r => r.GetCurrentForClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(relay);

        var body = new MemoryStream();
        context.Response.Body = body;

        var reachedNext = false;
        var middleware = new RelayLeaseGateMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, DeploymentProfile.For(kind), new FixedRelayLocalStatus(retired, holding), scope, relays.Object);

        body.Position = 0;
        return new Outcome(context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reachedNext,
            relays.Invocations.Count);
    }

    /// <summary>A PC that confirmed an « armé » ack sent <paramref name="ago"/> before now, and has said nothing since.</summary>
    private static ClinicRelay ArmedRelay(TimeSpan ago)
    {
        var sent = DateTime.UtcNow - ago;
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", sent.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, null, sent.AddDays(-1));
        relay.RecordAckConfirmation(relay.IssueAck(armed: true, sent), armed: true);
        return relay;
    }

    private sealed class FixedRelayLocalStatus(bool retired, bool holding = false)
        : ClinicManagement.Application.Common.Interfaces.IRelayLocalStatus
    {
        public bool IsRetired => retired;
        public DateTime? RetiredAtUtc => retired ? new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc) : null;
        public bool IsHolding => holding;
    }

    /// <summary>An armed PC that then said, on a heartbeat, that it holds the cabinet's saves since <paramref name="since"/>.</summary>
    private static ClinicRelay HoldingRelay(DateTime since)
    {
        var relay = ArmedRelay(TimeSpan.FromMinutes(10));
        relay.RecordHeartbeat(new RelayHeartbeat(10, 100, true, 0, 0, null, false, "build-1", null, null, null, null,
            Holding: true, HoldingSinceUtc: since), 10, DateTime.UtcNow);
        return relay;
    }

    // ---- the PC in charge (D13) ----------------------------------------------------------------------------------

    // [AC-3.5] Holding the cabinet's saves, the PC is the cabinet's server: every write passes.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_Holding_Pc_Accepts_The_Cabinets_Saves(string method)
    {
        var outcome = await InvokeAsync(method: method, holding: true);

        Assert.True(outcome.ReachedNext);
    }

    // [FR-5] …except the « online only » list, refused whatever the method — downloading the archive is a GET.
    [Theory]
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task A_Holding_Pc_Refuses_The_Online_Only_List(string method)
    {
        var outcome = await InvokeAsync(method: method, holding: true, onlineOnly: true);

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);
        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal("online_only", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Possible uniquement quand internet est revenu au cabinet.", json.RootElement.GetProperty("error").GetString());
    }

    // While the PC only follows, the online-only list is refused as any write is (standby) — and a read of it passes.
    [Fact]
    public async Task The_Online_Only_List_Means_Nothing_Until_The_Pc_Holds()
    {
        var write = await InvokeAsync(onlineOnly: true);
        var read = await InvokeAsync(method: "GET", onlineOnly: true);
        var cloud = await InvokeAsync(DeploymentKind.HostedMultiTenant, onlineOnly: true, holding: true);

        Assert.Contains(RelayRefusals.StandbyCode, write.Body);
        Assert.True(read.ReachedNext);
        Assert.True(cloud.ReachedNext);
    }

    // [AC-4.2] Once the PC said it holds the saves, the cloud names it, with the cabinet's own time of the takeover.
    [Fact]
    public async Task The_Cloud_Says_The_Cabinet_Works_On_The_Pc_Since_When()
    {
        var since = DateTime.UtcNow.AddMinutes(-5);
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId, relay: HoldingRelay(since));

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);
        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal("clinic_on_relay", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(RelayRefusals.OnRelay(RelayRefusals.SinceClinicTime(since, DateTime.UtcNow)),
            json.RootElement.GetProperty("error").GetString());
    }

    // FR-11 doors still pass while the PC holds the saves.
    [Fact]
    public async Task A_Door_Marked_For_The_Cut_Passes_While_The_Pc_Holds()
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId,
            relay: HoldingRelay(DateTime.UtcNow.AddMinutes(-5)), allowedWhileFenced: true);

        Assert.True(outcome.ReachedNext);
    }

    // [AC-8.1] A retired PC opens for administrators only: a colleague still signed in is signed out — reads included —
    // with the reason, while an administrator keeps the read-only copy.
    [Theory]
    [InlineData("secretary", "GET")]
    [InlineData("doctor", "POST")]
    public async Task A_Retired_Pc_Signs_Out_Everyone_But_The_Admins(string role, string method)
    {
        var outcome = await InvokeAsync(method: method, retired: true, role: role);

        Assert.Equal(StatusCodes.Status401Unauthorized, outcome.Status);
        Assert.Contains("relay_retired_admins_only", outcome.Body);
        Assert.False(outcome.ReachedNext);
    }

    // A retired PC's save refusal says it is retired — never « only during a cut », which would promise a save.
    [Fact]
    public async Task A_Save_On_A_Retired_Pc_Says_It_Is_Retired()
    {
        var outcome = await InvokeAsync(retired: true, role: "admin");

        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);
        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal(RelayRefusals.RetiredCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(RelayRefusals.RetiredReadOnly, json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task An_Admin_Still_Reads_A_Retired_Pc_And_A_Following_Pc_Is_Untouched()
    {
        var admin = await InvokeAsync(method: "GET", retired: true, role: "admin");
        Assert.True(admin.ReachedNext);

        var stillFollowing = await InvokeAsync(method: "GET", retired: false, role: "secretary");
        Assert.True(stillFollowing.ReachedNext);

        // Off a PC de secours the rule does not exist, whatever the status says.
        var cloud = await InvokeAsync(kind: DeploymentKind.HostedMultiTenant, method: "GET", retired: true, role: "secretary");
        Assert.True(cloud.ReachedNext);
    }

    // [Part 1] A staff write on the copy is refused with the sentence and the code the client branches on.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_Write_On_A_Standby_Relay_Is_Refused_With_423(string method)
    {
        var outcome = await InvokeAsync(method: method);

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);

        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal(RelayRefusals.StandbyCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(RelayRefusals.Standby, json.RootElement.GetProperty("error").GetString());
    }

    // Reads are untouched by construction: the copy exists to be read.
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task A_Read_On_A_Standby_Relay_Passes(string method)
    {
        var outcome = await InvokeAsync(method: method);

        Assert.True(outcome.ReachedNext);
    }

    // The sign-in doors carry the attribute and pass.
    [Fact]
    public async Task A_Write_Marked_Allowed_On_Standby_Passes()
    {
        var outcome = await InvokeAsync(allowed: true);

        Assert.True(outcome.ReachedNext);
    }

    // The two other kinds are never refused — the gate asks the capability, not a configuration value.
    [Theory]
    [InlineData(DeploymentKind.HostedMultiTenant)]
    [InlineData(DeploymentKind.SelfHostedLan)]
    public async Task A_Deployment_That_Is_Not_A_Relay_Is_Never_Refused(DeploymentKind kind)
    {
        var outcome = await InvokeAsync(kind: kind);

        Assert.True(outcome.ReachedNext);
    }

    // Outside /api the front door serves the web app; refusing it would 423 the page that explains the refusal.
    [Fact]
    public async Task A_Path_Outside_The_Api_Passes()
    {
        var outcome = await InvokeAsync(path: "/patients");

        Assert.True(outcome.ReachedNext);
    }

    // An unroutable path is routing's 404, not « this is a copy ».
    [Fact]
    public async Task An_Unroutable_Api_Path_Is_Not_Refused()
    {
        var outcome = await InvokeAsync(routed: false);

        Assert.True(outcome.ReachedNext);
    }

    // Its position is the half no behavioural test can see: after token-state enforcement (401/403 win), before the
    // subscription gate (a copy says « copy », not « pay »), and before the controllers.
    [Fact]
    public void The_Gate_Runs_After_Token_State_And_Before_The_Subscription_Gate()
    {
        var program = File.ReadAllText(Path.Combine(
            ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName,
            "ClinicManagement.API",
            "Program.cs"));

        var tokenState = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.LocalAuthEnforcementMiddleware>", StringComparison.Ordinal);
        var relay = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.RelayLeaseGateMiddleware>", StringComparison.Ordinal);
        var subscription = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.SubscriptionGateMiddleware>", StringComparison.Ordinal);

        Assert.True(relay > 0, "RelayLeaseGateMiddleware is no longer registered in Program.cs.");
        Assert.True(tokenState > 0 && tokenState < relay,
            "The relay gate must run AFTER LocalAuthEnforcementMiddleware, or a 423 masks a revoked token's 401.");
        Assert.True(relay < subscription,
            "The relay gate must run BEFORE the subscription gate, or a copy of an expired cabinet is told to pay.");

        var tenantScope = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.TenantScopeMiddleware>", StringComparison.Ordinal);
        Assert.True(tenantScope > 0 && tenantScope < relay,
            "The relay gate must run AFTER TenantScopeMiddleware: the cloud's fence is decided per cabinet.");
    }

    // ---- the cloud's fence (D13, D15) ------------------------------------------------------------------------------

    // [AC-6.3] A cabinet whose armed PC has been silent past 60 s: its writes wait, with the sentence and the code.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task A_Cabinet_Write_Is_Refused_While_Its_Armed_Pc_Is_Silent(string method)
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, method: method, clinic: ClinicId,
            relay: ArmedRelay(TimeSpan.FromSeconds(75)));

        Assert.False(outcome.ReachedNext);
        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);
        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal(RelayRefusals.SilentCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(RelayRefusals.Silent, json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_Cabinet_Whose_Pc_Answered_Recently_Writes_As_Usual()
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId,
            relay: ArmedRelay(TimeSpan.FromSeconds(30)));

        Assert.True(outcome.ReachedNext);
    }

    // FR-11: signing in, an admin's account changes and the PC de secours's own management stay open.
    [Fact]
    public async Task A_Door_Marked_For_The_Cut_Passes_The_Fence()
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId,
            relay: ArmedRelay(TimeSpan.FromMinutes(10)), allowedWhileFenced: true);

        Assert.True(outcome.ReachedNext);
    }

    // The cloud stays readable during a cut (US-4), and a read costs no lookup at all.
    [Fact]
    public async Task A_Read_Is_Never_Fenced_And_Reads_No_Relay()
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, method: "GET", clinic: ClinicId,
            relay: ArmedRelay(TimeSpan.FromMinutes(10)));

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RelayReads);
    }

    // A caller that is not a cabinet — the PC's own token, the vendor's console — has no PC to wait for.
    [Fact]
    public async Task A_Caller_That_Is_Not_A_Cabinet_Passes_And_Reads_No_Relay()
    {
        var outcome = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: null,
            relay: ArmedRelay(TimeSpan.FromMinutes(10)));

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RelayReads);
    }

    [Fact]
    public async Task A_Cabinet_With_No_Pc_Or_A_Retired_One_Writes_As_Usual()
    {
        var none = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId, relay: null);
        var retired = ArmedRelay(TimeSpan.FromMinutes(10));
        retired.Retire(ClinicManagement.Domain.Enums.ClinicRelayRetirement.Retired, "local|admin", DateTime.UtcNow);
        var afterRetire = await InvokeAsync(DeploymentKind.HostedMultiTenant, clinic: ClinicId, relay: retired);

        Assert.True(none.ReachedNext);
        Assert.Equal(1, none.RelayReads);
        Assert.True(afterRetire.ReachedNext);
    }

    // The fence is the cloud's: a LAN server has no PC de secours, and the PC's own refusal is relay_standby.
    [Theory]
    [InlineData(DeploymentKind.SelfHostedLan)]
    [InlineData(DeploymentKind.ClinicRelay)]
    public async Task Only_The_Cloud_Fences(DeploymentKind kind)
    {
        var outcome = await InvokeAsync(kind, clinic: ClinicId, relay: ArmedRelay(TimeSpan.FromMinutes(10)), allowed: true);

        Assert.True(outcome.ReachedNext);
        Assert.Equal(0, outcome.RelayReads);
    }
}
