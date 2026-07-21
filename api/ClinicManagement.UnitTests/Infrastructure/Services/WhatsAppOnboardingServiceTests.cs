using System.Net;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The WhatsApp Embedded-Signup onboarding service (Graph API). Verifies the code→token exchange (success,
/// missing-credential guard, non-success), and that Graph error bodies are classified into the right
/// <see cref="WhatsAppOnboardingError"/> category (already-registered / not-eligible) via the public steps.
/// No real network is touched — every request is intercepted by a stub handler.
/// </summary>
public class WhatsAppOnboardingServiceTests
{
    private static IHttpClientFactory Factory(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory.Object;
    }

    private static IConfiguration Config(bool withCredentials = true)
    {
        var values = new Dictionary<string, string?> { ["Meta:GraphApiVersion"] = "v21.0" };
        if (withCredentials)
        {
            values["Meta:AppId"] = "app-123";
            values["Meta:AppSecret"] = "secret-xyz";
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static WhatsAppOnboardingService Service(StubHandler handler, bool withCredentials = true) =>
        new(Factory(handler), Config(withCredentials), NullLogger<WhatsAppOnboardingService>.Instance);

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task ExchangeCode_Returns_Access_Token_On_Success()
    {
        Uri? uri = null;
        var service = Service(new StubHandler(req =>
        {
            uri = req.RequestUri;
            return Json(HttpStatusCode.OK, "{\"access_token\":\"biz-token\"}");
        }));

        var token = await service.ExchangeCodeForTokenAsync("the-code");

        Assert.Equal("biz-token", token);
        Assert.Contains("/v21.0/oauth/access_token", uri!.ToString());
        Assert.Contains("code=the-code", uri.ToString());
    }

    [Fact]
    public async Task ExchangeCode_Throws_CodeExchangeFailed_When_Credentials_Missing()
    {
        var called = false;
        var service = Service(new StubHandler(_ =>
        {
            called = true;
            return Json(HttpStatusCode.OK, "{}");
        }), withCredentials: false);

        var ex = await Assert.ThrowsAsync<WhatsAppOnboardingException>(
            () => service.ExchangeCodeForTokenAsync("the-code"));

        Assert.Equal(WhatsAppOnboardingError.CodeExchangeFailed, ex.Error);
        Assert.False(called); // no HTTP attempted without app credentials
    }

    [Fact]
    public async Task ExchangeCode_Throws_CodeExchangeFailed_On_NonSuccess()
    {
        var service = Service(new StubHandler(_ =>
            Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"bad code\"}}")));

        var ex = await Assert.ThrowsAsync<WhatsAppOnboardingException>(
            () => service.ExchangeCodeForTokenAsync("the-code"));

        Assert.Equal(WhatsAppOnboardingError.CodeExchangeFailed, ex.Error);
    }

    [Fact]
    public async Task RegisterPhone_Classifies_Already_Registered()
    {
        var service = Service(new StubHandler(_ =>
            Json(HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"Phone number already registered; migration required\"}}")));

        var ex = await Assert.ThrowsAsync<WhatsAppOnboardingException>(
            () => service.RegisterPhoneAsync("PN-99", "biz-token"));

        Assert.Equal(WhatsAppOnboardingError.NumberAlreadyRegistered, ex.Error);
    }

    [Fact]
    public async Task SubscribeApp_Classifies_Waba_Not_Eligible()
    {
        var service = Service(new StubHandler(_ =>
            Json(HttpStatusCode.BadRequest,
                "{\"error\":{\"message\":\"WABA is not eligible, business verification required\"}}")));

        var ex = await Assert.ThrowsAsync<WhatsAppOnboardingException>(
            () => service.SubscribeAppAsync("WABA-1", "biz-token"));

        Assert.Equal(WhatsAppOnboardingError.WabaNotEligible, ex.Error);
    }

    [Fact]
    public async Task SubscribeApp_Falls_Back_To_Step_Default_For_Unclassifiable_Error()
    {
        var service = Service(new StubHandler(_ =>
            Json(HttpStatusCode.InternalServerError, "{\"error\":{\"message\":\"transient upstream failure\"}}")));

        var ex = await Assert.ThrowsAsync<WhatsAppOnboardingException>(
            () => service.SubscribeAppAsync("WABA-1", "biz-token"));

        Assert.Equal(WhatsAppOnboardingError.WabaNotEligible, ex.Error); // subscribe step default
    }

    /// <summary>Intercepts every outbound request; no real network is touched.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
