using System.Security.Claims;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// `pointage-obligatoire`: a tracked secretary's saves wait for her arrival (423 <c>clock_in_required</c>). Most cases
/// assert what the gate must NOT refuse — it sits in front of every write — and that a read, an admin or an exempt
/// endpoint does not even look the lock up.
/// </summary>
public class AttendanceLockMiddlewareTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime FridayNoonUtc = AttendanceRules.ParseLocalTime(new DateOnly(2026, 11, 6), "12:00")!.Value;
    // Open every day for the whole day; the gate is judged at a pinned noon, never at the suite's own clock.
    private static readonly string AllDay = "[" + string.Join(",",
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(d => $"{{\"Day\":\"{d}\",\"Enabled\":true,\"From\":\"00:00\",\"To\":\"23:59\"}}")) + "]";

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly User _ines = User.CreateLocalUser(ClinicId, User.RoleSecretary, "ines@cabinet.tn", "HASH", "Ines Gharbi");
    private readonly User _admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "salma@cabinet.tn", "HASH", "Dr Salma");

    public AttendanceLockMiddlewareTests()
    {
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAttendanceSwitches(true, true, AllDay));
        _users.Setup(r => r.GetByAuth0SubAsync(_ines.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ines);
        _users.Setup(r => r.GetByAuth0SubAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_admin);
    }

    private sealed record Outcome(int Status, string Body, bool ReachedNext);

    private async Task<Outcome> InvokeAsync(
        User? caller, string method = "POST", string path = "/api/patients", bool routed = true, bool exempt = false,
        TenantScopeKind scope = TenantScopeKind.Clinic)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        if (caller is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, caller.Id) }, "test"));
        if (routed)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                exempt ? new EndpointMetadataCollection(new AllowsBeforeClockInAttribute("A test exemption long enough.")) : EndpointMetadataCollection.Empty,
                "routed"));
        var body = new MemoryStream();
        context.Response.Body = body;

        var tenant = new Mock<ITenantScope>();
        tenant.SetupGet(s => s.Kind).Returns(scope);
        tenant.SetupGet(s => s.ClinicId).Returns(scope == TenantScopeKind.Clinic ? ClinicId : null);
        var terminals = new Mock<IAttendanceTerminalRepository>();
        terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new LiveBornes(true, null));
        var evaluator = new AttendanceLockEvaluator(
            _clinics.Object, _entries.Object, terminals.Object, new Mock<IStaffLeaveRequestRepository>().Object,
            new Mock<IRelayLocalStatus>().Object, new Mock<IClinicRelayRepository>().Object, new Mock<ISubscriptionPolicy>().Object,
            new Mock<IClinicSubscriptionRepository>().Object);

        var reached = false;
        await new AttendanceLockMiddleware(_ => { reached = true; return Task.CompletedTask; })
            .InvokeAtAsync(context, tenant.Object, _users.Object, evaluator, FridayNoonUtc);

        body.Position = 0;
        return new Outcome(context.Response.StatusCode, await new StreamReader(body).ReadToEndAsync(), reached);
    }

    private void VerifyLockNeverLookedUp() =>
        _clinics.Verify(r => r.GetAttendanceSwitchesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task A_Write_Before_Her_Arrival_Is_Refused_423_With_The_Code_And_The_Sentence()
    {
        var outcome = await InvokeAsync(_ines);

        Assert.Equal(StatusCodes.Status423Locked, outcome.Status);
        Assert.False(outcome.ReachedNext);
        Assert.Contains($"\"code\":\"{AttendanceLockRules.ClockInRequiredCode}\"", outcome.Body);
        Assert.Contains("Pointez votre arriv", outcome.Body);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task A_Read_Passes_And_The_Lock_Is_Not_Even_Looked_Up(string method)
    {
        var outcome = await InvokeAsync(_ines, method);

        Assert.True(outcome.ReachedNext);
        VerifyLockNeverLookedUp();
        _users.Verify(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Endpoint_Declared_Open_Before_Her_Arrival_Passes()
    {
        var outcome = await InvokeAsync(_ines, exempt: true);

        Assert.True(outcome.ReachedNext);
        VerifyLockNeverLookedUp();
    }

    [Fact]
    public async Task An_Unroutable_Api_Path_Reaches_Routings_Own_404()
    {
        Assert.True((await InvokeAsync(_ines, routed: false)).ReachedNext);
        VerifyLockNeverLookedUp();
    }

    [Fact]
    public async Task A_Path_Outside_The_Api_Is_Never_Refused()
    {
        Assert.True((await InvokeAsync(_ines, path: "/hub/clinic")).ReachedNext);
    }

    [Fact]
    public async Task An_Admins_Save_Passes_And_Costs_No_Lock_Query()
    {
        _admin.SetAttendanceTracked(true);

        Assert.True((await InvokeAsync(_admin)).ReachedNext);
        VerifyLockNeverLookedUp();
    }

    [Fact]
    public async Task An_Anonymous_Or_Unscoped_Request_Passes()
    {
        Assert.True((await InvokeAsync(caller: null)).ReachedNext);
        Assert.True((await InvokeAsync(_ines, scope: TenantScopeKind.Unset)).ReachedNext);
        VerifyLockNeverLookedUp();
    }

    [Fact]
    public async Task Once_She_Has_Arrived_Her_Saves_Pass()
    {
        _entries.Setup(r => r.HasLineOnAsync(ClinicId, _ines.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.True((await InvokeAsync(_ines)).ReachedNext);
    }

    [Fact]
    public void The_Lock_Runs_After_The_Subscription_Gate_And_Before_The_Controllers()
    {
        var program = File.ReadAllText(Path.Combine(
            ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName, "ClinicManagement.API", "Program.cs"));

        var subscription = program.IndexOf("UseMiddleware<ClinicManagement.API.Middleware.SubscriptionGateMiddleware>", StringComparison.Ordinal);
        var lockGate = program.IndexOf("UseMiddleware<ClinicManagement.API.Middleware.AttendanceLockMiddleware>", StringComparison.Ordinal);
        var controllers = program.IndexOf("app.MapControllers();", StringComparison.Ordinal);

        Assert.True(lockGate > 0, "AttendanceLockMiddleware is no longer registered in Program.cs.");
        Assert.True(subscription < lockGate, "The lock must run after the subscription gate, so a lapsed cabinet hears « pay » first.");
        Assert.True(lockGate < controllers, "The lock must run before MapControllers, or a refused save reaches its handler.");
    }
}
