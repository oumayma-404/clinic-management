using ClinicManagement.Domain.Common;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The « Rappels » page in one read: a page of the delivery log plus the three clinic-wide counters above it.
/// </summary>
/// <param name="Page">
/// The filtered, ordered page of outbox rows — newest due first.
/// </param>
/// <param name="SentToday">Sent during the clinic's <b>local</b> day.</param>
/// <param name="Pending">Queued and not yet resolved, of any age.</param>
/// <param name="FailedRecent">
/// Failed within <see cref="Queries.GetClinicReminderLogQuery.FailedWindowDays"/> days.
/// <para>Not "today", deliberately: a send that failed at 23:00 would drop out of the counter at midnight, before
/// anyone arrived to see it. A failure is the one counter here that must survive the night.</para>
/// </param>
/// <param name="Blocked">
/// Queued but not sendable — the channel is off, unconfigured or unimplemented (L3a).
/// <para>The counter this whole status exists for. A queue that silently stops sending is the defect; « N rappels
/// bloqués » with the reason beside each row is what makes it a thing someone can fix. Unbounded by date, like
/// <paramref name="Pending"/>: the oldest blocked row is the one most worth noticing.</para>
/// </param>
public record ReminderLogDto(
    PagedResult<ReminderStatusDto> Page,
    int SentToday,
    int Pending,
    int FailedRecent,
    int Blocked);
