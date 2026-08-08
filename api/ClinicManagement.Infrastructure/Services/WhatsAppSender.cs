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
}
