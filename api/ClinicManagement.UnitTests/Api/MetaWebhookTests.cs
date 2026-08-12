using System.Security.Cryptography;
using System.Text;
using ClinicManagement.API.Controllers;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// FR-7a's webhook writer (<c>vendor-whatsapp-messaging-quota</c> Part 4 § 34).
///
/// <para><b>⚠️ The load-bearing case is that it actually WRITES, with the tenant scope left exactly as production
/// leaves it.</b> The endpoint is anonymous, so there is no <c>User</c> row for <c>TenantScopeMiddleware</c> to
/// resolve and the scope arrives <c>Unset</c> — where the clinic query filters compare against <c>Guid.Empty</c> and
/// return nothing. Without its own <c>UseSystemWide</c> the webhook would verify its signature, parse its payload,
/// resolve no cabinet, write nothing and answer <b>200 to Meta</b>. So these cases hand it the <b>real</b>
/// <see cref="TenantScope"/> and assert what it declared: a test that sets a scope by hand asserts the one
/// arrangement that is broken (the <c>PlatformAccountStateMiddleware</c> lesson, one feature over).</para>
///
/// <para>⚠️ The payload reader is exercised directly too, because a numeric <c>message_template_id</c>, several
/// entries in one delivery and a change on a field we do not handle are all shapes that need no signature, no HTTP
/// context and no scope to assert.</para>
/// </summary>
public class MetaWebhookTests
{
    private const string AppSecret = "meta-app-secret";
    private const string VerifyToken = "the-verify-token";
    private const string WabaId = "WABA-1";
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class Harness
    {
        public Mock<IClinicReminderSettingsRepository> Settings { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();

        /// <summary>The real one — see the ⚠️ on the class.</summary>
        public TenantScope Scope { get; } = new(NullLogger<TenantScope>.Instance);

        public ClinicReminderSettings Stored { get; }
        public MetaWebhookController Controller { get; }

        public Harness(bool sellsVendorMessaging = true, bool secretsConfigured = true)
        {
            Stored = new ClinicReminderSettings(ClinicId);
            Stored.ApplyWhatsAppConnection(WabaId, "PHONE-1");

            Settings.Setup(r => r.GetByWhatsAppBusinessAccountIdAsync(WabaId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Stored);

            var availability = new Mock<IVendorMessagingAvailability>();
            availability.SetupGet(a => a.SellsVendorMessaging).Returns(sellsVendorMessaging);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Meta:AppSecret"] = secretsConfigured ? AppSecret : null,
                    ["Meta:WebhookVerifyToken"] = secretsConfigured ? VerifyToken : null,
                })
                .Build();

            Controller = new MetaWebhookController(
                availability.Object, Settings.Object, UnitOfWork.Object, Scope, config,
                NullLogger<MetaWebhookController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
            };
        }

        /// <summary>Posts a body with a signature, honest or forged.</summary>
        public Task<IActionResult> PostAsync(string body, string? signature = null)
        {
            var http = Controller.ControllerContext.HttpContext;
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            http.Request.ContentLength = Encoding.UTF8.GetByteCount(body);
            http.Request.Headers["X-Hub-Signature-256"] = signature ?? Sign(body);
            return Controller.Receive();
        }
    }

    private static string Sign(string body) =>
        "sha256=" + Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(AppSecret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    /// <summary>Meta's real envelope shape: the WABA is the ENTRY's id, and the template id is a number.</summary>
    private static string StatusPayload(string status, string name = WhatsAppReminderTemplate.Name) =>
        $$$"""
        {"object":"whatsapp_business_account","entry":[{"id":"{{{WabaId}}}","time":1786000000,"changes":[
          {"field":"message_template_status_update","value":{"event":"{{{status}}}",
           "message_template_id":1234567890,"message_template_name":"{{{name}}}","message_template_language":"fr",
           "reason":"NONE"}}]}]}
        """;

    // ---- The one that matters ------------------------------------------------------------------

    /// <summary>
    /// [FR-7a] A valid notification for a known WABA moves that cabinet's template state — <b>and</b> the endpoint
    /// declared the cross-clinic scope without which it would have resolved nothing. See the ⚠️ on the class.
    /// </summary>
    [Fact]
    public async Task An_Approval_Moves_The_Cabinets_Template_State()
    {
        var harness = new Harness();

        var result = await harness.PostAsync(StatusPayload("APPROVED"));

        Assert.IsType<OkResult>(result);
        Assert.Equal(WhatsAppTemplateStatus.Approved, harness.Stored.WhatsAppTemplateStatus);
        Assert.NotNull(harness.Stored.WhatsAppTemplateStatusCheckedAtUtc);
        Assert.Equal("1234567890", harness.Stored.WhatsAppTemplateId);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Without this the read above returns nothing in production while every layer reports success.
        Assert.Equal(TenantScopeKind.SystemWide, harness.Scope.Kind);
    }

    /// <summary>[FR-7a][EC-10] A refusal is recorded as such — the state the practice is told and we act on.</summary>
    [Fact]
    public async Task A_Rejection_Is_Recorded()
    {
        var harness = new Harness();

        await harness.PostAsync(StatusPayload("REJECTED"));

        Assert.Equal(WhatsAppTemplateStatus.Rejected, harness.Stored.WhatsAppTemplateStatus);
    }

    // ---- What it must refuse -------------------------------------------------------------------

    /// <summary>[§ 34] A forged signature writes nothing. The signature IS the authentication here.</summary>
    [Fact]
    public async Task A_Forged_Signature_Is_Refused_And_Writes_Nothing()
    {
        var harness = new Harness();

        var result = await harness.PostAsync(StatusPayload("APPROVED"), signature: "sha256=deadbeef");

        Assert.IsType<ForbidResult>(result);
        Assert.Null(harness.Stored.WhatsAppTemplateStatus);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// [§ 34] An unconfigured app secret refuses every payload — never « accept anything ». An endpoint that writes a
    /// cabinet's sending state on an unauthenticated POST is worse than one that is temporarily deaf.
    /// </summary>
    [Fact]
    public async Task An_Unconfigured_Secret_Refuses_Rather_Than_Accepting()
    {
        var harness = new Harness(secretsConfigured: false);

        var result = await harness.PostAsync(StatusPayload("APPROVED"));

        Assert.IsType<ForbidResult>(result);
        Assert.Null(harness.Stored.WhatsAppTemplateStatus);
    }

    /// <summary>[EC-16] Absent where the deployment does not sell vendor messaging — 404, not 403 and not a 200.</summary>
    [Fact]
    public async Task It_Is_Absent_Where_Vendor_Messaging_Is_Not_Sold()
    {
        var harness = new Harness(sellsVendorMessaging: false);

        Assert.IsType<NotFoundResult>(await harness.PostAsync(StatusPayload("APPROVED")));
        Assert.IsType<NotFoundResult>(harness.Controller.Verify("subscribe", VerifyToken, "challenge-42"));
    }

    // ---- The subscription handshake ------------------------------------------------------------

    /// <summary>[§ 34] The verify handshake echoes <c>hub.challenge</c> when the token matches.</summary>
    [Fact]
    public void The_Verify_Handshake_Answers_The_Challenge()
    {
        var content = Assert.IsType<ContentResult>(
            new Harness().Controller.Verify("subscribe", VerifyToken, "challenge-42"));

        Assert.Equal("challenge-42", content.Content);
    }

    /// <summary>[§ 34] A wrong token, and an unconfigured one, both refuse.</summary>
    [Fact]
    public void The_Verify_Handshake_Refuses_A_Wrong_Or_Unconfigured_Token()
    {
        Assert.IsType<ForbidResult>(new Harness().Controller.Verify("subscribe", "guessed", "challenge-42"));
        Assert.IsType<ForbidResult>(
            new Harness(secretsConfigured: false).Controller.Verify("subscribe", VerifyToken, "challenge-42"));
    }

    // ---- The payload reader, on its own ---------------------------------------------------------

    /// <summary>
    /// [§ 34] <c>message_template_id</c> is a <b>number</b> in Meta's payload while every id this product stores is
    /// text — a string-only reader drops it silently and the poll loses its by-id read.
    /// </summary>
    [Fact]
    public void A_Numeric_Template_Id_Is_Read_As_Text()
    {
        var update = Assert.Single(MetaTemplateStatusPayload.Read(StatusPayload("APPROVED")));

        Assert.Equal("1234567890", update.TemplateId);
        Assert.Equal(WabaId, update.BusinessAccountId);
    }

    /// <summary>
    /// [§ 34] A WABA may hold templates that are not ours — a cabinet's own marketing template — and their review is
    /// none of our business. Acting on one would move this cabinet's sending state for the wrong reason.
    /// </summary>
    [Fact]
    public void Another_Templates_Status_Is_Ignored()
    {
        Assert.Empty(MetaTemplateStatusPayload.Read(StatusPayload("REJECTED", name: "promo_ete")));
    }

    /// <summary>
    /// [§ 34] Anything unreadable yields no updates rather than throwing: a malformed payload from an anonymous
    /// caller must not become a 500, and Meta retries a non-2xx.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("""{"entry":"not-an-array"}""")]
    [InlineData("""{"entry":[{"id":"WABA-1","changes":[{"field":"message_template_quality_update","value":{}}]}]}""")]
    public void An_Unreadable_Or_Irrelevant_Payload_Yields_Nothing(string body) =>
        Assert.Empty(MetaTemplateStatusPayload.Read(body));

    /// <summary>
    /// [§ 34] An unknown status word holds rather than releases. Meta adds states, and this product's rule is that
    /// <b>only</b> Approved may send — so the consequence of not recognising one must be a delayed reminder, never a
    /// message Meta refuses and a unit nobody can account for.
    /// </summary>
    [Fact]
    public void An_Unknown_Status_Word_Falls_On_The_Holding_Side()
    {
        var update = Assert.Single(MetaTemplateStatusPayload.Read(StatusPayload("SOME_FUTURE_STATE")));

        Assert.NotEqual(WhatsAppTemplateStatus.Approved, update.Status);
    }
}
