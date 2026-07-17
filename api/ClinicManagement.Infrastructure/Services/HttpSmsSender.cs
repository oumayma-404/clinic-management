using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sends SMS reminders via a config-driven generic HTTP gateway (JSON POST) using the configured
/// alphanumeric sender ID and an API key as a bearer token. Disabled (→ <c>NotConfigured</c>) unless the
/// gateway URL, sender ID and API key are all set.
/// </summary>
public class HttpSmsSender : HttpReminderChannelSender, IReminderChannelSender
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<HttpSmsSender> _logger;

    public HttpSmsSender(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<HttpSmsSender> logger)
        : base(httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public NotificationType Channel => NotificationType.SMS;

    public Task<ReminderSendResult> SendAsync(string phoneE164, string message, CancellationToken cancellationToken = default)
    {
        var apiUrl = RemindersConfig.SmsApiUrl(_configuration);
        var senderId = RemindersConfig.SmsSenderId(_configuration);
        var apiKey = RemindersConfig.SmsApiKey(_configuration);

        if (string.IsNullOrWhiteSpace(apiUrl) || string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogDebug("SMS gateway not configured; skipping SMS reminder to {Phone}.", phoneE164);
            return Task.FromResult(ReminderSendResult.NotConfigured);
        }

        var payload = new
        {
            sender = senderId,
            to = phoneE164,
            message
        };

        return PostJsonAsync(apiUrl, payload, apiKey, "SMS", cancellationToken);
    }
}
