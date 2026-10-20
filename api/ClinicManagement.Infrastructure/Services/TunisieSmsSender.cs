using System.Text.RegularExpressions;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Sends SMS reminders through <b>Tunisie SMS</b> (tunisiesms.tn), under the resolved sender id and API key — the
/// vendor's own account where the vendor sells messaging. Disabled (→ <c>NotConfigured</c>) unless the URL, sender and
/// key are all present.
///
/// <para>⚠️ <b>Tunisie SMS answers 200 and states its verdict inside the body</b> (<c>&lt;status_code&gt;</c>):
/// 402 « crédit insuffisant » arrives as an HTTP success. Trusting the status — what the generic sender did — marks a
/// reminder « Envoyé » that never left. So the body is read on success too, and each code is mapped below.</para>
///
/// <para>⚠️ <b>The request shape is provisional until the provider's API document is in hand</b>: it posts
/// <c>{ sender, to, message }</c> with the key as a bearer token, and the number without its « + ». Only
/// <see cref="BuildRequest"/> should have to change when the document arrives — the classification follows the codes
/// the provider publishes.</para>
/// </summary>
public class TunisieSmsSender : HttpReminderChannelSender, IReminderChannelSender
{
    /// <summary>How long « no credit / unknown key / sender not approved » stops the sends — long enough to stop a storm.</summary>
    public static readonly TimeSpan AccountRefusalPause = TimeSpan.FromMinutes(15);

    /// <summary>How long the daily quota stops the sends.</summary>
    public static readonly TimeSpan QuotaPause = TimeSpan.FromHours(1);

    private static readonly Regex XmlStatusCode = new(
        @"<status_code>\s*(\d{3})\s*</status_code>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JsonStatusCode = new(
        @"""status_code""\s*:\s*""?(\d{3})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly SmsProviderBreaker _breaker;
    private readonly ILogger<TunisieSmsSender> _logger;

    public TunisieSmsSender(
        IHttpClientFactory httpClientFactory,
        SmsProviderBreaker breaker,
        ILogger<TunisieSmsSender> logger)
        : base(httpClientFactory, logger)
    {
        _breaker = breaker;
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

        // The renderer guarantees one SMS; this is the last line before money is spent on a second one.
        if (message.Length > ReminderText.SmsMaxLength)
        {
            return Task.FromResult(ReminderSendResult.RecipientUnreachable(
                $"Message de plus de {ReminderText.SmsMaxLength} caractères — non envoyé"));
        }

        if (_breaker.IsOpen(DateTime.UtcNow, out var pausedBecause))
        {
            return Task.FromResult(ReminderSendResult.Throttled(pausedBecause ?? "Envoi SMS suspendu"));
        }

        return PostJsonAsync(settings.SmsApiUrl, BuildRequest(phoneE164, message, settings), settings.SmsApiKey, "SMS", cancellationToken);
    }

    /// <summary>The provider's request body. Provisional — see the type note.</summary>
    private static object BuildRequest(string phoneE164, string message, ResolvedReminderSettings settings) => new
    {
        sender = settings.SmsSenderId,
        to = phoneE164.TrimStart('+'),
        message,
    };

    protected override ReminderSendResult ClassifySuccess(string body, string channelLabel) =>
        StatusCodeIn(body) is { } code ? FromProviderCode(code) : ReminderSendResult.Sent;

    protected override ReminderSendResult Classify(string body, int statusCode, string channelLabel) =>
        FromProviderCode(StatusCodeIn(body) ?? statusCode);

    /// <summary>
    /// The provider's published codes. Nothing from the body reaches the result — the sentences are ours (D-8).
    /// </summary>
    private ReminderSendResult FromProviderCode(int code)
    {
        switch (code)
        {
            case 200:
                return ReminderSendResult.Sent;

            // Our account, not this reminder: stop asking for a while, keep the rows queued, and say so loudly.
            case 400 or 401 or 402 or 442:
            {
                var reason = code switch
                {
                    402 => "Crédit SMS épuisé chez le fournisseur — envoi en attente",
                    442 => "Expéditeur SMS non validé par l'opérateur — envoi en attente",
                    _ => "Compte SMS refusé par le fournisseur — envoi en attente",
                };
                _breaker.Trip(DateTime.UtcNow, AccountRefusalPause, reason);
                _logger.LogError("Tunisie SMS refused the account (code {Code}); SMS sends paused for {Minutes} min.",
                    code, AccountRefusalPause.TotalMinutes);
                return ReminderSendResult.Throttled(reason);
            }

            case 420:
                _breaker.Trip(DateTime.UtcNow, QuotaPause, "Quota SMS journalier atteint — envoi en attente");
                _logger.LogWarning("Tunisie SMS daily quota reached (code 420); SMS sends paused for {Hours} h.",
                    QuotaPause.TotalHours);
                return ReminderSendResult.Throttled("Quota SMS journalier atteint — envoi en attente");

            case 441:
                return ReminderSendResult.RecipientUnreachable("Numéro refusé par l'opérateur SMS");

            // A request we built wrong: retrying the same body cannot succeed.
            case 430 or 431 or 440:
                _logger.LogError("Tunisie SMS rejected the request itself (code {Code}).", code);
                return ReminderSendResult.RecipientUnreachable($"Message refusé par le fournisseur SMS (code {code})");

            default:
                return ReminderSendResult.Transient($"SMS gateway returned {code}");
        }
    }

    private static int? StatusCodeIn(string body)
    {
        var match = XmlStatusCode.Match(body);
        if (!match.Success)
        {
            match = JsonStatusCode.Match(body);
        }

        return match.Success && int.TryParse(match.Groups[1].Value, out var code) ? code : null;
    }
}
