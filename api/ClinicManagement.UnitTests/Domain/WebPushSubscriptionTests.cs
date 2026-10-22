using ClinicManagement.Domain.Common;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Which Web Push subscriptions the server will ever POST to (staff-chat Part 8). The endpoint is the client's to
/// choose, so this is what stops a signed-in account aiming the dispatcher at the server's own network.
/// </summary>
public class WebPushSubscriptionTests
{
    // A real-shaped uncompressed P-256 point and a 16-byte secret.
    private static readonly string P256dh = WebPushSubscription.ToBase64Url(new byte[] { 0x04 }.Concat(Enumerable.Repeat((byte)7, 64)).ToArray());
    private static readonly string Auth = WebPushSubscription.ToBase64Url(Enumerable.Repeat((byte)9, 16).ToArray());

    [Theory]
    [InlineData("https://web.push.apple.com/QGuQyavXutnMH7YKi3QrDYnOiBh0W7bpMzTRXm9h2Lbm")]
    [InlineData("https://fcm.googleapis.com/fcm/send/dKz3X:APA91bH")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAAABl")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=BQYAAAB")]
    public void The_Browsers_Push_Services_Are_Accepted(string endpoint)
    {
        Assert.Null(WebPushSubscription.Refusal(endpoint, P256dh, Auth));
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/fcm/send/x")]          // not https
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/x")]    // not the push service's port
    [InlineData("https://10.0.0.5/push")]                          // the server's own network
    [InlineData("https://localhost/push")]
    [InlineData("https://minio:9000/bucket")]
    [InlineData("https://fcm.googleapis.com.evil.example/x")]     // a lookalike suffix
    [InlineData("https://evilpush.apple.com.example/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/x")]
    [InlineData("pas une adresse")]
    public void Anything_Else_Is_Refused(string endpoint)
    {
        Assert.NotNull(WebPushSubscription.Refusal(endpoint, P256dh, Auth));
    }

    [Fact]
    public void A_Subscription_Without_Usable_Keys_Is_Refused()
    {
        const string endpoint = "https://web.push.apple.com/abc";

        Assert.NotNull(WebPushSubscription.Refusal(endpoint, null, Auth));
        Assert.NotNull(WebPushSubscription.Refusal(endpoint, P256dh, null));
        Assert.NotNull(WebPushSubscription.Refusal(endpoint, "pas-une-cle", Auth));
        Assert.NotNull(WebPushSubscription.Refusal(endpoint, WebPushSubscription.ToBase64Url(new byte[65]), Auth)); // not 0x04-prefixed
        Assert.NotNull(WebPushSubscription.Refusal(endpoint, P256dh, WebPushSubscription.ToBase64Url(new byte[8])));
    }
}
