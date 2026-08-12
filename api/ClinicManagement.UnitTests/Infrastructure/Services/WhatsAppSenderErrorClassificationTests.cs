using System.Net;
using System.Text;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// FR-8 — Meta's own refusals told apart (<c>vendor-whatsapp-messaging-quota</c> Part 4 § 37): a <b>throttle</b>
/// leaves the row queued and spends no retry budget, a <b>stopped number</b> parks it, and anything else keeps the
/// transient behaviour every channel had before.
///
/// <para><b>⚠️ Every classified case is asserted against a FULL-LENGTH envelope</b> (&gt; 200 characters, with
/// <c>code</c> after a long <c>message</c>), and that is the whole point of this class rather than a detail of it.
/// The base truncates the body at 200 characters for its log line, and Meta puts a long <c>message</c> — plus
/// <c>error_user_title</c> and <c>error_user_msg</c> — <i>before</i> <c>code</c>. A classifier fed the truncated
/// copy finds no code at all and falls through to transient, so FR-8 would read as implemented at every layer while
/// being inert. A short fixture like <c>{"error":{"code":131048}}</c> is under 200 characters and would pass
/// against exactly that broken sender.</para>
///
/// <para>⚠️ The other standing assertion is that <b>no response body reaches the result</b> — the
/// <c>SECURITY_REVIEW_2026-08</c> finding (D-8): the endpoint URL is tenant-supplied and the result's message is
/// persisted on the outbox row and served back to <i>any</i> clinic role, so echoing Meta's body there turns a
/// settings field into a read primitive.</para>
/// </summary>
public class WhatsAppSenderErrorClassificationTests
{
    /// <summary>A realistic Graph error envelope: the code sits well past the base's 200-character log cut.</summary>
    private static string MetaError(int code) =>
        $$$"""
        {"error":{"message":"(#{{{code}}}) Une erreur est survenue lors de l'envoi du message. Le compte WhatsApp Business associé à ce numéro ne peut pas envoyer ce message pour le moment. Veuillez consulter la documentation Meta pour plus de détails sur cette condition.","type":"OAuthException","error_subcode":2494055,"error_user_title":"Message non envoyé","error_user_msg":"Ce message n'a pas pu être remis au destinataire.","fbtrace_id":"AbCdEfGhIjKlMnOpQrStUv","code":{{{code}}}}}
        """;

    private static ResolvedReminderSettings Configured() => new()
    {
        EnabledChannels = new[] { NotificationType.WhatsApp },
        WhatsAppApiUrl = "https://graph.facebook.com/v21.0",
        WhatsAppPhoneNumberId = "123456789",
        WhatsAppTemplateName = "rappel_rendez_vous",
        WhatsAppAccessToken = "secret-token",
    };

    private static async Task<ReminderSendResult> SendAgainst(string body, HttpStatusCode status)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler, disposeHandler: false));

        var sender = new WhatsAppSender(factory.Object, NullLogger<WhatsAppSender>.Instance);
        return await sender.SendAsync("+21620123456", "Rappel", Configured());
    }

    // ---- The six codes -------------------------------------------------------------------------

    /// <summary>
    /// [FR-8] Application, account and platform rate limits, and too-many-to-one-recipient. Nothing is wrong with
    /// the reminder, so it must keep its whole retry budget.
    /// </summary>
    [Theory]
    [InlineData(4)]      // application request limit
    [InlineData(80007)]  // rate limit hit
    [InlineData(130429)] // cloud API message throughput
    [InlineData(131056)] // too many messages to one recipient
    public async Task A_Throttle_Defers_The_Row(int code)
    {
        var result = await SendAgainst(MetaError(code), HttpStatusCode.TooManyRequests);

        Assert.Equal(ReminderSendOutcome.Throttled, result.Outcome);
        Assert.Null(result.BlockReason);
    }

    /// <summary>
    /// [FR-8][EC-11] Meta has stopped this sender: the row is <b>held</b> under its own reason rather than retried
    /// three times and failed, because a retry cannot change the answer and would burn the budget.
    /// </summary>
    [Theory]
    [InlineData(131048)] // messaging health / spam-rate limit
    [InlineData(131064)] // account limit reached (template classification)
    public async Task A_Stopped_Number_Parks_The_Row(int code)
    {
        var result = await SendAgainst(MetaError(code), HttpStatusCode.BadRequest);

        Assert.Equal(ReminderSendOutcome.Blocked, result.Outcome);
        Assert.Equal(OutboxBlockReason.MessagingNumberStopped, result.BlockReason);
    }

    /// <summary>[FR-8] Anything else keeps the behaviour that shipped: an ordinary transient failure.</summary>
    [Fact]
    public async Task An_Unrecognised_Code_Stays_Transient()
    {
        var result = await SendAgainst(MetaError(132000), HttpStatusCode.BadRequest);

        Assert.Equal(ReminderSendOutcome.TransientFailure, result.Outcome);
        Assert.Null(result.BlockReason);
    }

    /// <summary>
    /// [FR-8] A body that is not a Graph envelope at all — an HTML error page from a proxy, say — is transient. A
    /// <i>named</i> outcome on a payload we could not parse would be asserting something about Meta's answer on no
    /// evidence.
    /// </summary>
    [Fact]
    public async Task An_Unparseable_Body_Stays_Transient()
    {
        var result = await SendAgainst("<html><body>502 Bad Gateway</body></html>", HttpStatusCode.BadGateway);

        Assert.Equal(ReminderSendOutcome.TransientFailure, result.Outcome);
    }

    // ---- The fixture's own premise, and the security property ----------------------------------

    /// <summary>
    /// The envelope really is longer than the log truncation, so the cases above genuinely exercise the defect they
    /// exist for. Without this, shortening the fixture one day would quietly make every assertion above vacuous.
    /// </summary>
    [Fact]
    public void The_Fixture_Envelope_Is_Longer_Than_The_Logs_Truncation()
    {
        var body = MetaError(131048);

        Assert.True(body.Length > 200, $"The fixture is only {body.Length} characters — see the class note.");
        Assert.True(
            body.IndexOf("\"code\"", StringComparison.Ordinal) > 200,
            "`code` must sit past the 200-character cut, or a truncated classifier would still find it.");
    }

    /// <summary>
    /// [D-8] Nothing Meta returned may reach the result — not on the classified paths and not on the transient one.
    /// The endpoint is tenant-supplied and this message is served back to any clinic role.
    /// </summary>
    [Theory]
    [InlineData(131048)]
    [InlineData(4)]
    [InlineData(132000)]
    public async Task No_Response_Body_Reaches_The_Result(int code)
    {
        var result = await SendAgainst(MetaError(code), HttpStatusCode.BadRequest);

        Assert.NotNull(result.Error);
        Assert.DoesNotContain("fbtrace_id", result.Error);
        Assert.DoesNotContain("AbCdEfGhIjKlMnOpQrStUv", result.Error);
        Assert.DoesNotContain("OAuthException", result.Error);
        Assert.DoesNotContain("error_user_msg", result.Error);
        Assert.DoesNotContain("documentation Meta", result.Error);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request));
    }
}
