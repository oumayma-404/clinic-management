namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>The outcome of one transactional-email attempt.</summary>
public enum TransactionalEmailOutcome
{
    Sent,

    /// <summary>The install has no SMTP host configured — nothing was attempted, and nothing will be.</summary>
    NotConfigured,

    /// <summary>The host was reachable-ish and refused, or the network did. Retrying may work.</summary>
    Failed
}

/// <summary>
/// Result of a transactional send, carrying the operator-facing reason for a failure. Never an exception: the
/// caller has a French refusal to compose either way, and a 500 over a mail server hiccup tells the visitor
/// nothing they can act on.
/// </summary>
public sealed record TransactionalEmailResult(TransactionalEmailOutcome Outcome, string? Error)
{
    public static readonly TransactionalEmailResult Sent = new(TransactionalEmailOutcome.Sent, null);
    public static readonly TransactionalEmailResult NotConfigured = new(TransactionalEmailOutcome.NotConfigured, null);
    public static TransactionalEmailResult Failed(string error) => new(TransactionalEmailOutcome.Failed, error);
}

/// <summary>
/// Sends one plain-text email that belongs to <b>no clinic</b> — the first such path in the product.
///
/// <para>⚠️ <b>It reads the per-install <c>Notification:Smtp:*</c> settings, deliberately, and must keep doing
/// so.</b> Every other outbound email here goes through <c>IDocumentEmailSender</c>, which takes a
/// <c>ResolvedReminderSettings</c> — and those are resolved <i>per clinic</i>. A clinic self-signup has no
/// clinic, so there is nothing to resolve against: routing this through <c>IReminderSettingsProvider</c> would
/// look tidier and would stop working for the one caller that exists. That is the whole reason this interface is
/// separate rather than an overload of the document sender.</para>
///
/// <para>⚠️ <b>Deliberately not an outbox queue either.</b> Every queue in this product keys on
/// <c>ClinicId</c> — the reminder outbox, the document-email outbox — and a verification
/// email is not a background dispatch: the visitor is sitting in front of the form waiting for it, so a failure
/// has to reach them as a refusal they can retry (AC-15), not as a row in a table nobody will look at.</para>
/// </summary>
public interface ITransactionalEmailSender
{
    /// <summary>
    /// Whether this install can send at all. Asked <b>before</b> anything is written, so the visitor gets a
    /// French refusal naming the missing configuration instead of a 202 over an email that will never arrive.
    /// </summary>
    bool IsConfigured { get; }

    Task<TransactionalEmailResult> SendAsync(
        string recipientEmail,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
