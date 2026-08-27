using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicManagement.Application.Common.Authorization;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.API.Controllers;

/// <summary>
/// FR-7a's <b>first</b> writer of a cabinet's template state: Meta's <c>message_template_status_update</c> webhook.
/// It is what makes AC-1.5's « les rappels partiront dès la validation » true in minutes; the reconciling poll in
/// <c>MessagingAllowanceJob</c> is the second, and neither is a substitute for the other — a webhook Meta never
/// delivered, or one that arrived while this process was down, would otherwise strand a cabinet at « en attente de
/// validation » for ever with its reminders piling up held.
///
/// <para><b>⚠️ Anonymous, and the signature is the whole of the authentication.</b> Meta carries no bearer token, so
/// <c>X-Hub-Signature-256</c> — HMAC-SHA256 over the <b>raw</b> body with <c>Meta:AppSecret</c> — is what says the
/// payload is Meta's. Unconfigured secret ⇒ every POST is refused, never « accept anything ».</para>
///
/// <para><b>⚠️ It declares <c>UseSystemWide</c> as its first act, and without that it writes nothing while
/// answering 200.</b> Anonymous means no <c>User</c> row for <c>TenantScopeMiddleware</c> to resolve, so the scope
/// lands <c>Unset</c> — where the clinic query filters compare against <c>Guid.Empty</c> and return <b>no rows</b>.
/// <c>ClinicReminderSettings</c> is filtered, so the endpoint would verify its signature, parse its payload, resolve
/// no cabinet and report success. Neither derived guard catches it: <c>SystemWideCallerCoverageTests</c> derives its
/// candidates from « reads a filtered entity with <i>no HTTP context</i> », and a webhook has one — which is why that
/// test names this controller explicitly. And the symptom is not an error: the poll picks the state up on its next
/// pass, so the only observable effect is AC-1.5 degrading from minutes to a day.</para>
///
/// <para><b>⚠️ Absent where the deployment does not sell vendor messaging</b> (EC-16), like every other surface of
/// this feature — 404, not 403 or a silent 200.</para>
/// </summary>
[ApiController]
[Route(RoutePrefix)]
// Both actions below are deliberately [AllowAnonymous] — Meta cannot hold a token. The class policy exists so an
// action added here later is covered rather than silently anonymous, ConnectivityController's shape.
[Authorize(Policy = AuthorizationPolicies.AnyClinicRole)]
public class MetaWebhookController : ApiControllerBase
{
    private const string RoutePrefix = "api/meta/webhook";
    private const string SignatureHeader = "X-Hub-Signature-256";
    private const string SignaturePrefix = "sha256=";

    /// <summary>Meta's payloads are a few hundred bytes; this is a bound on an anonymous, unauthenticated read.</summary>
    private const int MaxBodyBytes = 128 * 1024;

    private readonly IVendorMessagingAvailability _availability;
    private readonly IClinicReminderSettingsRepository _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantScope _tenantScope;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MetaWebhookController> _logger;

    public MetaWebhookController(
        IVendorMessagingAvailability availability,
        IClinicReminderSettingsRepository settings,
        IUnitOfWork unitOfWork,
        ITenantScope tenantScope,
        IConfiguration configuration,
        ILogger<MetaWebhookController> logger)
    {
        _availability = availability;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _tenantScope = tenantScope;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Meta's subscription handshake: it echoes <c>hub.challenge</c> back, once, when the endpoint is registered.
    /// Refused unless <c>Meta:WebhookVerifyToken</c> is configured <b>and</b> matches — an unset token must not
    /// register an endpoint anybody can subscribe.
    /// </summary>
    [AllowAnonymous]
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (!_availability.SellsVendorMessaging)
        {
            return NotFound();
        }

        var expected = MetaConfig.WebhookVerifyToken(_configuration);
        if (string.IsNullOrWhiteSpace(expected))
        {
            _logger.LogWarning(
                "A Meta webhook verification arrived but Meta:WebhookVerifyToken is not configured; refusing.");
            return Forbid();
        }

        // Fixed-time comparison: this is a secret an attacker would otherwise be able to probe a byte at a time.
        if (mode != "subscribe"
            || verifyToken is null
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(verifyToken), Encoding.UTF8.GetBytes(expected)))
        {
            _logger.LogWarning("A Meta webhook verification failed its token check.");
            return Forbid();
        }

        return Content(challenge ?? string.Empty, "text/plain");
    }

    /// <summary>
    /// One <c>message_template_status_update</c> notification. Answers <b>200 for anything it accepts</b>, including
    /// a payload naming a WABA this deployment does not know — Meta retries a non-2xx, and a cabinet we cannot
    /// resolve is not something a retry will fix.
    /// </summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken = default)
    {
        if (!_availability.SellsVendorMessaging)
        {
            return NotFound();
        }

        // ⚠️ FIRST act — see the ⚠️ on the class. Without it every read below returns nothing and this answers 200.
        _tenantScope.UseSystemWide("Meta template status webhook");

        var appSecret = MetaConfig.AppSecret(_configuration);
        if (string.IsNullOrWhiteSpace(appSecret))
        {
            _logger.LogWarning("A Meta webhook payload arrived but Meta:AppSecret is not configured; refusing.");
            return Forbid();
        }

        var body = await ReadRawBodyAsync(cancellationToken);
        if (body is null)
        {
            return BadRequest();
        }

        if (!SignatureMatches(body, appSecret))
        {
            _logger.LogWarning("A Meta webhook payload failed its X-Hub-Signature-256 check.");
            return Forbid();
        }

        foreach (var update in MetaTemplateStatusPayload.Read(body))
        {
            await ApplyAsync(update, cancellationToken);
        }

        return Ok();
    }

    private async Task ApplyAsync(MetaTemplateStatusUpdate update, CancellationToken cancellationToken)
    {
        var settings = await _settings.GetByWhatsAppBusinessAccountIdAsync(update.BusinessAccountId, cancellationToken);
        if (settings is null)
        {
            // A WABA this deployment holds no cabinet for. Logged without the id, which is a customer's account
            // identifier on somebody else's Meta app.
            _logger.LogInformation("A Meta template webhook named a WhatsApp Business Account we do not hold.");
            return;
        }

        settings.SetWhatsAppTemplateState(update.Status, update.Category, update.TemplateId, DateTime.UtcNow);
        await _settings.UpdateAsync(settings, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Clinic {ClinicId}'s WhatsApp template is now {Status} (Meta webhook).", settings.Id, update.Status);
    }

    /// <summary>The raw bytes as sent, because the signature covers those and not a re-serialised copy.</summary>
    private async Task<string?> ReadRawBodyAsync(CancellationToken cancellationToken)
    {
        if (Request.ContentLength > MaxBodyBytes)
        {
            return null;
        }

        using var reader = new StreamReader(Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken);
        return body.Length > MaxBodyBytes ? null : body;
    }

    private bool SignatureMatches(string body, string appSecret)
    {
        var header = Request.Headers[SignatureHeader].ToString();
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var presented = header[SignaturePrefix.Length..].Trim();
        var computed = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(body)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(computed.ToLowerInvariant()));
    }
}

/// <summary>One template-status change Meta has told us about.</summary>
public sealed record MetaTemplateStatusUpdate(
    string BusinessAccountId,
    Domain.Enums.WhatsAppTemplateStatus Status,
    string? Category,
    string? TemplateId);

/// <summary>
/// Reads Meta's <c>entry[].changes[]</c> envelope into the updates this product acts on. A separate, <b>pure</b>
/// type so the payload shapes — a numeric <c>message_template_id</c>, several entries in one delivery, a change on a
/// field we do not handle — are assertable without a signature, an HTTP context or a tenant scope.
///
/// <para>⚠️ It is total: anything unreadable yields <b>no</b> updates rather than throwing. A malformed payload from
/// an anonymous caller must not become a 500, and Meta retries a non-2xx.</para>
/// </summary>
public static class MetaTemplateStatusPayload
{
    /// <summary>The field Story 0 confirmed. A change on any other field is ignored, not an error.</summary>
    public const string StatusField = "message_template_status_update";

    public static IReadOnlyList<MetaTemplateStatusUpdate> Read(string body)
    {
        var updates = new List<MetaTemplateStatusUpdate>();

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("entry", out var entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return updates;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                // The WABA id is the entry's own id — the payload's `value` never names the cabinet.
                var wabaId = Scalar(entry, "id");
                if (string.IsNullOrWhiteSpace(wabaId)
                    || !entry.TryGetProperty("changes", out var changes)
                    || changes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var change in changes.EnumerateArray())
                {
                    if (Scalar(change, "field") != StatusField
                        || !change.TryGetProperty("value", out var value))
                    {
                        continue;
                    }

                    // The status word arrives as `event`. Only the French reminder template concerns us: a WABA may
                    // hold others (a cabinet's own marketing template), and their review is none of our business.
                    if (Scalar(value, "message_template_name") is { } name
                        && name != WhatsAppReminderTemplate.Name)
                    {
                        continue;
                    }

                    if (WhatsAppTemplateStatuses.Parse(Scalar(value, "event")) is not { } status)
                    {
                        continue;
                    }

                    updates.Add(new MetaTemplateStatusUpdate(
                        wabaId!,
                        status,
                        // A status notification carries no category; where one is present it is honoured, and where
                        // it is not the stored value is preserved by SetWhatsAppTemplateState.
                        Scalar(value, "new_category") ?? Scalar(value, "category"),
                        Scalar(value, "message_template_id")));
                }
            }
        }
        catch (JsonException)
        {
            return updates;
        }

        return updates;
    }

    /// <summary>
    /// A string or a number as text. ⚠️ <c>message_template_id</c> is a <b>number</b> in Meta's payload while every
    /// id this product stores is text, so a string-only reader silently drops it and the poll loses its by-id read.
    /// </summary>
    private static string? Scalar(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null,
        };
    }
}
