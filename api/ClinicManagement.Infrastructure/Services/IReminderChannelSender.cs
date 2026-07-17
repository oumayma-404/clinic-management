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
    NotConfigured
}

/// <summary>Result of a reminder send, carrying an error message for the transient-failure case.</summary>
public sealed record ReminderSendResult(ReminderSendOutcome Outcome, string? Error)
{
    public static readonly ReminderSendResult Sent = new(ReminderSendOutcome.Sent, null);
    public static readonly ReminderSendResult NotConfigured = new(ReminderSendOutcome.NotConfigured, null);
    public static ReminderSendResult Transient(string error) => new(ReminderSendOutcome.TransientFailure, error);
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
