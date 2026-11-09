using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.News;

/// <summary>The « Nouveautés » page in the app: every news the console sent and did not hide, newest first.</summary>
public sealed record NewsFeedDto(int Unread, IReadOnlyList<NewsEntryDto> Entries);

/// <param name="SentOn">The Tunisian day it was sent (<c>yyyy-MM-dd</c>) — what the page prints and groups by month.</param>
public sealed record NewsEntryDto(
    Guid Id,
    string Subject,
    DateTime SentAt,
    string SentOn,
    bool IsUnread,
    IReadOnlyList<NewsItemDto> Items);

public sealed record NewsItemDto(string Emoji, string Text);

/// <summary>The sidebar's badge: how many news this admin has not seen yet.</summary>
public sealed record NewsSummaryDto(int Unread);

public static class NewsReading
{
    /// <summary>The signed-in account, or null when the token names nobody this server knows.</summary>
    public static async Task<User?> ResolveCallerAsync(
        IClinicContext clinicContext, IUserRepository users, CancellationToken cancellationToken)
    {
        var userId = clinicContext.GetUserId();
        return string.IsNullOrWhiteSpace(userId) ? null : await users.GetByAuth0SubAsync(userId, cancellationToken);
    }

    /// <summary>
    /// What this admin has read up to. With no marker yet, the account's creation: a new admin finds the whole history
    /// on the page without a badge claiming years of news as unread.
    /// </summary>
    public static DateTime ReadUpTo(PlatformNewsReadMarker? marker, User user) => marker?.ReadUpToUtc ?? user.CreatedAt;

    /// <summary>The one « unread » rule — the summary's count and the page's flags both go through it.</summary>
    public static bool IsUnread(DateTime sentAtUtc, DateTime readUpTo) => sentAtUtc > readUpTo;

    public static NewsEntryDto ToEntry(PlatformNewsAppRow row, DateTime readUpTo) => new(
        row.Id,
        row.Subject,
        row.CreatedAtUtc,
        ClinicClock.ToClinicLocal(row.CreatedAtUtc).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        IsUnread(row.CreatedAtUtc, readUpTo),
        row.Items.Select(i => new NewsItemDto(i.Emoji, i.Text)).ToList());
}
