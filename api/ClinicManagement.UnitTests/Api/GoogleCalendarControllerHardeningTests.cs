using System.Reflection;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// Hardening pass — <see cref="GoogleCalendarController"/> auth gate (§2 / AC-4) and OAuth <c>state</c>
/// CSRF validation (§3 / AC-5).
/// </summary>
public class GoogleCalendarControllerHardeningTests
{
    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GoogleCalendar:ClientId"] = "test-client-id",
            ["GoogleCalendar:ClientSecret"] = "test-client-secret",
            ["GoogleCalendar:RedirectUri"] = "https://localhost:5001/api/googlecalendar/callback",
        })
        .Build();

    private static GoogleCalendarController Controller(IMemoryCache cache)
    {
        var controller = new GoogleCalendarController(
            new Mock<IGoogleCalendarSyncService>().Object,
            Config(),
            new Mock<IGoogleTokenStore>().Object,
            cache,
            NullLogger<GoogleCalendarController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    // ---- AC-4: authorization attributes -------------------------------------

    [Fact]
    public void Controller_Requires_Authorization_At_Class_Level() // [AC-4]
    {
        Assert.NotNull(typeof(GoogleCalendarController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Theory]
    [InlineData(nameof(GoogleCalendarController.SyncFromGoogleCalendar))]
    [InlineData(nameof(GoogleCalendarController.GetSyncStatus))]
    [InlineData(nameof(GoogleCalendarController.SyncAppointmentToGoogle))]
    public void Ajax_Endpoints_Are_Not_Anonymous(string methodName) // [AC-4]
    {
        var method = typeof(GoogleCalendarController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(GoogleCalendarController.Authorize))]
    [InlineData(nameof(GoogleCalendarController.Callback))]
    public void OAuth_Redirect_Endpoints_Remain_Anonymous(string methodName) // [AC-4]
    {
        var method = typeof(GoogleCalendarController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ---- AC-5: OAuth state validation ---------------------------------------

    [Fact]
    public async Task Callback_Rejects_Missing_State() // [AC-5]
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);

        var result = await controller.Callback(code: "auth-code", error: null, state: null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_Rejects_Unrecognized_State() // [AC-5]
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);

        var result = await controller.Callback(code: "auth-code", error: null, state: "never-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void Authorize_Issues_And_Stores_A_State() // [AC-5] a matching state can then be validated
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);

        var result = controller.Authorize();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Contains("state=", redirect.Url);
        // Exactly one short-lived state entry was persisted server-side for the callback to match against.
        Assert.Equal(1, cache.Count);
        // Finding 6: the same state is dropped into an HttpOnly companion cookie (double-submit binding).
        var setCookie = controller.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("google_oauth_state=", setCookie);
        Assert.Contains("httponly", setCookie.ToLowerInvariant());
    }

    // Finding 6: a state that is present server-side but is NOT accompanied by the matching companion
    // cookie is rejected — this is the login-CSRF binding (an attacker-minted state has no cookie).
    [Fact]
    public async Task Callback_Rejects_State_Without_Matching_Cookie()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);
        cache.Set("google_oauth_state:" + "server-issued", true);

        var result = await controller.Callback(code: "auth-code", error: null, state: "server-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_Rejects_State_Cookie_Mismatch()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);
        cache.Set("google_oauth_state:" + "server-issued", true);
        controller.HttpContext.Request.Headers.Cookie = "google_oauth_state=different-value";

        var result = await controller.Callback(code: "auth-code", error: null, state: "server-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
