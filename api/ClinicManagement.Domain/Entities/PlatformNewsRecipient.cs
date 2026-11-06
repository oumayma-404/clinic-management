using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>Who one news broadcast was addressed to, and what happened. The cabinet name is a snapshot, not a link.</summary>
public sealed record PlatformNewsRecipientInput(Guid ClinicId, string ClinicName, string Email, string? FullName);

public class PlatformNewsRecipient : Entity<Guid>
{
    public const int ErrorMaxLength = 500;

    public Guid BroadcastId { get; private set; }
    public Guid ClinicId { get; private set; }
    public string ClinicName { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string? FullName { get; private set; }
    public PlatformNewsDeliveryStatus Status { get; private set; }
    public string? Error { get; private set; }
    public DateTime? AttemptedAtUtc { get; private set; }

    private PlatformNewsRecipient() { } // For EF Core

    internal PlatformNewsRecipient(Guid broadcastId, PlatformNewsRecipientInput input)
        : base(Guid.NewGuid())
    {
        BroadcastId = broadcastId;
        ClinicId = input.ClinicId;
        ClinicName = input.ClinicName;
        Email = input.Email;
        FullName = input.FullName;
        Status = PlatformNewsDeliveryStatus.Pending;
    }

    internal void MarkSending(DateTime nowUtc)
    {
        if (Status != PlatformNewsDeliveryStatus.Pending)
        {
            throw new InvalidOperationException("Ce destinataire a déjà été traité.");
        }

        Status = PlatformNewsDeliveryStatus.Sending;
        AttemptedAtUtc = nowUtc;
    }

    internal void MarkSent(DateTime nowUtc)
    {
        Status = PlatformNewsDeliveryStatus.Sent;
        Error = null;
        AttemptedAtUtc = nowUtc;
    }

    internal void MarkFailed(string error, DateTime nowUtc)
    {
        Status = PlatformNewsDeliveryStatus.Failed;
        Error = error.Length > ErrorMaxLength ? error[..ErrorMaxLength] : error;
        AttemptedAtUtc = nowUtc;
    }
}
