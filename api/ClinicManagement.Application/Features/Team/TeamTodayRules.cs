using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Team;

/// <summary>One person's today, shared by the desk read and the borne's tiles so a state means one thing everywhere.</summary>
public static class TeamTodayRules
{
    /// <summary>
    /// « in » with an open line today; « left » once today's lines are all closed (a new tap is a new arrival); « leave »
    /// on a whole-day congé; otherwise « out » (a half day included).
    /// </summary>
    public static TeamTodayPersonDto PersonToday(
        User user, IEnumerable<AttendanceEntry> entries, IEnumerable<StaffLeaveRequest> acceptedLeaves, DateTime nowUtc)
    {
        var today = AttendanceRules.Today(nowUtc);
        var own = entries.Where(e => e.UserId == user.Id).ToList();
        var todays = own.Where(e => e.LocalDay == today).ToList();
        var openToday = todays.Where(e => e.IsOpen).OrderByDescending(e => e.ArrivalUtc).FirstOrDefault();
        var lastDeparture = openToday is null ? todays.Max(e => e.DepartureUtc) : null;
        var leave = AttendanceRules.LeaveCovering(user.Id, acceptedLeaves, today);
        var missing = own.Where(e => e.IsOpen && e.LocalDay < today).OrderByDescending(e => e.LocalDay).FirstOrDefault();

        return new TeamTodayPersonDto
        {
            UserId = user.Id,
            FullName = StaffLeaveRules.DisplayName(user),
            State = openToday is not null ? AttendanceRules.StateIn
                : lastDeparture is not null ? AttendanceRules.StateLeft
                : leave is { HalfDay: false } ? AttendanceRules.StateLeave
                : AttendanceRules.StateOut,
            Since = openToday is not null ? AttendanceRules.LocalTime(openToday.ArrivalUtc) : null,
            LeftAt = lastDeparture is { } left ? AttendanceRules.LocalTime(left) : null,
            LeaveTypeLabel = leave is not null ? StaffLeaveRules.TypeLabel(leave.Type) : null,
            LeaveHalfDay = leave?.HalfDay == true,
            MissingDepartureOn = missing?.LocalDay,
            NeedsSchema = !user.HasAttendanceSchema,
            SchemaLockedUntil = user.IsSchemaLocked(nowUtc) ? user.SchemaLockedUntilUtc : null,
        };
    }

    /// <summary>« Équipe aujourd'hui »'s order: here, expected, gone for the day, on congé.</summary>
    public static int Rank(string state) => state switch
    {
        AttendanceRules.StateIn => 0,
        AttendanceRules.StateOut => 1,
        AttendanceRules.StateLeft => 2,
        _ => 3,
    };
}
