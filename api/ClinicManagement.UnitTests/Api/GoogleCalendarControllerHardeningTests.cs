using System.Reflection;
using MediatR;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Repositories;
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
/// Hardening pass — <see cref="GoogleCalendarController"/> auth gate (§2 / AC-4/AC-5) and per-clinic OAuth
/// <c>state</c> CSRF + clinic binding (feature cloud-security-and-tenant-isolation, #4).
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

    private static GoogleCalendarController Controller(IMemoryCache cache, Guid? clinicId = null)
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(clinicId ?? Guid.NewGuid()));

        var controller = new GoogleCalendarController(
            new Mock<IGoogleCalendarSyncService>().Object,
            Config(),
            new Mock<IClinicRepository>().Object,
            resolver.Object,
            new Mock<IUnitOfWork>().Object,
            cache,
            new Mock<IMediator>().Object,
            new Mock<IGoogleTokenProtector>().Object,
            new Mock<IClinicContext>().Object,
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
    [InlineData(nameof(GoogleCalendarController.GetSyncStatus))]
    [InlineData(nameof(GoogleCalendarController.SyncAppointmentToGoogle))]
    [InlineData(nameof(GoogleCalendarController.Connect))]
    [InlineData(nameof(GoogleCalendarController.Disconnect))]
    public void Ajax_Endpoints_Are_Not_Anonymous(string methodName) // [AC-4]
    {
        var method = typeof(GoogleCalendarController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.Null(method!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(GoogleCalendarController.SyncAppointmentToGoogle))]
    [InlineData(nameof(GoogleCalendarController.Connect))]
    // AC-P2.34: the disconnect joins the same class — it revokes the clinic's whole Google connection.
    [InlineData(nameof(GoogleCalendarController.Disconnect))]
    public void Mutating_Google_Endpoints_Are_Admin_Only(string methodName) // [#4 admin-gate]
    {
        var method = typeof(GoogleCalendarController).GetMethod(methodName);
        Assert.NotNull(method);
        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal("AdminOnly", authorize!.Policy);
    }

    [Theory]
    [InlineData(nameof(GoogleCalendarController.Callback))]
    public void OAuth_Redirect_Endpoints_Remain_Anonymous(string methodName) // [AC-4]
    {
        var method = typeof(GoogleCalendarController).GetMethod(methodName);
        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ---- OAuth state (CSRF) + per-clinic binding ----------------------------

    [Fact]
    public async Task Connect_Issues_And_Stores_A_Clinic_Bound_State()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache, Guid.NewGuid());

        var result = await controller.Connect();

        var ok = Assert.IsType<OkObjectResult>(result);
        var authUrl = ok.Value!.GetType().GetProperty("authUrl")!.GetValue(ok.Value) as string;
        Assert.NotNull(authUrl);
        Assert.Contains("state=", authUrl!);
        // Exactly one short-lived state entry was persisted server-side, bound to the caller's clinic.
        Assert.Equal(1, cache.Count);
        // The same state is dropped into an HttpOnly companion cookie (double-submit binding).
        var setCookie = controller.HttpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("google_oauth_state=", setCookie);
        Assert.Contains("httponly", setCookie.ToLowerInvariant());
    }

    [Fact]
    public async Task Callback_Rejects_Missing_State()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);

        var result = await controller.Callback(code: "auth-code", error: null, state: null);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_Rejects_Unrecognized_State()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);

        var result = await controller.Callback(code: "auth-code", error: null, state: "never-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // A state present server-side but NOT accompanied by the matching companion cookie is rejected
    // (login-CSRF binding — an attacker-minted state has no cookie).
    [Fact]
    public async Task Callback_Rejects_State_Without_Matching_Cookie()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);
        cache.Set("google_oauth_state:" + "server-issued", Guid.NewGuid());

        var result = await controller.Callback(code: "auth-code", error: null, state: "server-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Callback_Rejects_State_Cookie_Mismatch()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var controller = Controller(cache);
        cache.Set("google_oauth_state:" + "server-issued", Guid.NewGuid());
        controller.HttpContext.Request.Headers.Cookie = "google_oauth_state=different-value";

        var result = await controller.Callback(code: "auth-code", error: null, state: "server-issued");

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
