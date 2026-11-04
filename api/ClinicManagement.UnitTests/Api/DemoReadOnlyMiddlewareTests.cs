using System.Text.Json;
using ClinicManagement.API.Middleware;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Demo;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The demo's read-only gate (<c>demo-cabinet</c> FR-2). Driven through a real <see cref="DefaultHttpContext"/> with
/// fabricated endpoint metadata, because what can go wrong — the method rule, the path scope, the two attributes and
/// the response shape — is invisible in an interface. Half the cases assert what it must NOT refuse: on any other
/// deployment, and for an ordinary read, a wrong verdict would break the product rather than the demo.
/// </summary>
public class DemoReadOnlyMiddlewareTests
{
    private sealed record Outcome(bool PassedThrough, int StatusCode, string Body);

    private static async Task<Outcome> InvokeAsync(
        string method = "POST",
        string path = "/api/patients",
        bool refusesWrites = true,
        object? attribute = null,
        bool routed = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        if (routed)
        {
            var metadata = attribute is null
                ? EndpointMetadataCollection.Empty
                : new EndpointMetadataCollection(attribute);
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "routed"));
        }

        var demo = new Mock<IDemoPolicy>();
        demo.SetupGet(d => d.RefusesWrites).Returns(refusesWrites);

        var passed = false;
        var middleware = new DemoReadOnlyMiddleware(_ =>
        {
            passed = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context, demo.Object);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return new Outcome(passed, context.Response.StatusCode, body);
    }

    [Theory] // [FR-2] every write is refused, whatever its verb
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task A_Write_Is_Refused_With_The_Demo_Answer(string method)
    {
        var outcome = await InvokeAsync(method);

        Assert.False(outcome.PassedThrough);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);

        using var json = JsonDocument.Parse(outcome.Body);
        Assert.Equal(DemoRefusals.ReadOnlyCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(DemoRefusals.ReadOnlyMessage, json.RootElement.GetProperty("error").GetString());
    }

    [Fact] // [FR-2] entering, staying, leaving and printing stay allowed
    public async Task A_Write_Marked_Allowed_Passes()
    {
        var outcome = await InvokeAsync(attribute: new AllowedInDemoAttribute("test reason"));

        Assert.True(outcome.PassedThrough);
    }

    [Theory] // [AC-3.4] opening every screen and form is a read
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task An_Ordinary_Read_Passes(string method)
    {
        var outcome = await InvokeAsync(method);

        Assert.True(outcome.PassedThrough);
    }

    [Fact] // [FR-2] a read that hands data out of the demo is refused too
    public async Task A_Read_Marked_Refused_Is_Refused()
    {
        var outcome = await InvokeAsync("GET", attribute: new RefusedInDemoAttribute("test reason"));

        Assert.False(outcome.PassedThrough);
        Assert.Equal(StatusCodes.Status403Forbidden, outcome.StatusCode);
    }

    [Fact] // the refusal attribute concerns reads; a write is refused regardless and never let through by it
    public async Task A_Write_Marked_Refused_Is_Still_Refused()
    {
        var outcome = await InvokeAsync("POST", attribute: new RefusedInDemoAttribute("test reason"));

        Assert.False(outcome.PassedThrough);
    }

    [Theory] // production, the LAN install and the PC de secours never see the gate
    [InlineData("POST")]
    [InlineData("GET")]
    public async Task Another_Deployment_Is_Untouched(string method)
    {
        var outcome = await InvokeAsync(
            method, refusesWrites: false, attribute: new RefusedInDemoAttribute("test reason"));

        Assert.True(outcome.PassedThrough);
    }

    [Fact] // an unroutable path is routing's 404, never a demo refusal
    public async Task An_Unroutable_Path_Passes()
    {
        var outcome = await InvokeAsync(path: "/api/nothing-here", routed: false);

        Assert.True(outcome.PassedThrough);
    }

    [Fact] // the realtime hub and the web's own routes are not the API
    public async Task A_Path_Outside_The_Api_Passes()
    {
        var outcome = await InvokeAsync(path: "/hub/clinic/negotiate");

        Assert.True(outcome.PassedThrough);
    }

    /// <summary>
    /// [FR-2] Registered before the token checks, a revoked token would read « démonstration » instead of 401; after
    /// the idempotency middleware, a refused save would still claim its key. Only the position can be wrong.
    /// </summary>
    [Fact]
    public void The_Gate_Runs_After_Token_State_And_Before_Idempotency()
    {
        var program = File.ReadAllText(Path.Combine(
            ClinicManagement.UnitTests.Common.SolutionSources.Root().FullName,
            "ClinicManagement.API",
            "Program.cs"));

        var tokenState = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.LocalAuthEnforcementMiddleware>", StringComparison.Ordinal);
        var gate = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.DemoReadOnlyMiddleware>", StringComparison.Ordinal);
        var idempotency = program.IndexOf(
            "UseMiddleware<ClinicManagement.API.Middleware.IdempotencyMiddleware>", StringComparison.Ordinal);

        Assert.True(gate > 0, "DemoReadOnlyMiddleware is no longer registered in Program.cs.");
        Assert.True(tokenState > 0 && tokenState < gate, "The demo gate must run after LocalAuthEnforcementMiddleware.");
        Assert.True(idempotency > gate, "The demo gate must run before IdempotencyMiddleware.");
    }
}
