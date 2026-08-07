using ClinicManagement.Application.Common.Models;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>The outcome of a single document-email send attempt. Mirrors <see cref="ReminderSendOutcome"/>.</summary>
public enum DocumentEmailSendOutcome
{
    /// <summary>Accepted by the SMTP server → the row is <c>Sent</c>.</summary>
    Sent,

    /// <summary>A transient SMTP/network failure → leave the row <c>Queued</c> and retry on a later tick.</summary>
    TransientFailure,

    /// <summary>The SMTP settings are incomplete → send nothing (no retry spam against a missing host).</summary>
    NotConfigured
}

/// <summary>Result of a document-email send, carrying the reason for a transient failure.</summary>
public sealed record DocumentEmailSendResult(DocumentEmailSendOutcome Outcome, string? Error)
{
    public static readonly DocumentEmailSendResult Sent = new(DocumentEmailSendOutcome.Sent, null);
    public static readonly DocumentEmailSendResult NotConfigured = new(DocumentEmailSendOutcome.NotConfigured, null);
    public static DocumentEmailSendResult Transient(string error) => new(DocumentEmailSendOutcome.TransientFailure, error);
}

/// <summary>One outbound document email: the recipient, the wording, and the PDF to attach.</summary>
public sealed record DocumentEmailMessage(
    string RecipientEmail,
    string Subject,
    string Body,
    byte[] Attachment,
    string AttachmentFileName);

/// <summary>
/// Sends one document email over SMTP, reading host/port/TLS/credentials/from-identity from the
/// <paramref name="settings"/> resolved for the row's clinic (per-clinic override else per-install) — never from
/// config directly, exactly like <see cref="IReminderChannelSender"/>.
/// </summary>
public interface IDocumentEmailSender
{
    Task<DocumentEmailSendResult> SendAsync(
        DocumentEmailMessage message,
        ResolvedReminderSettings settings,
        CancellationToken cancellationToken = default);
}
