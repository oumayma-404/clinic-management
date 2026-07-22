using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sends SMS reminders via a config-driven generic HTTP gateway (JSON POST) using the resolved alphanumeric
/// sender ID and API key (bearer token) for the row's clinic. Disabled (→ <c>NotConfigured</c>) unless the
/// gateway URL, sender ID and API key are all present in the resolved settings.
/// </summary>
public class HttpSmsSender : HttpReminderChannelSender, IReminderChannelSender
{
    private readonly ILogger<HttpSmsSender> _logger;

    public HttpSmsSender(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpSmsSender> logger)
        : base(httpClientFactory)
    {
        _logger = logger;
    }

    public NotificationType Channel => NotificationType.SMS;

    public Task<ReminderSendResult> SendAsync(
        string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default)
    {
        if (!settings.SmsConfigured)
        {
            _logger.LogDebug("SMS gateway not configured; skipping SMS reminder to {Phone}.", ReminderPhone.Mask(phoneE164));
            return Task.FromResult(ReminderSendResult.NotConfigured);
        }

        var payload = new
        {
            sender = settings.SmsSenderId,
            to = phoneE164,
            message
        };

        return PostJsonAsync(settings.SmsApiUrl, payload, settings.SmsApiKey, "SMS", cancellationToken);
    }
}
