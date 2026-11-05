using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.Application.Features.Team;

/// <summary>One person's today, shared by the desk read and the borne's tiles so « présente » means one thing.</summary>
public static class TeamTodayRules
{
    /// <summary>« in » with an open line today; « leave » on a whole-day congé; otherwise « out » (a half day included).</summary>
    public static TeamTodayPersonDto PersonToday(
        User user, IEnumerable<AttendanceEntry> entries, IEnumerable<StaffLeaveRequest> acceptedLeaves, DateOnly today)
    {
        var own = entries.Where(e => e.UserId == user.Id).ToList();
        var openToday = own.Where(e => e.IsOpen && e.LocalDay == today).OrderByDescending(e => e.ArrivalUtc).FirstOrDefault();
        var leave = AttendanceRules.LeaveCovering(user.Id, acceptedLeaves, today);
        var missing = own.Where(e => e.IsOpen && e.LocalDay < today).OrderByDescending(e => e.LocalDay).FirstOrDefault();

        return new TeamTodayPersonDto
        {
            UserId = user.Id,
            FullName = StaffLeaveRules.DisplayName(user),
            State = openToday is not null ? AttendanceRules.StateIn
                : leave is { HalfDay: false } ? AttendanceRules.StateLeave
                : AttendanceRules.StateOut,
            Since = openToday is not null ? AttendanceRules.LocalTime(openToday.ArrivalUtc) : null,
            LeaveTypeLabel = leave is not null ? StaffLeaveRules.TypeLabel(leave.Type) : null,
            LeaveHalfDay = leave?.HalfDay == true,
            MissingDepartureOn = missing?.LocalDay,
        };
    }
}
