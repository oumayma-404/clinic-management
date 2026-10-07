using System.Text.Json;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Features.Relay;
using ClinicManagement.Infrastructure.Deployment;
using Microsoft.AspNetCore.Http;
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

    private sealed record Outcome(int Status, string Body, bool ReachedNext);

    private static async Task<Outcome> InvokeAsync(
        DeploymentKind kind = DeploymentKind.ClinicRelay,
        string path = WritePath,
        string method = "POST",
        bool allowed = false,
        bool routed = true,
        bool retired = false,
        string? role = null)
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
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                allowed
                    ? new EndpointMetadataCollection(new AllowedOnStandbyRelayAttribute("test"))
                    : EndpointMetadataCollection.Empty,
                allowed ? "allowed" : "routed"));
        }

        var body = new MemoryStream();
        context.Response.Body = body;

        var reachedNext = false;
        var middleware = new RelayLeaseGateMiddleware(_ =>
        {
            reachedNext = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, DeploymentProfile.For(kind), new FixedRelayLocalStatus(retired));

        body.Position = 0;
        return new Outcome(context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reachedNext);
    }

    private sealed class FixedRelayLocalStatus(bool retired) : ClinicManagement.Application.Common.Interfaces.IRelayLocalStatus
    {
        public bool IsRetired => retired;
        public DateTime? RetiredAtUtc => retired ? new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc) : null;
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
    }
}
