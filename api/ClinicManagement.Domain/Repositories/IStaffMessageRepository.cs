using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Domain.Repositories;

/// <summary>Unread messages of one thread from one sender — the grain the FR-2 « who rings » rule is applied at.</summary>
public sealed record StaffUnreadCount(string ConversationKey, string SenderUserId, int Count, int UrgentCount, DateTime LatestSentAt);

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

    Task<StaffChatReadMarker?> GetMarkerAsync(
        Guid clinicId, string userId, string conversationKey, CancellationToken cancellationToken = default);

    Task AddMarkerAsync(StaffChatReadMarker marker, CancellationToken cancellationToken = default);
}
