using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Team;

/// <summary>
/// Where the pointage setup stands — Paramètres' checklist and « Équipe aujourd'hui »'s setup line read it here, and the
/// lock's part comes from <see cref="AttendanceLockRules.LockSetup"/>, so neither can disagree with the 423.
/// </summary>
public static class AttendanceSetupRules
{
    /// <summary>How long after opening a silent borne is worth a line.</summary>
    public static readonly TimeSpan DeadBorneGrace = TimeSpan.FromMinutes(30);

    public const string IssueNoBorne = "no_borne";
    public const string IssueBornePending = "borne_pending";
    public const string IssueNoPunchSinceOpening = "no_punch_since_opening";

    /// <summary>The cabinet's active accounts that pointent.</summary>
    public static List<User> TrackedPeople(IEnumerable<User> roster) =>
        roster.Where(u => u.IsActive && u.IsAttendanceTracked).ToList();

    /// <summary>
    /// A borne may be dead: an open day, 30 min past opening, someone expected (a schéma, no whole-day congé), a borne
    /// live since before opening — and not one line in the cabinet today.
    /// </summary>
    public static bool NoPunchSinceOpening(
        DateTime nowUtc, string? workingHoursJson, IReadOnlyCollection<AttendanceTerminal> terminals,
        IEnumerable<User> trackedPeople, IEnumerable<StaffLeaveRequest> acceptedLeaves, bool anyLineToday)
    {
        if (anyLineToday)
            return false;
        var today = AttendanceRules.Today(nowUtc);
        if (AttendanceLockRules.OpeningUtc(today, workingHoursJson) is not { } opening || nowUtc < opening + DeadBorneGrace)
            return false;
        if (!terminals.Any(t => t.IsLive && (t.ActivatedAtUtc is not { } activated || activated < opening)))
            return false;
        var leaves = acceptedLeaves.ToList();
        return trackedPeople.Any(u => u.HasAttendanceSchema && !AttendanceRules.IsOnFullDayLeave(u.Id, leaves, today));
    }

    /// <summary>The one setup line above « Équipe aujourd'hui »: no borne, one waiting, a silent one — or nothing.</summary>
    public static string? SetupIssue(IReadOnlyCollection<AttendanceTerminal> terminals, bool noPunchSinceOpening) =>
        terminals.Any(t => t.IsLive) ? (noPunchSinceOpening ? IssueNoPunchSinceOpening : null)
        : terminals.Any(t => t.IsPending) ? IssueBornePending
        : IssueNoBorne;

    public static AttendanceSetupDto Setup(
        Clinic clinic, IReadOnlyCollection<AttendanceTerminal> terminals, IReadOnlyCollection<User> trackedPeople,
        bool noPunchSinceOpening, DateTime nowUtc)
    {
        var live = LiveBornes.Of(terminals);
        var pending = terminals.Any(t => t.IsPending);
        var (state, activeFrom, inactiveReason) = AttendanceLockRules.LockSetup(
            clinic.AttendanceEnabled, clinic.AttendanceLockEnabled, clinic.WorkingHoursJson, live, pending,
            clinic.AttendanceLockArmedAtUtc, nowUtc);
        return new AttendanceSetupDto
        {
            HoursSet = AttendanceLockRules.HasOpeningHours(clinic.WorkingHoursJson),
            BorneLive = live.Any,
            BornePending = pending,
            NoPunchSinceOpening = noPunchSinceOpening,
            TrackedCount = trackedPeople.Count,
            SchemasMissing = trackedPeople
                .Where(u => !u.HasAttendanceSchema)
                .Select(StaffLeaveRules.DisplayName)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            LockState = state,
            LockActiveFrom = activeFrom,
            LockInactiveReason = inactiveReason,
        };
    }
}
