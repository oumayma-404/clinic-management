using Microsoft.EntityFrameworkCore;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Persistence;

namespace ClinicManagement.Infrastructure.Repositories;

public class StaffMessageRepository : IStaffMessageRepository
{
    private readonly ApplicationDbContext _context;

    public StaffMessageRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(StaffMessage message, CancellationToken cancellationToken = default)
    {
        await _context.StaffMessages.AddAsync(message, cancellationToken);
    }

    public async Task<StaffMessage?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StaffMessages.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMessage>> GetPageAsync(
        Guid clinicId, string conversationKey, DateTime? beforeSentAt, Guid? beforeId, int take,
        CancellationToken cancellationToken = default)
    {
        return await PageQuery(_context, clinicId, conversationKey, beforeSentAt, beforeId)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMessage>> GetLatestPerConversationAsync(
        Guid clinicId, IReadOnlyCollection<string> conversationKeys, CancellationToken cancellationToken = default)
    {
        if (conversationKeys.Count == 0)
        {
            return Array.Empty<StaffMessage>();
        }

        return await LatestQuery(_context, clinicId, conversationKeys)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffUnreadCount>> GetUnreadCountsAsync(
        Guid clinicId, string viewerUserId, IReadOnlyCollection<string> conversationKeys, DateTime floorUtc,
        CancellationToken cancellationToken = default)
    {
        if (conversationKeys.Count == 0)
        {
            return Array.Empty<StaffUnreadCount>();
        }

        return await UnreadQuery(_context, clinicId, viewerUserId, conversationKeys, floorUtc)
            .GroupBy(m => new { m.ConversationKey, m.SenderUserId })
            .Select(g => new StaffUnreadCount(
                g.Key.ConversationKey,
                g.Key.SenderUserId,
                g.Count(),
                g.Count(m => m.IsUrgent),
                g.Max(m => m.SentAt)))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMessage>> GetRecentUnreadAsync(
        Guid clinicId, string viewerUserId, IReadOnlyCollection<string> conversationKeys, DateTime floorUtc, int take,
        CancellationToken cancellationToken = default)
    {
        if (conversationKeys.Count == 0)
        {
            return Array.Empty<StaffMessage>();
        }

        return await UnreadQuery(_context, clinicId, viewerUserId, conversationKeys, floorUtc)
            .OrderByDescending(m => m.SentAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, int>> GetQuickMessageUsageAsync(
        Guid clinicId, string senderUserId, DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        return await UsageQuery(_context, clinicId, senderUserId, sinceUtc)
            .ToDictionaryAsync(u => u.Key, u => u.Count, StringComparer.Ordinal, cancellationToken);
    }

    public async Task<StaffChatReadMarker?> GetMarkerAsync(
        Guid clinicId, string userId, string conversationKey, CancellationToken cancellationToken = default)
    {
        return await _context.StaffChatReadMarkers.FirstOrDefaultAsync(
            r => r.ClinicId == clinicId && r.UserId == userId && r.ConversationKey == conversationKey,
            cancellationToken);
    }

    public async Task AddMarkerAsync(StaffChatReadMarker marker, CancellationToken cancellationToken = default)
    {
        await _context.StaffChatReadMarkers.AddAsync(marker, cancellationToken);
    }

    public async Task<IReadOnlyList<StaffChatReadMarker>> GetMarkersAsync(
        Guid clinicId, string conversationKey, CancellationToken cancellationToken = default)
    {
        return await _context.StaffChatReadMarkers
            .Where(r => r.ClinicId == clinicId && r.ConversationKey == conversationKey)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetClaimedByAsync(Guid clinicId, Guid messageId, CancellationToken cancellationToken = default)
    {
        return await _context.StaffMessages
            .Where(m => m.ClinicId == clinicId && m.Id == messageId)
            .AsNoTracking()
            .Select(m => m.ClaimedByUserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>One sender's quick-message counts since a moment — what orders their buttons (AC-2.2). Public for the SQL test.</summary>
    public static IQueryable<StaffQuickMessageUsage> UsageQuery(
        ApplicationDbContext db, Guid clinicId, string senderUserId, DateTime sinceUtc) =>
        db.StaffMessages
            .Where(m => m.ClinicId == clinicId
                && m.SenderUserId == senderUserId
                && m.SentAt >= sinceUtc
                && m.QuickMessageKey != null)
            .GroupBy(m => m.QuickMessageKey!)
            .Select(g => new StaffQuickMessageUsage(g.Key, g.Count()));

    /// <summary>One thread, newest first, keyset-paged on (SentAt, Id). Public so the SQL test compiles what ships.</summary>
    public static IQueryable<StaffMessage> PageQuery(
        ApplicationDbContext db, Guid clinicId, string conversationKey, DateTime? beforeSentAt, Guid? beforeId)
    {
        var query = db.StaffMessages.Where(m => m.ClinicId == clinicId && m.ConversationKey == conversationKey);

        if (beforeSentAt is { } before && beforeId is { } id)
        {
            query = query.Where(m => m.SentAt < before || (m.SentAt == before && m.Id.CompareTo(id) < 0));
        }

        return query.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id);
    }

    /// <summary>The latest row of each thread. Public so the SQL test compiles what ships.</summary>
    public static IQueryable<StaffMessage> LatestQuery(
        ApplicationDbContext db, Guid clinicId, IReadOnlyCollection<string> conversationKeys) =>
        db.StaffMessages
            .Where(m => m.ClinicId == clinicId && conversationKeys.Contains(m.ConversationKey))
            .GroupBy(m => m.ConversationKey)
            .Select(g => g.OrderByDescending(m => m.SentAt).ThenByDescending(m => m.Id).First());

    /// <summary>
    /// The single definition of « unread for this viewer »: another person's message, newer than both the viewer's
    /// read marker for that thread and <paramref name="floorUtc"/> (the account's creation — the bell's late-joiner rule).
    /// </summary>
    public static IQueryable<StaffMessage> UnreadQuery(
        ApplicationDbContext db, Guid clinicId, string viewerUserId, IReadOnlyCollection<string> conversationKeys,
        DateTime floorUtc) =>
        db.StaffMessages.Where(m =>
            m.ClinicId == clinicId
            && conversationKeys.Contains(m.ConversationKey)
            && m.SenderUserId != viewerUserId
            && m.SentAt > floorUtc
            && !db.StaffChatReadMarkers.Any(r =>
                r.ClinicId == clinicId
                && r.UserId == viewerUserId
                && r.ConversationKey == m.ConversationKey
                && r.LastReadAt >= m.SentAt));
}

/// <summary>One row of <see cref="StaffMessageRepository.UsageQuery"/>.</summary>
public sealed record StaffQuickMessageUsage(string Key, int Count);
