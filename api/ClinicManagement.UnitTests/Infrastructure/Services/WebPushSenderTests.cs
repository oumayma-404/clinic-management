using System.Net;
using System.Security.Cryptography;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Services;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Web Push delivery (staff-chat Part 8): where it may send, what it sends, and what each answer means.
/// </summary>
public class WebPushSenderTests
{
    private const string AppleEndpoint = "https://web.push.apple.com/QGuQyavXutnMH7YKi3QrDYnOiBh0W7bpMzTRXm9h2LbmIjfULFfLZvqc";

    private sealed class Recorder : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Calls { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request, request.Content == null ? Array.Empty<byte>() : await request.Content.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }

    private static WebPushSender Sender(Recorder recorder)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(recorder, disposeHandler: false));
        return new WebPushSender(factory.Object);
    }

    private static ResolvedPushCredentials Credentials()
    {
        var (publicKey, privateKey) = WebPushCrypto.NewVapidKeys();
        return new ResolvedPushCredentials(DevicePlatform.WebPush, "mailto:contact@apexa.tn", publicKey, privateKey);
    }

    private static (string P256dh, string Auth) BrowserKeys()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(false);
        var point = new byte[] { 0x04 }.Concat(p.Q.X!).Concat(p.Q.Y!).ToArray();
        return (WebPushSubscription.ToBase64Url(point), WebPushSubscription.ToBase64Url(RandomNumberGenerator.GetBytes(16)));
    }

    private static PushMessage Chat(string endpoint = AppleEndpoint)
    {
        var (p256dh, auth) = BrowserKeys();
        return new PushMessage(endpoint, "Nouveau message", NotificationCategory.StaffChatMessage, null, "dm:local%7Ca:local%7Cb", p256dh, auth);
    }

    [Fact]
    public async Task A_Push_Is_Signed_Encrypted_And_Sent_To_The_Subscription()
    {
        var recorder = new Recorder();

        var result = await Sender(recorder).SendAsync(Chat(), Credentials());

        Assert.Equal(PushSendOutcome.Sent, result.Outcome);
        var (request, body) = Assert.Single(recorder.Calls);
        Assert.Equal(AppleEndpoint, request.RequestUri!.AbsoluteUri);
        Assert.StartsWith("vapid t=", request.Headers.GetValues("Authorization").Single());
        Assert.Equal("aes128gcm", request.Content!.Headers.ContentEncoding.Single());
        Assert.Equal("high", request.Headers.GetValues("Urgency").Single());
        Assert.Equal(32, request.Headers.GetValues("Topic").Single().Length);
        // The push service relays bytes it cannot read: neither the label nor the thread is in clear.
        Assert.DoesNotContain("Nouveau message", System.Text.Encoding.UTF8.GetString(body));
        Assert.DoesNotContain("dm:local", System.Text.Encoding.UTF8.GetString(body));
    }

    [Fact]
    public void The_Payload_Carries_The_Label_And_The_Routing_Only()
    {
        var payload = System.Text.Json.JsonDocument.Parse(WebPushSender.Payload(Chat())).RootElement;

        Assert.Equal("Nouveau message", payload.GetProperty("title").GetString());
        Assert.Equal("StaffChatMessage", payload.GetProperty("category").GetString());
        Assert.Equal("dm:local%7Ca:local%7Cb", payload.GetProperty("conversation").GetString());
        Assert.False(payload.TryGetProperty("appointmentId", out _));
    }

    // A subscription the browser has dropped: terminal for the device, so the dispatcher deactivates it.
    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task An_Expired_Subscription_Is_Reported_As_A_Dead_Token(HttpStatusCode status)
    {
        var recorder = new Recorder { Status = status };

        var result = await Sender(recorder).SendAsync(Chat(), Credentials());

        Assert.Equal(PushSendOutcome.TokenInvalid, result.Outcome);
    }

    [Fact]
    public async Task A_Rejected_Vapid_Key_Parks_The_Row()
    {
        var recorder = new Recorder { Status = HttpStatusCode.Forbidden };

        var result = await Sender(recorder).SendAsync(Chat(), Credentials());

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
    }

    [Fact]
    public async Task A_Busy_Push_Service_Is_Retried()
    {
        var recorder = new Recorder { Status = HttpStatusCode.TooManyRequests };

        var result = await Sender(recorder).SendAsync(Chat(), Credentials());

        Assert.Equal(PushSendOutcome.TransientFailure, result.Outcome);
    }

    // The allow-list is re-checked where the connection is opened, not only at registration.
    [Theory]
    [InlineData("https://10.0.0.5/push")]
    [InlineData("https://evil.example.com/push")]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]
    public async Task An_Endpoint_Off_The_Push_Services_Is_Never_Called(string endpoint)
    {
        var recorder = new Recorder();

        var result = await Sender(recorder).SendAsync(Chat(endpoint), Credentials());

        Assert.Equal(PushSendOutcome.TokenInvalid, result.Outcome);
        Assert.Empty(recorder.Calls);
    }

    [Fact]
    public async Task Unusable_Vapid_Keys_Park_The_Row_Without_A_Call()
    {
        var recorder = new Recorder();

        var result = await Sender(recorder).SendAsync(
            Chat(), new ResolvedPushCredentials(DevicePlatform.WebPush, "mailto:x@y.z", "pas-une-cle", "pas-une-cle"));

        Assert.Equal(PushSendOutcome.NotConfigured, result.Outcome);
        Assert.Empty(recorder.Calls);
    }
}
