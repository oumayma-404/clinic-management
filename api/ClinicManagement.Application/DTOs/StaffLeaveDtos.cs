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

    /// <summary>Who else is already on an accepted congé on one of these days (a waiting request only).</summary>
    public List<string> OthersOff { get; set; } = new();

    /// <summary>Whether it may still be withdrawn today.</summary>
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

/// <summary>A year of congé payé: the allowance, what is taken, and what is left (null with no allowance).</summary>
public class StaffLeaveBalanceDto
{
    public int Year { get; set; }
    public decimal? Allowance { get; set; }
    public decimal Taken { get; set; }
    public decimal? Remaining { get; set; }

    /// <summary>Congé payé still waiting for an answer, counted in this year.</summary>
    public decimal Pending { get; set; }
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
}

/// <summary>The secretary's own page: her balance and every request, newest first.</summary>
public class MyStaffLeaveDto
{
    public StaffLeaveBalanceDto Balance { get; set; } = new();
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
    public List<StaffLeaveTypeTotalDto> AcceptedByType { get; set; } = new();
    public List<StaffLeaveRequestDto> Items { get; set; } = new();
}

/// <summary>What a request would count before it is sent.</summary>
public class StaffLeavePreviewDto
{
    public decimal Days { get; set; }
    public decimal? BalanceAfter { get; set; }
}

public class PendingStaffLeaveCountDto
{
    public int Count { get; set; }
}

public class TeamModulesDto
{
    public bool LeaveRequestsEnabled { get; set; }
    public uint Version { get; set; }
}
