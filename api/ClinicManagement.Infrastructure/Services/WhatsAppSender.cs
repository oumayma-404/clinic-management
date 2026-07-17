using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sends WhatsApp reminders via the WhatsApp Business (Graph API) using a pre-approved <b>utility template</b>
/// whose single body parameter (<c>{{1}}</c>) carries the rendered reminder text — WhatsApp free-text is
/// never sent. Disabled (→ <c>NotConfigured</c>) unless the API URL, phone-number id, template name and
/// access token are all set.
/// </summary>
public class WhatsAppSender : HttpReminderChannelSender, IReminderChannelSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppSender> _logger;

    public WhatsAppSender(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WhatsAppSender> logger)
        : base(httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public NotificationType Channel => NotificationType.WhatsApp;

    public Task<ReminderSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        var apiUrl = RemindersConfig.WhatsAppApiUrl(_configuration);
        var phoneNumberId = RemindersConfig.WhatsAppPhoneNumberId(_configuration);
        var templateName = RemindersConfig.WhatsAppTemplateName(_configuration);
        var accessToken = RemindersConfig.WhatsAppAccessToken(_configuration);
        var templateLanguage = RemindersConfig.WhatsAppTemplateLanguage(_configuration);

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(phoneNumberId) ||
            string.IsNullOrWhiteSpace(templateName) || string.IsNullOrWhiteSpace(accessToken))
        {
            _logger.LogDebug("WhatsApp Business API not configured; skipping WhatsApp reminder to {Phone}.", phoneE164);
            return Task.FromResult(ReminderSendResult.NotConfigured);
        }

        var endpoint = $"{apiUrl.TrimEnd('/')}/{phoneNumberId}/messages";

        // Graph API "to" wants the E.164 number without the leading '+'.
        var payload = new
        {
            messaging_product = "whatsapp",
            to = phoneE164.TrimStart('+'),
            type = "template",
            template = new
            {
                name = templateName,
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

        return PostJsonAsync(endpoint, payload, accessToken, "WhatsApp", cancellationToken);
    }
}
