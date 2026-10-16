using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Domain.Entities;

/// <summary>One line of the cabinet's internal chat (staff-chat). Append-only: never edited, never purged (FR-8).</summary>
/// <remarks>An <see cref="Entity{TId}"/> and not an aggregate root, so the audit interceptor does not journal chat.</remarks>
public class StaffMessage : Entity<Guid>
{
    public const int BodyMaxLength = 1000;
    public const int ConversationKeyMaxLength = 600;
    public const int UserIdMaxLength = 255;
    public const int QuickMessageKeyMaxLength = 64;
    public const int QuickMessageLabelMaxLength = 40;

    public Guid ClinicId { get; private set; }
    public string ConversationKey { get; private set; } = string.Empty;

    /// <summary>A plain string, no foreign key: <c>Users</c> is not archived, and a departed colleague's lines stay.</summary>
    public string SenderUserId { get; private set; } = string.Empty;

    public string? Body { get; private set; }
    public string? QuickMessageKey { get; private set; }
    public string? QuickMessageLabel { get; private set; }
    public bool IsUrgent { get; private set; }
    public Guid? PatientId { get; private set; }
    public Guid? AppointmentId { get; private set; }
    public DateTime SentAt { get; private set; }
    public string? ClaimedByUserId { get; private set; }
    public DateTime? ClaimedAt { get; private set; }

    private StaffMessage() { } // For EF Core

    public static StaffMessage Create(
        Guid clinicId,
        StaffConversation conversation,
        string senderUserId,
        string? body,
        StaffQuickMessageSnapshot? quickMessage,
        Guid? patientId,
        Guid? appointmentId,
        DateTime sentAtUtc)
    {
        if (clinicId == Guid.Empty)
        {
            throw new ArgumentException("La clinique est obligatoire.", nameof(clinicId));
        }

        ArgumentNullException.ThrowIfNull(conversation);

        if (string.IsNullOrWhiteSpace(senderUserId))
        {
            throw new ArgumentException("L'expéditeur est obligatoire.", nameof(senderUserId));
        }

        var text = NormalizeBody(body);
        if (text is null && quickMessage is null)
        {
            throw new ArgumentException("Un message vide ne peut pas être envoyé.", nameof(body));
        }

        if (text?.Length > BodyMaxLength)
        {
            throw new ArgumentException($"Un message ne peut pas dépasser {BodyMaxLength} caractères.", nameof(body));
        }

        return new StaffMessage
        {
            Id = Guid.NewGuid(),
            ClinicId = clinicId,
            ConversationKey = conversation.Key,
            SenderUserId = senderUserId,
            Body = text,
            QuickMessageKey = quickMessage?.Key,
            QuickMessageLabel = quickMessage?.Label,
            IsUrgent = quickMessage?.IsUrgent ?? false,
            PatientId = patientId,
            AppointmentId = appointmentId,
            SentAt = sentAtUtc,
        };
    }

    /// <summary>« Je m'en occupe » (FR-6): the first taker wins; taking it again is a no-op, another person is refused.</summary>
    public void Claim(string userId, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("La personne qui prend le message est obligatoire.", nameof(userId));
        }

        if (ClaimedByUserId == userId)
        {
            return;
        }

        if (ClaimedByUserId is not null)
        {
            throw new InvalidOperationException("Ce message est déjà pris par quelqu'un d'autre.");
        }

        ClaimedByUserId = userId;
        ClaimedAt = atUtc;
    }

    /// <summary>« Je ne peux plus » (AC-5.2): only the taker hands it back; an unclaimed message stays as it is.</summary>
    public void ReleaseClaim(string userId)
    {
        if (ClaimedByUserId is null)
        {
            return;
        }

        if (ClaimedByUserId != userId)
        {
            throw new InvalidOperationException("Seule la personne qui a pris ce message peut le rendre.");
        }

        ClaimedByUserId = null;
        ClaimedAt = null;
    }

    /// <summary>Trimmed, with blank collapsed to null; inner line breaks kept (FR-3).</summary>
    public static string? NormalizeBody(string? body)
    {
        var trimmed = body?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
