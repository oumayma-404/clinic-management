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
        : base(httpClientFactory)
    {
        _logger = logger;
    }

    public NotificationType Channel => NotificationType.WhatsApp;

    public Task<ReminderSendResult> SendAsync(
        string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.WhatsAppApiUrl) ||
            string.IsNullOrWhiteSpace(settings.WhatsAppPhoneNumberId) ||
            string.IsNullOrWhiteSpace(settings.WhatsAppTemplateName) ||
            string.IsNullOrWhiteSpace(settings.WhatsAppAccessToken))
        {
            _logger.LogDebug("WhatsApp Business API not configured; skipping WhatsApp reminder to {Phone}.", ReminderPhone.Mask(phoneE164));
            return Task.FromResult(ReminderSendResult.NotConfigured);
        }

        var endpoint = $"{settings.WhatsAppApiUrl.TrimEnd('/')}/{settings.WhatsAppPhoneNumberId}/messages";
        var templateLanguage = string.IsNullOrWhiteSpace(settings.WhatsAppTemplateLanguage)
            ? DefaultTemplateLanguage
            : settings.WhatsAppTemplateLanguage;

        // Graph API "to" wants the E.164 number without the leading '+'.
        var payload = new
        {
            messaging_product = "whatsapp",
            to = phoneE164.TrimStart('+'),
            type = "template",
            template = new
            {
                name = settings.WhatsAppTemplateName,
                language = new { code = templateLanguage },
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
        };

        return PostJsonAsync(endpoint, payload, settings.WhatsAppAccessToken, "WhatsApp", cancellationToken);
    }
}
