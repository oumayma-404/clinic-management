using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Repositories;

/// <summary>Unread messages of one thread from one sender — the grain the FR-2 « who rings » rule is applied at.</summary>
public sealed record StaffUnreadCount(string ConversationKey, string SenderUserId, int Count, int UrgentCount, DateTime LatestSentAt);

/// <summary>What a message's appointment link shows (FR-3): when, whose, and whether it still stands.</summary>
public sealed record StaffLinkedAppointment(
    Guid Id, Guid? PatientId, Guid? DoctorId, DateTime StartsAt, AppointmentStatus Status, bool Disregarded);

/// <summary>The latest « Patient arrivé » sent for one appointment — what « Signalé à 09:32 par Salma » reads (AC-3.3).</summary>
public sealed record StaffArrival(Guid AppointmentId, DateTime SentAt, string SenderUserId);

public interface IStaffMessageRepository
{
    Task AddAsync(StaffMessage message, CancellationToken cancellationToken = default);

    Task<StaffMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Newest first, strictly older than the (sentAt, id) cursor when one is given.</summary>
    Task<IReadOnlyList<StaffMessage>> GetPageAsync(
        Guid clinicId, string conversationKey, DateTime? beforeSentAt, Guid? beforeId, int take,
        CancellationToken cancellationToken = default);

    /// <summary>The latest message of each of <paramref name="conversationKeys"/> that has one.</summary>
    Task<IReadOnlyList<StaffMessage>> GetLatestPerConversationAsync(
        Guid clinicId, IReadOnlyCollection<string> conversationKeys, CancellationToken cancellationToken = default);

    /// <summary>What <paramref name="viewerUserId"/> has not read, newer than <paramref name="floorUtc"/>, grouped by thread and sender.</summary>
    Task<IReadOnlyList<StaffUnreadCount>> GetUnreadCountsAsync(
        Guid clinicId, string viewerUserId, IReadOnlyCollection<string> conversationKeys, DateTime floorUtc,
        CancellationToken cancellationToken = default);

    /// <summary>The newest unread messages for <paramref name="viewerUserId"/>, newest first.</summary>
    Task<IReadOnlyList<StaffMessage>> GetRecentUnreadAsync(
        Guid clinicId, string viewerUserId, IReadOnlyCollection<string> conversationKeys, DateTime floorUtc, int take,
        CancellationToken cancellationToken = default);

    /// <summary>How many times <paramref name="senderUserId"/> sent each quick message since <paramref name="sinceUtc"/> (AC-2.2).</summary>
    Task<IReadOnlyDictionary<string, int>> GetQuickMessageUsageAsync(
        Guid clinicId, string senderUserId, DateTime sinceUtc, CancellationToken cancellationToken = default);

    Task<StaffChatReadMarker?> GetMarkerAsync(
        Guid clinicId, string userId, string conversationKey, CancellationToken cancellationToken = default);

    Task AddMarkerAsync(StaffChatReadMarker marker, CancellationToken cancellationToken = default);

    /// <summary>Every member's read marker for one thread — what « Vu par … » is computed from (AC-5.4).</summary>
    Task<IReadOnlyList<StaffChatReadMarker>> GetMarkersAsync(
        Guid clinicId, string conversationKey, CancellationToken cancellationToken = default);

    /// <summary>The appointments some messages link to, in this cabinet only; a removed one is simply absent.</summary>
    Task<IReadOnlyList<StaffLinkedAppointment>> GetLinkedAppointmentsAsync(
        Guid clinicId, IReadOnlyCollection<Guid> appointmentIds, CancellationToken cancellationToken = default);

    /// <summary>The cabinet's appointments starting between two instants, removed ones left out — today's, for the arrival action.</summary>
    Task<IReadOnlyList<StaffLinkedAppointment>> GetAppointmentsBetweenAsync(
        Guid clinicId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The latest arrival per appointment sent between two instants, in any conversation (AC-3.3).</summary>
    Task<IReadOnlyList<StaffArrival>> GetArrivalsAsync(
        Guid clinicId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>The latest arrival sent for one appointment, by anyone, in any conversation (EC-5, EC-15).</summary>
    Task<StaffArrival?> GetLastArrivalAsync(Guid clinicId, Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>Who holds a message now, read past this request's own tracked copy — the answer after a lost claim race.</summary>
    Task<string?> GetClaimedByAsync(Guid clinicId, Guid messageId, CancellationToken cancellationToken = default);
}
