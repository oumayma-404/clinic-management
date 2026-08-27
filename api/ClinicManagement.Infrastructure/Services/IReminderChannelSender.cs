using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>The outcome of a single reminder send attempt.</summary>
public enum ReminderSendOutcome
{
    /// <summary>Delivered to the gateway successfully → the reminder is <c>Sent</c>.</summary>
    Sent,

    /// <summary>A transient send/gateway failure → leave <c>Pending</c> and retry on a later tick.</summary>
    TransientFailure,

    /// <summary>The channel is enabled but its credentials/template are missing → send nothing (no failure spam).</summary>
    NotConfigured,

    /// <summary>
    /// The provider is rate-limiting us (FR-8). The row stays <c>Pending</c> and is retried on a later tick, and it
    /// <b>consumes no retry budget</b>: nothing is wrong with the reminder, so spending its three attempts on our
    /// own throughput would fail a message that would have gone out fine a minute later.
    /// </summary>
    Throttled,

    /// <summary>
    /// The provider has stopped this sender and a retry cannot change that (FR-8, EC-11). The row is <b>parked</b>
    /// under the reason on the result — held, not failed — so it goes out if the situation is resolved.
    /// </summary>
    Blocked
}

/// <summary>
/// Result of a reminder send, carrying an error message for the transient-failure case.
///
/// <para>⚠️ <b>Nothing the provider returned may reach this record.</b> The response body used to, truncated to 200
/// bytes, and that string is persisted on the outbox row and served back to the clinic by <c>reminder-status</c> and
/// <c>reminder-log</c> — the latter readable by <i>any</i> clinic role. Since the endpoint URL is tenant-supplied,
/// that turned a settings field into a read primitive (D-8). Every message below is written by us.</para>
/// </summary>
public sealed record ReminderSendResult(ReminderSendOutcome Outcome, string? Error, OutboxBlockReason? BlockReason = null)
{
    public static readonly ReminderSendResult Sent = new(ReminderSendOutcome.Sent, null);
    public static readonly ReminderSendResult NotConfigured = new(ReminderSendOutcome.NotConfigured, null);
    public static ReminderSendResult Transient(string error) => new(ReminderSendOutcome.TransientFailure, error);

    /// <summary><paramref name="error"/> is ours, not the provider's — see the ⚠️ on the type.</summary>
    public static ReminderSendResult Throttled(string error) => new(ReminderSendOutcome.Throttled, error);

    public static ReminderSendResult Blocked(OutboxBlockReason reason, string sentence) =>
        new(ReminderSendOutcome.Blocked, sentence, reason);
}

/// <summary>
/// Channel-generic sender for one reminder message. Implementations are matched to a reminder row by their
/// <see cref="Channel"/> (the row's <c>NotificationType</c>). The phone is already normalized to E.164 and
/// the message is pre-rendered by the enqueuer; a sender only performs the outbound call, reading its endpoint,
/// sender identity, secret and template from the <paramref name="settings"/> resolved for the row's clinic
/// (per-clinic override or the per-install fallback) — never from config directly.
/// </summary>
public interface IReminderChannelSender
{
    NotificationType Channel { get; }

    Task<ReminderSendResult> SendAsync(
        string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default);
}
