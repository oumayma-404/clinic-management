using ClinicManagement.Application.Common;
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
    /// <summary>
    /// What this admin has read up to. With no marker yet, the account's creation: a new admin finds the whole history
    /// on the page without a badge claiming years of news as unread.
    /// </summary>
    public static DateTime ReadUpTo(PlatformNewsReadMarker? marker, User user) => marker?.ReadUpToUtc ?? user.CreatedAt;

    public static NewsEntryDto ToEntry(PlatformNewsAppRow row, DateTime readUpTo) => new(
        row.Id,
        row.Subject,
        row.CreatedAtUtc,
        ClinicClock.ToClinicLocal(row.CreatedAtUtc).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        row.CreatedAtUtc > readUpTo,
        row.Items.Select(i => new NewsItemDto(i.Emoji, i.Text)).ToList());
}
