using System.Text.Json;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sends WhatsApp reminders via the WhatsApp Business (Graph API) using a pre-approved <b>utility template</b>
/// whose single body parameter (<c>{{1}}</c>) carries the rendered reminder text — WhatsApp free-text is
/// never sent. Endpoint/identity/template/token come from the resolved settings for the row's clinic; disabled
/// (→ <c>NotConfigured</c>) unless the API URL, phone-number id, template name and access token are all present.
/// </summary>
public class WhatsAppSender : HttpReminderChannelSender, IReminderChannelSender
{
    // Matches RemindersConfig's per-install default; used when the resolved settings carry no template language.
    private const string DefaultTemplateLanguage = "fr";

    private readonly ILogger<WhatsAppSender> _logger;

    public WhatsAppSender(
        IHttpClientFactory httpClientFactory,
        ILogger<WhatsAppSender> logger)
        : base(httpClientFactory, logger)
    {
        _logger = logger;
    }

    public NotificationType Channel => NotificationType.WhatsApp;

    public Task<ReminderSendResult> SendAsync(
        string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.WhatsAppConfigured)
        {
            _logger.LogDebug("WhatsApp Business API not configured; skipping WhatsApp reminder to {Phone}.", ReminderPhone.Mask(phoneE164));
            return Task.FromResult(ReminderSendResult.NotConfigured);
        }

        var endpoint = $"{settings.WhatsAppApiUrl.TrimEnd('/')}/{settings.WhatsAppPhoneNumberId}/messages";
        var templateLanguage = string.IsNullOrWhiteSpace(settings.WhatsAppTemplateLanguage)
            ? DefaultTemplateLanguage
            : settings.WhatsAppTemplateLanguage;

        // Graph API "to" wants the E.164 number without the leading '+'.
        var to = phoneE164.TrimStart('+');
        var language = new { code = templateLanguage };

        // A proper reminder template has one body variable {{1}} that receives the rendered text. A
        // parameter-less template (e.g. hello_world) must be sent WITHOUT a components array, or Meta rejects
        // it with "#132000 number of params does not match". Branch on the resolved setting.
        object payload = settings.WhatsAppTemplateHasBodyParam
            ? new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = settings.WhatsAppTemplateName,
                    language,
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[]
                            {
                                new { type = "text", text = message }
                            }
                        }
                    }
                }
            }
            : new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = settings.WhatsAppTemplateName,
                    language
                }
            };

        return PostJsonAsync(endpoint, payload, settings.WhatsAppAccessToken, "WhatsApp", cancellationToken);
    }

    /// <summary>
    /// FR-8 — Meta's own refusals, told apart by their <c>error.code</c>:
    /// a <b>throttle</b> leaves the row queued and spends no retry budget, and a <b>stopped number</b> parks it,
    /// because retrying burns capacity and cannot succeed (EC-11). Anything else keeps the previous behaviour.
    ///
    /// <para>⚠️ <b>Read on the FULL body</b> (see the base's own ⚠️): Meta puts a long <c>message</c> before
    /// <c>code</c>, so classifying the truncated log copy matches nothing on a real payload — which is why
    /// <c>WhatsAppSenderErrorClassificationTests</c> asserts against a realistic full-length envelope rather than a
    /// short fixture, which would pass against a sender that fails on every genuine response.</para>
    ///
    /// <para>⚠️ Nothing from <paramref name="body"/> reaches the result. The sentences below are ours (D-8).</para>
    /// </summary>
    protected override ReminderSendResult Classify(string body, int statusCode, string channelLabel)
    {
        var code = MetaErrorCode(body);

        return code switch
        {
            // Application, account and platform rate limits, and too many messages to one recipient. Nothing is
            // wrong with this reminder, so it must not spend an attempt.
            4 or 80007 or 130429 or 131056 => ReminderSendResult.Throttled(
                "Limite d'envoi WhatsApp atteinte — rappel reporté automatiquement"),

            // 131048: the sender's messaging health / spam-rate limit. 131064: the account has hit the limit its
            // template classification puts on it. Both are stopped-until-somebody-acts, never a retry away.
            131048 or 131064 => ReminderSendResult.Blocked(
                OutboxBlockReason.MessagingNumberStopped,
                "Envoi WhatsApp suspendu par Meta — rappel en attente, nous nous en occupons"),

            _ => base.Classify(body, statusCode, channelLabel),
        };
    }

    /// <summary>
    /// Meta's <c>error.code</c>, or null when the body is not a Graph error envelope. An unreadable body is left to
    /// the base — falling back to a *named* outcome on a payload we could not parse would be asserting something
    /// about Meta's answer on no evidence.
    /// </summary>
    private static int? MetaErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                   && error.TryGetProperty("code", out var code)
                   && code.TryGetInt32(out var value)
                ? value
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
