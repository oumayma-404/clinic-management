using System.Net;
using System.Text.Json;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The reminder channel senders (spec AC-7): SMS via the configured HTTP gateway with the alphanumeric
/// sender id, WhatsApp via the Business API using the pre-approved utility template (single body parameter,
/// never free-text). A channel with missing credentials reports NotConfigured; a non-2xx is transient.
/// </summary>
public class ReminderChannelSenderTests
{
    private static IHttpClientFactory Factory(StubHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));
        return factory.Object;
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ---- SMS ----------------------------------------------------------------

    [Fact]
    public async Task Sms_Is_NotConfigured_When_Gateway_Is_Missing()
    {
        var sender = new HttpSmsSender(
            Factory(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            Config(new Dictionary<string, string?>()),
            NullLogger<HttpSmsSender>.Instance);

        var result = await sender.SendAsync("+21620123456", "Rappel");

        Assert.Equal(ReminderSendOutcome.NotConfigured, result.Outcome);
    }

    [Fact]
    public async Task Sms_Sends_With_Configured_Sender_Id_And_Api_Key()
    {
        HttpRequestMessage? captured = null;
        string? body = null;
        var handler = new StubHandler(req =>
        {
            captured = req;
            body = ReadBody(req);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sender = new HttpSmsSender(Factory(handler), Config(new Dictionary<string, string?>
        {
            ["Reminders:Sms:ApiUrl"] = "https://sms.test/send",
            ["Reminders:Sms:SenderId"] = "MaClinique",
            ["Reminders:Sms:ApiKey"] = "secret-key",
        }), NullLogger<HttpSmsSender>.Instance);

        var result = await sender.SendAsync("+21620123456", "Rappel RDV");

        Assert.Equal(ReminderSendOutcome.Sent, result.Outcome);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("MaClinique", doc.RootElement.GetProperty("sender").GetString());
        Assert.Equal("+21620123456", doc.RootElement.GetProperty("to").GetString());
        Assert.Equal("Rappel RDV", doc.RootElement.GetProperty("message").GetString());
        Assert.Contains("Bearer secret-key", captured!.Headers.GetValues("Authorization"));
    }

    [Fact]
    public async Task Sms_Returns_Transient_On_Non_Success_Status()
    {
        var sender = new HttpSmsSender(
            Factory(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))),
            Config(new Dictionary<string, string?>
            {
                ["Reminders:Sms:ApiUrl"] = "https://sms.test/send",
                ["Reminders:Sms:SenderId"] = "MaClinique",
                ["Reminders:Sms:ApiKey"] = "secret-key",
            }),
            NullLogger<HttpSmsSender>.Instance);

        var result = await sender.SendAsync("+21620123456", "Rappel");

        Assert.Equal(ReminderSendOutcome.TransientFailure, result.Outcome);
        Assert.NotNull(result.Error);
    }

    // ---- WhatsApp -----------------------------------------------------------

    [Fact]
    public async Task WhatsApp_Is_NotConfigured_When_Api_Is_Missing()
    {
        var sender = new WhatsAppSender(
            Factory(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))),
            Config(new Dictionary<string, string?>()),
            NullLogger<WhatsAppSender>.Instance);

        var result = await sender.SendAsync("+21620123456", "Rappel");

        Assert.Equal(ReminderSendOutcome.NotConfigured, result.Outcome);
    }

    [Fact]
    public async Task WhatsApp_Sends_Approved_Template_With_A_Single_Body_Parameter()
    {
        Uri? uri = null;
        string? body = null;
        var handler = new StubHandler(req =>
        {
            uri = req.RequestUri;
            body = ReadBody(req);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var sender = new WhatsAppSender(Factory(handler), Config(new Dictionary<string, string?>
        {
            ["Reminders:WhatsApp:ApiUrl"] = "https://graph.test/v21.0",
            ["Reminders:WhatsApp:PhoneNumberId"] = "PN123",
            ["Reminders:WhatsApp:TemplateName"] = "appointment_reminder",
            ["Reminders:WhatsApp:TemplateLanguage"] = "fr",
            ["Reminders:WhatsApp:AccessToken"] = "wa-token",
        }), NullLogger<WhatsAppSender>.Instance);

        var result = await sender.SendAsync("+21620123456", "Rappel RDV le 03/01");

        Assert.Equal(ReminderSendOutcome.Sent, result.Outcome);
        Assert.Equal("https://graph.test/v21.0/PN123/messages", uri!.ToString());

        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("21620123456", root.GetProperty("to").GetString()); // E.164 without the leading '+'
        Assert.Equal("template", root.GetProperty("type").GetString());

        var template = root.GetProperty("template");
        Assert.Equal("appointment_reminder", template.GetProperty("name").GetString());
        Assert.Equal("fr", template.GetProperty("language").GetProperty("code").GetString());

        var parameters = template.GetProperty("components")[0].GetProperty("parameters");
        Assert.Equal(1, parameters.GetArrayLength()); // exactly one body parameter, carrying the reminder text
        Assert.Equal("Rappel RDV le 03/01", parameters[0].GetProperty("text").GetString());
    }

    // Reads the request body synchronously (via ReadAsStream) so the stub responder stays non-blocking-async.
    private static string ReadBody(HttpRequestMessage request)
    {
        using var reader = new StreamReader(request.Content!.ReadAsStream());
        return reader.ReadToEnd();
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
