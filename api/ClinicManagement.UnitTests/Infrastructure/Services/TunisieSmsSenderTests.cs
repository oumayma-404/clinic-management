using System.Net;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Tunisie SMS states its verdict inside the body, under HTTP 200. Each published code is read, an account-level
/// refusal pauses the sends instead of hammering, and nothing from the body reaches the stored result (D-8).
/// </summary>
public class TunisieSmsSenderTests
{
    private static readonly ResolvedReminderSettings Configured = new()
    {
        EnabledChannels = [NotificationType.SMS],
        SmsApiUrl = "https://api.tunisiesms.test/send",
        SmsSenderId = "APEXA",
        SmsApiKey = "key",
    };

    private static string Xml(int code) =>
        $"<?xml version=\"1.0\"?><status><status_code>{code}</status_code><status_msg>secret-detail</status_msg><message_id>42</message_id></status>";

    private sealed class Gateway : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;

        public Gateway(Func<HttpResponseMessage> respond) => _respond = respond;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_respond());
        }
    }

    private static (TunisieSmsSender Sender, Gateway Gateway) Build(
        Func<HttpResponseMessage> respond, SmsProviderBreaker? breaker = null)
    {
        var gateway = new Gateway(respond);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(gateway, disposeHandler: false));
        return (new TunisieSmsSender(factory.Object, breaker ?? new SmsProviderBreaker(), NullLogger<TunisieSmsSender>.Instance), gateway);
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    [Fact]
    public async Task A_200_Carrying_Code_200_Is_Sent()
    {
        var (sender, _) = Build(() => Ok(Xml(200)));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.Sent, result.Outcome);
    }

    /// <summary>The defect the generic sender had: a 200 announcing « crédit insuffisant » was recorded « Envoyé ».</summary>
    [Fact]
    public async Task A_200_Carrying_No_Credit_Is_Not_Sent_And_Pauses_The_Sends()
    {
        var breaker = new SmsProviderBreaker();
        var (sender, gateway) = Build(() => Ok(Xml(402)), breaker);

        var first = await sender.SendAsync("+21620123456", "Rappel", Configured);
        var second = await sender.SendAsync("+21620123457", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.Throttled, first.Outcome);
        Assert.Equal(ReminderSendOutcome.Throttled, second.Outcome);
        Assert.Equal(1, gateway.Calls); // the second row did not reach the provider
        Assert.True(breaker.IsOpen(DateTime.UtcNow, out _));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(442)]
    public async Task An_Account_Refusal_Waits_Rather_Than_Failing_The_Reminder(int code)
    {
        var (sender, _) = Build(() => Ok(Xml(code)));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.Throttled, result.Outcome);
    }

    [Fact]
    public async Task The_Daily_Quota_Waits_Too()
    {
        var (sender, _) = Build(() => Ok(Xml(420)));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.Throttled, result.Outcome);
    }

    [Fact]
    public async Task A_Refused_Destination_Fails_Without_Retrying()
    {
        var (sender, _) = Build(() => Ok(Xml(441)));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.RecipientUnreachable, result.Outcome);
    }

    [Theory]
    [InlineData(430)]
    [InlineData(431)]
    [InlineData(440)]
    public async Task A_Request_The_Provider_Rejects_Fails_Without_Retrying(int code)
    {
        var (sender, _) = Build(() => Ok(Xml(code)));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.RecipientUnreachable, result.Outcome);
    }

    [Fact]
    public async Task A_Json_Body_Is_Read_Too()
    {
        var (sender, _) = Build(() => Ok("{\"status\":{\"status_code\":\"402\",\"status_msg\":\"x\"}}"));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.Throttled, result.Outcome);
    }

    [Fact]
    public async Task A_Non_2xx_Carrying_A_Code_Is_Classified_By_That_Code()
    {
        var (sender, _) = Build(() => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Xml(441)) });

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.RecipientUnreachable, result.Outcome);
    }

    [Fact]
    public async Task An_Unknown_Code_Is_Transient()
    {
        var (sender, _) = Build(() => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

        Assert.Equal(ReminderSendOutcome.TransientFailure, result.Outcome);
    }

    [Fact]
    public async Task Nothing_From_The_Body_Reaches_The_Result()
    {
        foreach (var code in new[] { 400, 402, 420, 430, 441, 442, 499 })
        {
            var (sender, _) = Build(() => Ok(Xml(code)));

            var result = await sender.SendAsync("+21620123456", "Rappel", Configured);

            Assert.DoesNotContain("secret-detail", result.Error ?? string.Empty);
        }
    }

    [Fact]
    public async Task A_Message_Over_One_Sms_Is_Never_Sent()
    {
        var (sender, gateway) = Build(() => Ok(Xml(200)));

        var result = await sender.SendAsync("+21620123456", new string('x', 158), Configured);

        Assert.Equal(ReminderSendOutcome.RecipientUnreachable, result.Outcome);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public void The_Breaker_Never_Shortens_An_Open_Pause()
    {
        var breaker = new SmsProviderBreaker();
        var now = new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc);

        breaker.Trip(now, TimeSpan.FromHours(1), "quota");
        breaker.Trip(now, TimeSpan.FromMinutes(15), "credit");

        Assert.True(breaker.IsOpen(now.AddMinutes(30), out var reason));
        Assert.Equal("quota", reason);
        Assert.False(breaker.IsOpen(now.AddHours(1), out _));
    }
}
