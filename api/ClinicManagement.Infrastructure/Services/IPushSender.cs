using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>What one push send attempt resolved to.</summary>
public enum PushSendOutcome
{
    /// <summary>FCM/APNs accepted it → the row is <c>Sent</c>.</summary>
    Sent,

    /// <summary>A transient network or gateway failure → keep <c>Pending</c> and retry on a later tick.</summary>
    TransientFailure,

    /// <summary>
    /// The platform says this token is not registered any more — the app was uninstalled, or the token was
    /// replaced (AC-49).
    ///
    /// <para><b>This outcome is load-bearing, which is why it is not folded into a failure.</b> It is terminal
    /// <i>per device</i>, not per message: retrying is pointless for this row <b>and</b> for every future one, so
    /// the dispatcher fails the row <i>and</i> deactivates the registration behind it. Treated as a transient
    /// failure it would burn the retry budget of every notification for a phone that no longer exists, for ever.</para>
    /// </summary>
    TokenInvalid,

    /// <summary>This platform's credentials are absent → send nothing, park the row (AC-50).</summary>
    NotConfigured
}

/// <summary>The outcome plus the reason a row records and the « Rappels »-style surface would show.</summary>
public sealed record PushSendResult(PushSendOutcome Outcome, string? Error)
{
    public static readonly PushSendResult Sent = new(PushSendOutcome.Sent, null);
    public static readonly PushSendResult NotConfigured = new(PushSendOutcome.NotConfigured, null);
    public static PushSendResult Transient(string error) => new(PushSendOutcome.TransientFailure, error);
    public static PushSendResult TokenInvalid(string error) => new(PushSendOutcome.TokenInvalid, error);
}

/// <summary>
/// One notification, as much of it as leaves the building. There is no message body and no recipient name — a
/// <see cref="Label"/> that is a fixed French category phrase and ids for routing, and that is the whole payload
/// (AC-47).
/// </summary>
public sealed record PushMessage(
    string Token,
    string Label,
    NotificationCategory Category,
    Guid? AppointmentId);

/// <summary>
/// Platform-generic sender for one push. Matched to a queued row by its <see cref="Platform"/>, exactly as
/// <see cref="IReminderChannelSender"/> is matched by its channel.
///
/// <para>Credentials arrive as a parameter rather than being read from configuration, so « is this platform
/// sendable? » has one answer shared by the enqueue gate, the dispatcher and the settings badge.</para>
///
/// <para><b>Never throws.</b> A push is a post-commit side effect of a clinical or financial operation that has
/// already succeeded (AC-55), so every failure is a returned outcome.</para>
/// </summary>
public interface IPushSender
{
    DevicePlatform Platform { get; }

    Task<PushSendResult> SendAsync(
        PushMessage message, ResolvedPushCredentials credentials, CancellationToken cancellationToken = default);
}
