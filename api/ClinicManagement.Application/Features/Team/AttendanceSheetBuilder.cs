using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// One month of pointage as the sheet reads it: a row per day, each person's lines, closed days, accepted congés and
/// the month's totals. Shared by the admin's « Présences », a secretary's file and « Mes présences », so the three
/// cannot count a month differently.
/// </summary>
public static class AttendanceSheetBuilder
{
    public static AttendanceSheetDto Build(
        string monthKey, DateOnly from, DateOnly to, IReadOnlyList<User> people, IReadOnlyList<AttendanceEntry> entries,
        IEnumerable<StaffLeaveRequest> acceptedLeaves, IReadOnlyDictionary<string, User> roster, string? workingHoursJson,
        DateOnly today, DateOnly? firstLineDay = null)
    {
        var peopleIds = people.Select(p => p.Id).ToHashSet();
        var leaves = acceptedLeaves
            .Where(l => l.Status == StaffLeaveStatus.Approved && peopleIds.Contains(l.UserId))
            .ToList();
        var byDay = entries.ToLookup(e => e.LocalDay);

        var days = new List<AttendanceDayDto>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            days.Add(new AttendanceDayDto
            {
                Day = day,
                IsOpenDay = AttendanceRules.IsOpenDay(day, workingHoursJson),
                IsToday = day == today,
                Entries = byDay[day].Select(e => AttendanceRules.ToDto(e, roster, today)).ToList(),
                Leaves = leaves
                    .Where(l => l.StartDate <= day && day <= l.EndDate)
                    .Select(l => new AttendanceLeaveMarkDto
                    {
                        UserId = l.UserId,
                        TypeLabel = StaffLeaveRules.TypeLabel(l.Type),
                        HalfDay = l.HalfDay,
                    })
                    .ToList(),
            });
        }

        return new AttendanceSheetDto
        {
            Month = monthKey,
            MonthLabel = ClinicClock.MonthLabelFr(monthKey),
            People = people
                .Select(p => new AttendancePersonDto
                {
                    UserId = p.Id,
                    FullName = StaffLeaveRules.DisplayName(p),
                    IsActive = p.IsActive,
                    CountsFrom = CountsFrom(firstLineDay, p),
                })
                .ToList(),
            Days = days,
            Totals = people.Select(p => AttendanceRules.Totals(p.Id, entries, today)).ToList(),
        };
    }

    /// <summary>Before the cabinet's first line, or before her account existed, an empty day is not an absence.</summary>
    public static DateOnly? CountsFrom(DateOnly? firstLineDay, User person)
    {
        if (firstLineDay is not { } first)
            return null;
        var created = AttendanceRules.LocalDayOf(person.CreatedAt);
        return created > first ? created : first;
    }

    /// <summary>Who the sheet has a column for: everyone who pointe today, plus anyone with a line in the month.</summary>
    public static List<User> PeopleFor(IReadOnlyDictionary<string, User> roster, IEnumerable<AttendanceEntry> entries)
    {
        var withLines = entries.Select(e => e.UserId).ToHashSet();
        return roster.Values
            .Where(u => (u.IsActive && u.IsAttendanceTracked) || withLines.Contains(u.Id))
            .OrderBy(u => StaffLeaveRules.DisplayName(u), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(u => u.Id, StringComparer.Ordinal)
            .ToList();
    }
}
