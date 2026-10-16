using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Services;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Android delivery (staff-chat Part 7): the service-account key is exchanged for an access token, and the payload
/// picks the shell's channel and collapses one thread into one banner.
/// </summary>
public class FcmPushSenderTests
{
    private const string TokenUri = "https://oauth.test/token";
    private const string Endpoint = "https://fcm.test/v1/projects/{projectId}/messages:send";

    public FcmPushSenderTests() => FcmAccessToken.ClearCache();

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Calls { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> TokenResponse { get; set; } = _ =>
            Json(HttpStatusCode.OK, """{"access_token":"minted-token","expires_in":3600}""");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request, body));
            return request.RequestUri!.AbsoluteUri == TokenUri ? TokenResponse(request) : Json(HttpStatusCode.OK, "{}");
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static FcmPushSender Sender(Recorder recorder)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(recorder, disposeHandler: false));
        return new FcmPushSender(factory.Object);
    }

    private static string ServiceAccountJson()
    {
        using var rsa = RSA.Create(2048);
        return JsonSerializer.Serialize(new
        {
            type = "service_account",
            client_email = "push@apexa-test.iam.gserviceaccount.com",
            private_key = rsa.ExportPkcs8PrivateKeyPem(),
            token_uri = TokenUri
        });
    }

    private static ResolvedPushCredentials Credentials(string secret) =>
        new(DevicePlatform.Android, Endpoint, "apexa-test", secret);

    private static PushMessage Chat() =>
        new("device-token", "Nouveau message", NotificationCategory.StaffChatMessage, null, "dm:local%7Ca:local%7Cb");

    // The key itself is not a bearer: it is exchanged for one, and the send carries the minted token.
    [Fact]
    public async Task A_Service_Account_Key_Is_Exchanged_For_An_Access_Token()
    {
        var recorder = new Recorder();

        var result = await Sender(recorder).SendAsync(Chat(), Credentials(ServiceAccountJson()));

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        var (tokenRequest, tokenBody) = recorder.Calls[0];
        Assert.Equal(TokenUri, tokenRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("grant_type=urn%3Aietf%3Aparams%3Aoauth%3Agrant-type%3Ajwt-bearer", tokenBody);
        var send = recorder.Calls[1].Request;
        Assert.Equal("https://fcm.test/v1/projects/apexa-test/messages:send", send.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer minted-token", send.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public async Task The_Token_Is_Reused_Until_It_Nears_Expiry()
    {
        var recorder = new Recorder();
        var sender = Sender(recorder);
        var key = ServiceAccountJson();

        await sender.SendAsync(Chat(), Credentials(key));
        await sender.SendAsync(Chat(), Credentials(key));

        Assert.Single(recorder.Calls, c => c.Request.RequestUri!.AbsoluteUri == TokenUri);
    }

    // A chat message lands on the « Messages » channel, tagged by its thread, carrying the thread and no text.
    [Fact]
    public async Task A_Chat_Push_Uses_The_Messages_Channel_And_Its_Thread_As_Tag()
    {
        var recorder = new Recorder();

        await Sender(recorder).SendAsync(Chat(), Credentials(ServiceAccountJson()));

        using var payload = JsonDocument.Parse(recorder.Calls[1].Body);
        var message = payload.RootElement.GetProperty("message");
        Assert.Equal("Nouveau message", message.GetProperty("notification").GetProperty("title").GetString());
        Assert.False(message.GetProperty("notification").TryGetProperty("body", out _));
        Assert.Equal("dm:local%7Ca:local%7Cb", message.GetProperty("data").GetProperty("conversation").GetString());
        var android = message.GetProperty("android");
        Assert.Equal("HIGH", android.GetProperty("priority").GetString());
        Assert.Equal(FcmPushSender.MessagesChannel, android.GetProperty("notification").GetProperty("channel_id").GetString());
        Assert.Equal("dm:local%7Ca:local%7Cb", android.GetProperty("notification").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task An_Agenda_Push_Uses_The_Agenda_Channel()
    {
        var recorder = new Recorder();
        var appointmentId = Guid.NewGuid();

        await Sender(recorder).SendAsync(
            new PushMessage("device-token", "Nouveau rendez-vous", NotificationCategory.AppointmentCreated, appointmentId),
            Credentials(ServiceAccountJson()));

        using var payload = JsonDocument.Parse(recorder.Calls[1].Body);
        var android = payload.RootElement.GetProperty("message").GetProperty("android").GetProperty("notification");
        Assert.Equal(FcmPushSender.AgendaChannel, android.GetProperty("channel_id").GetString());
        Assert.Equal(appointmentId.ToString(), android.GetProperty("tag").GetString());
    }

    // A key Google refuses is the operator's to fix: parked, never retried to death.
    [Fact]
    public async Task A_Refused_Key_Parks_The_Row()
    {
        var recorder = new Recorder
        {
            TokenResponse = _ => Json(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""")
        };

        var result = await Sender(recorder).SendAsync(Chat(), Credentials(ServiceAccountJson()));

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Single(recorder.Calls);
    }

    [Fact]
    public async Task An_Unreadable_Key_Parks_The_Row_Without_Calling_Google()
    {
        var recorder = new Recorder();

        var result = await Sender(recorder).SendAsync(Chat(), Credentials("""{"client_email":"x@y.z","private_key":"not a pem"}"""));

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Empty(recorder.Calls);
    }

    // A bearer configured by hand still works as it did.
    [Fact]
    public async Task A_Plain_Secret_Is_Used_As_The_Bearer()
    {
        var recorder = new Recorder();

        await Sender(recorder).SendAsync(Chat(), Credentials("ya29.hand-made"));

        Assert.Equal("Bearer ya29.hand-made", Assert.Single(recorder.Calls).Request.Headers.GetValues("Authorization").Single());
    }
}
