using ClinicManagement.Infrastructure.Http;
using ClinicManagement.Application.Common.Interfaces;
using System.Net;
using System.Net.Mail;
using ClinicManagement.Application.Common.Models;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// SMTP implementation of <see cref="IDocumentEmailSender"/>, over the framework's own
/// <see cref="System.Net.Mail.SmtpClient"/> — deliberately no new package. The offline-LAN install ships a
/// bundled payload, so a dependency that buys nothing here is a dependency the installer has to carry; STARTTLS,
/// authentication and a single PDF attachment are all this needs and all the framework client is asked to do.
/// <para>
/// Never throws: every failure is classified into a <see cref="DocumentEmailSendResult"/> so the dispatcher can
/// decide between retrying and giving up. A permanent SMTP rejection (a bad mailbox) is still reported as
/// transient — the retry cap bounds it, and misreading a greylisting delay as permanent would silently drop a
/// document the practitioner believes was sent.
/// </para>
/// </summary>
public class SmtpDocumentEmailSender : IDocumentEmailSender
{
    // Bounded so one unreachable host cannot hold the dispatch tick open; the row stays queued and retries.
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<SmtpDocumentEmailSender> _logger;

    private readonly IOutboundEndpointPolicy _endpointPolicy;

    public SmtpDocumentEmailSender(
        ILogger<SmtpDocumentEmailSender> logger, IOutboundEndpointPolicy endpointPolicy)
    {
        _logger = logger;
        _endpointPolicy = endpointPolicy;
    }

    public async Task<DocumentEmailSendResult> SendAsync(
        DocumentEmailMessage message,
        ResolvedReminderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.EmailConfigured)
        {
            return DocumentEmailSendResult.NotConfigured;
        }

        try
        {
            // ⚠️ SMTP is the SSRF path with NO compensating accident. The HTTP integrations are protected by
            // https being forced — a private service rarely speaks TLS with a valid certificate — while this
            // dials a bare host on a bare port, so a tenant naming `127.0.0.1.nip.io` reaches the API
            // container's own loopback. `SmtpClient` has no connect callback to hang the check on, so the
            // resolution happens here, immediately before the connect.
            await PublicEgressGuard.EnsureHostResolvesPublicAsync(
                settings.SmtpHost!, _endpointPolicy.AllowsPrivateNetworkEndpoints, cancellationToken);

            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.SmtpUseTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = (int)SendTimeout.TotalMilliseconds
            };

            // An unauthenticated relay is a real clinic-LAN deployment, so credentials are optional: only
            // attach them when a username was actually resolved.
            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword ?? string.Empty);
            }

            var from = string.IsNullOrWhiteSpace(settings.SmtpFromName)
                ? new MailAddress(settings.SmtpFromAddress)
                : new MailAddress(settings.SmtpFromAddress, settings.SmtpFromName);

            using var mail = new MailMessage
            {
                From = from,
                Subject = message.Subject,
                Body = message.Body,
                IsBodyHtml = false
            };
            mail.To.Add(new MailAddress(message.RecipientEmail));

            using var attachmentStream = new MemoryStream(message.Attachment, writable: false);
            mail.Attachments.Add(new Attachment(attachmentStream, message.AttachmentFileName, "application/pdf"));

            await client.SendMailAsync(mail, cancellationToken);
            return DocumentEmailSendResult.Sent;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A host shutdown mid-tick is not the row's fault — leave it queued.
            throw;
        }
        catch (Exception ex)
        {
            // PII: the recipient address is not logged — the row id is enough to find it.
            // FR-4.4 — `DocumentFileNaming` composes this from the patient's name, so the stem is PHI. The
            // extension is the only part that ever diagnosed anything.
            _logger.LogWarning(
                ex, "SMTP send failed for a document email attachment ({FileName})",
                LogMask.FileName(message.AttachmentFileName));
            return DocumentEmailSendResult.Transient(ex.Message);
        }
    }
}
