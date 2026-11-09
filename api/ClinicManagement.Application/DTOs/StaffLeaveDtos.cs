namespace ClinicManagement.Application.DTOs;

/// <summary>One secretary congé request, as both she and the admin read it.</summary>
public class StaffLeaveRequestDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    /// <summary>The <c>StaffLeaveType</c> member name — what the client branches on.</summary>
    public string Type { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public bool HalfDay { get; set; }

    /// <summary>The <c>StaffLeaveHalfDayPart</c> member name (« Morning » / « Afternoon »); null on a whole day.</summary>
    public string? HalfDayPart { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>The <c>StaffLeaveStatus</c> member name.</summary>
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;

    public decimal RequestedDays { get; set; }
    public decimal? CountedDays { get; set; }
    public string? DecidedByName { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? RejectionReason { get; set; }
    public bool EnteredByAdmin { get; set; }

    /// <summary>Congé payé left for the year once this request is counted; null when no allowance applies.</summary>
    public decimal? BalanceAfter { get; set; }

    /// <summary>The day <see cref="BalanceAfter"/> is read on (credits a monthly rule adds until then count).</summary>
    public DateOnly? BalanceAfterOn { get; set; }

    /// <summary>The same figure if her other waiting congés payés of that year were accepted too; null when there are none.</summary>
    public decimal? BalanceAfterWithPending { get; set; }

    /// <summary>Who else is already on an accepted congé on one of these days (a waiting request only).</summary>
    public List<string> OthersOff { get; set; } = new();

    /// <summary>Colleagues with a waiting request on one of these days (a waiting request only).</summary>
    public List<string> OthersPending { get; set; } = new();

    /// <summary>Set once withdrawn: who did it, whether it was her own doing, and the motif given.</summary>
    public string? CancelledByName { get; set; }
    public bool CancelledBySelf { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>Whether the reader may withdraw it now (an admin: any waiting or accepted one, whatever its dates).</summary>
    public bool CanCancel { get; set; }

    public DateTime CreatedAt { get; set; }
    public uint Version { get; set; }
}

public class StaffLeaveRequestsPageDto
{
    public List<StaffLeaveRequestDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>
/// A year of congé payé, folded from the « Droit à congé » lines and the congés (<c>StaffLeaveLedger</c>), read today.
/// <see cref="Remaining"/> = <see cref="Acquired"/> − <see cref="Taken"/> − <see cref="Planned"/> − <see cref="Expired"/>,
/// and null when nothing was ever set for her — never a balance the cabinet did not give.
/// </summary>
public class StaffLeaveBalanceDto
{
    public int Year { get; set; }

    /// <summary>A rule, a starting balance or an adjustment covers this year.</summary>
    public bool HasAccount { get; set; }

    /// <summary>What the previous year left on 1 January (negative: what she owed).</summary>
    public decimal CarriedIn { get; set; }

    /// <summary>« Solde de départ » given this year, and its day.</summary>
    public decimal? Opening { get; set; }
    public DateOnly? OpeningDay { get; set; }

    /// <summary>Credited by the rule so far this year.</summary>
    public decimal Credited { get; set; }

    /// <summary>Days added or removed by hand this year (net).</summary>
    public decimal Adjusted { get; set; }

    /// <summary>Carried in + opening + credited + adjusted.</summary>
    public decimal Acquired { get; set; }

    /// <summary>Accepted congé payé already started — every accepted day of the year, absorbed ones included.</summary>
    public decimal Taken { get; set; }

    /// <summary>
    /// The part of <see cref="Taken"/> that moves nothing — inside the « Solde de départ », or before her droit began;
    /// null when none.
    /// </summary>
    public decimal? TakenInOpening { get; set; }

    /// <summary>Accepted congé payé still to come this year — its days are spoken for.</summary>
    public decimal Planned { get; set; }

    /// <summary>Congé payé still waiting for an answer, counted in this year.</summary>
    public decimal Pending { get; set; }

    /// <summary>Lost on 31 December (a past year only).</summary>
    public decimal Expired { get; set; }

    /// <summary>What 31 December will take if nothing more is taken (a rule that does not carry over); null when nothing.</summary>
    public decimal? LostAtYearEnd { get; set; }

    /// <summary>What the previous 31 December took; null when nothing.</summary>
    public decimal? LostLastYear { get; set; }

    /// <summary>What the rule will still credit before 31 December.</summary>
    public decimal Upcoming { get; set; }

    public decimal? Remaining { get; set; }

    /// <summary>Every accepted day already taken, every year — « total pris depuis le début ».</summary>
    public decimal TakenTotal { get; set; }

    /// <summary>The rule in force today, or the next one to start; null with none.</summary>
    public StaffLeaveRuleDto? Rule { get; set; }

    /// <summary>A rule starting after today, beside <see cref="Rule"/> (never the same one).</summary>
    public StaffLeaveRuleDto? NextRule { get; set; }

    /// <summary>The rule's next credit after today.</summary>
    public StaffLeaveCreditDto? NextCredit { get; set; }
}

/// <summary>« 20 j par an » / « 2 j par mois », from <see cref="Since"/>; <see cref="CarryOver"/> false = lost on 31 December.</summary>
public class StaffLeaveRuleDto
{
    public decimal Days { get; set; }

    /// <summary>The <c>StaffLeaveAccrual</c> member name: « Year » or « Month ».</summary>
    public string Accrual { get; set; } = string.Empty;
    public bool CarryOver { get; set; }
    public DateOnly Since { get; set; }
}

public class StaffLeaveCreditDto
{
    public DateOnly Day { get; set; }
    public decimal Days { get; set; }
}

/// <summary>A line of the relevé. <see cref="Days"/> is signed; an opening states its value; <see cref="Balance"/> follows it.</summary>
public class StaffLeaveMovementDto
{
    public DateOnly Day { get; set; }

    /// <summary>The <c>StaffLeaveMovementKind</c> member name.</summary>
    public string Kind { get; set; } = string.Empty;
    public decimal Days { get; set; }
    public decimal Balance { get; set; }

    /// <summary>The « Droit à congé » line behind it (an opening, an adjustment, or the rule that credited).</summary>
    public Guid? EntryId { get; set; }

    /// <summary>The congé behind a taken or planned line, with its dates.</summary>
    public Guid? RequestId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }

    /// <summary>A credit's rule: « Year » or « Month ».</summary>
    public string? Accrual { get; set; }

    /// <summary>An adjustment's motif.</summary>
    public string? Reason { get; set; }

    /// <summary>Struck through: listed where it stood, moving nothing.</summary>
    public bool Cancelled { get; set; }
    public string? CancelReason { get; set; }

    /// <summary>History: before her droit began, or inside a « Solde de départ » — it does not move <see cref="Balance"/>.</summary>
    public bool Absorbed { get; set; }
}

/// <summary>A « Droit à congé » line as the admin manages it (cancel with a motif).</summary>
public class StaffLeaveAllowanceEntryDto
{
    public Guid Id { get; set; }

    /// <summary>The <c>StaffLeaveAllowanceKind</c> member name.</summary>
    public string Kind { get; set; } = string.Empty;
    public decimal Days { get; set; }
    public string? Accrual { get; set; }
    public bool CarryOver { get; set; }
    public DateOnly OnDay { get; set; }
    public string? Reason { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool Cancelled { get; set; }
    public string? CancelledByName { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
}

/// <summary>Days accepted in a year for one type of absence.</summary>
public class StaffLeaveTypeTotalDto
{
    public string Type { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public decimal Days { get; set; }
}

/// <summary>« Utilisateurs → Congés »'s team strip: each active secretary's balance and next congé.</summary>
public class TeamLeaveOverviewDto
{
    public int Year { get; set; }
    public List<TeamLeaveMemberDto> Members { get; set; } = new();
}

public class TeamLeaveMemberDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public StaffLeaveBalanceDto Balance { get; set; } = new();
    public DateOnly? NextStartDate { get; set; }
    public DateOnly? NextEndDate { get; set; }
    public bool NextHalfDay { get; set; }
    public string? NextHalfDayPart { get; set; }

    /// <summary>Congé payé days waiting for a decision.</summary>
    public decimal PendingDays { get; set; }
}

/// <summary>The secretary's own page: her balance and every request, newest first.</summary>
public class MyStaffLeaveDto
{
    public StaffLeaveBalanceDto Balance { get; set; } = new();

    /// <summary>The relevé of <see cref="StaffLeaveBalanceDto.Year"/>, read-only for her.</summary>
    public List<StaffLeaveMovementDto> Statement { get; set; } = new();
    public List<StaffLeaveRequestDto> Items { get; set; } = new();
}

/// <summary>A secretary's file for the admin.</summary>
public class StaffFileDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public StaffLeaveBalanceDto Balance { get; set; } = new();
    public List<StaffLeaveMovementDto> Statement { get; set; } = new();

    /// <summary>Every « Droit à congé » line ever written for her, cancelled ones included, newest first.</summary>
    public List<StaffLeaveAllowanceEntryDto> Entries { get; set; } = new();
    public List<StaffLeaveTypeTotalDto> AcceptedByType { get; set; } = new();
    public List<StaffLeaveRequestDto> Items { get; set; } = new();
}

/// <summary>What a request would count before it is sent.</summary>
public class StaffLeavePreviewDto
{
    public decimal Days { get; set; }

    /// <summary>The cabinet is closed every day of the range: the request would count 0 and is refused.</summary>
    public bool NoOpenDay { get; set; }

    public decimal? BalanceAfter { get; set; }
    public DateOnly? BalanceAfterOn { get; set; }
    public decimal? BalanceAfterWithPending { get; set; }

    /// <summary>Her own waiting or accepted request already on these dates — the send would be refused.</summary>
    public StaffLeaveOverlapDto? Overlap { get; set; }
}

public class StaffLeaveOverlapDto
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>« Pending » or « Approved ».</summary>
    public string Status { get; set; } = string.Empty;
}

public class PendingStaffLeaveCountDto
{
    public int Count { get; set; }
}

public class TeamModulesDto
{
    public bool LeaveRequestsEnabled { get; set; }
    public bool AttendanceEnabled { get; set; }
    public bool AttendanceLockEnabled { get; set; }
    public uint Version { get; set; }
}
