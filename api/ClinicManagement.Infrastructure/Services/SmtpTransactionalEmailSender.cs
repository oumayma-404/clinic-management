using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// <see cref="ITransactionalEmailSender"/> over the framework's own <see cref="System.Net.Mail.SmtpClient"/>,
/// on <see cref="SmtpDocumentEmailSender"/>'s pattern and for the same reason: no new package, because the
/// offline-LAN installer carries every dependency this solution takes.
///
/// <para>⚠️ <b>It reads <see cref="SmtpConfig"/> — the per-install <c>Notification:Smtp:*</c> section — and not
/// <c>ResolvedReminderSettings</c>.</b> That is not an oversight to be tidied up later: the settings object every
/// other sender takes is resolved <i>per clinic</i>, and this sender's only caller is clinic self-signup, which
/// runs before any clinic exists. Moving it onto <c>IReminderSettingsProvider</c> would compile and would leave
/// the feature with no way to send anything.</para>
///
/// <para>Never throws: a failure is classified so the caller can turn it into a French refusal the visitor can
/// act on.</para>
/// </summary>
public class SmtpTransactionalEmailSender : ITransactionalEmailSender
{
    // ⚠️ Enforced by a linked CancellationTokenSource, NOT SmtpClient.Timeout — that governs the synchronous Send
    // only, so setting it left this bounded by nothing but the OS TCP timeout, with the visitor on a spinner.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(20);

    private readonly IConfiguration _configuration;
    private readonly IPublicAppUrlProvider _appUrl;
    private readonly ILogger<SmtpTransactionalEmailSender> _logger;

    public SmtpTransactionalEmailSender(
        IConfiguration configuration,
        IPublicAppUrlProvider appUrl,
        ILogger<SmtpTransactionalEmailSender> logger)
    {
        _configuration = configuration;
        _appUrl = appUrl;
        _logger = logger;
    }

    /// <summary>
    /// A host and a usable envelope sender are the two things without which nothing can be sent. Credentials are
    /// not among them — an unauthenticated relay is a real deployment (the same allowance the document sender
    /// makes).
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpConfig.Host(_configuration))
        && !string.IsNullOrWhiteSpace(SmtpConfig.FromAddress(_configuration));

    public async Task<TransactionalEmailResult> SendAsync(
        string recipientEmail,
        string subject,
        EmailContent content,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return TransactionalEmailResult.NotConfigured;
        }

        // One snapshot of the section, so a reload mid-send cannot mix two configurations — and so the envelope
        // sender is proven non-null once rather than re-read behind a null-forgiving operator at each use.
        var fromAddress = SmtpConfig.FromAddress(_configuration);
        var fromName = SmtpConfig.FromName(_configuration);
        var username = SmtpConfig.Username(_configuration);

        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            return TransactionalEmailResult.NotConfigured;
        }

        var plainText = EmailLayout.PlainText(content);

        try
        {
            // ⚠️ Deliberately NOT behind `PublicEgressGuard`, unlike `SmtpDocumentEmailSender` beside it. That
            // one dials a host a clinic admin typed into a settings screen, which is a tenant-controlled
            // endpoint and therefore an SSRF primitive. This one reads the OPERATOR's own configuration, where
            // an internal relay on the compose network is the normal, intended arrangement — guarding it would
            // refuse the correct deployment and protect nobody, since anyone who can edit the configuration
            // already has the container.
            using var client = new SmtpClient(SmtpConfig.Host(_configuration), SmtpConfig.Port(_configuration))
            {
                EnableSsl = SmtpConfig.UseTls(_configuration),
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(
                    username, SmtpConfig.Password(_configuration) ?? string.Empty);
            }

            using var mail = new MailMessage
            {
                From = string.IsNullOrWhiteSpace(fromName)
                    ? new MailAddress(fromAddress)
                    : new MailAddress(fromAddress, fromName),
                Subject = subject,
                SubjectEncoding = Encoding.UTF8,
                // The plain-text alternate is ALSO the `Body`, so a client reading only that property (and a
                // spam filter scoring the message) sees prose rather than markup.
                Body = plainText,
                BodyEncoding = Encoding.UTF8,
                IsBodyHtml = false
            };
            mail.To.Add(new MailAddress(recipientEmail));

            // ⚠️ `multipart/alternative` — both parts, always, and in this order. `AlternateViews` is ordered
            // least-preferred first: a client picks the LAST one it can render, so appending the plain text
            // after the HTML would hand every graphical client the unstyled version. Sending the HTML *alone*
            // is the other failure — a reader with HTML refused gets raw tags or an empty message.
            var plain = AlternateView.CreateAlternateViewFromString(
                plainText, Encoding.UTF8, MediaTypeNames.Text.Plain);
            var html = AlternateView.CreateAlternateViewFromString(
                EmailLayout.Html(content, _appUrl.BaseUrl), Encoding.UTF8, MediaTypeNames.Text.Html);
            mail.AlternateViews.Add(plain);
            mail.AlternateViews.Add(html);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SendTimeout);

            await client.SendMailAsync(mail, timeout.Token);
            return TransactionalEmailResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Neither the recipient nor the body is logged: the body carries a live verification link, and the
            // address is the one thing an enumeration attempt would want confirmed in a log.
            _logger.LogWarning(ex, "Transactional SMTP send failed.");
            return TransactionalEmailResult.Failed(ex.Message);
        }
    }
}
