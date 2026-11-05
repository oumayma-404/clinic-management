namespace ClinicManagement.Application.DTOs;

/// <summary>What a device learns about itself: whether it is a borne, its name, and the tiles it shows.</summary>
public class PointageStatusDto
{
    public bool IsBorne { get; set; }

    /// <summary>Which borne this device is — what « Paramètres → Équipe » marks « cet appareil » and replaces.</summary>
    public Guid? TerminalId { get; set; }
    public string? Name { get; set; }

    /// <summary>False on a borne whose cabinet has switched the pointage off: the page says so instead of showing tiles.</summary>
    public bool Enabled { get; set; }
    public List<PointagePersonDto> People { get; set; } = new();
}

/// <summary>One tile of the borne. Nothing else about anyone: name, initials, today's state.</summary>
public class PointagePersonDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;

    /// <summary>« out » (pas encore arrivée / partie), « in » (présente), « leave » (en congé aujourd'hui).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>« 08:02 » — when she arrived, while « in ».</summary>
    public string? Since { get; set; }

    public bool NeedsSchema { get; set; }
    public DateTime? LockedUntil { get; set; }
}

public class PunchResultDto
{
    /// <summary>« arrival » or « departure ».</summary>
    public string Action { get; set; } = string.Empty;
    public string At { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TerminalName { get; set; } = string.Empty;
}

public class AttendanceTerminalDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
    public uint Version { get; set; }
}

/// <summary>A borne just registered, with its secret — the controller puts it in the cookie and never in the body.</summary>
public class RegisteredTerminalDto
{
    public AttendanceTerminalDto Terminal { get; set; } = new();
    public string Secret { get; set; } = string.Empty;
}

/// <summary>« Personnes qui pointent »: every account of the cabinet, and whether it clocks in.</summary>
public class AttendancePersonSettingDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool Tracked { get; set; }

    /// <summary>The admin's choice; null when the account follows its role.</summary>
    public bool? TrackedOverride { get; set; }
    public bool HasSchema { get; set; }
    public DateTime? LockedUntil { get; set; }
    public uint Version { get; set; }
}

public class AttendanceSettingsDto
{
    public List<AttendanceTerminalDto> Terminals { get; set; } = new();
    public List<AttendancePersonSettingDto> People { get; set; } = new();
}

/// <summary>One arrival/departure line. Times are cabinet-local « HH:mm ».</summary>
public class AttendanceEntryDto
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateOnly LocalDay { get; set; }
    public string Arrival { get; set; } = string.Empty;
    public string? Departure { get; set; }

    /// <summary>Open on today's line: she is still there.</summary>
    public bool IsPresent { get; set; }

    /// <summary>Open on an earlier day: « Départ non pointé ». No hour is invented for it.</summary>
    public bool DepartureMissing { get; set; }

    public bool IsCorrected { get; set; }
    public string? CorrectionReason { get; set; }
    public string? CorrectedByName { get; set; }
    public DateTime? CorrectedAt { get; set; }

    /// <summary>What the borne recorded, kept whatever the correction says.</summary>
    public string? PunchedArrival { get; set; }
    public string? PunchedDeparture { get; set; }
    public string? TerminalName { get; set; }

    /// <summary>Minutes of a closed line; null while open.</summary>
    public int? Minutes { get; set; }
    public uint Version { get; set; }
}

public class AttendanceLeaveMarkDto
{
    public string UserId { get; set; } = string.Empty;
    public string TypeLabel { get; set; } = string.Empty;
    public bool HalfDay { get; set; }
}

public class AttendanceDayDto
{
    public DateOnly Day { get; set; }

    /// <summary>False on a day the cabinet is closed (Paramètres → Horaires).</summary>
    public bool IsOpenDay { get; set; }
    public bool IsToday { get; set; }
    public List<AttendanceEntryDto> Entries { get; set; } = new();
    public List<AttendanceLeaveMarkDto> Leaves { get; set; } = new();
}

public class AttendancePersonDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>A month's figures for one person: days pointed, hours of closed lines, missing departures.</summary>
public class AttendanceTotalsDto
{
    public string UserId { get; set; } = string.Empty;
    public int Days { get; set; }
    public int Minutes { get; set; }
    public int MissingDepartures { get; set; }

    /// <summary>A line still open today — its hours are not in <see cref="Minutes"/> yet.</summary>
    public bool OpenToday { get; set; }
}

public class AttendanceSheetDto
{
    /// <summary>« 2026-11 ».</summary>
    public string Month { get; set; } = string.Empty;
    public string MonthLabel { get; set; } = string.Empty;
    public List<AttendancePersonDto> People { get; set; } = new();
    public List<AttendanceDayDto> Days { get; set; } = new();
    public List<AttendanceTotalsDto> Totals { get; set; } = new();
}

/// <summary>Who is here today, who is on congé, and what waits for the admin.</summary>
public class TeamTodayDto
{
    public DateOnly Day { get; set; }
    public List<TeamTodayPersonDto> People { get; set; } = new();

    /// <summary>Congé requests waiting for a decision; null while the congés module is off.</summary>
    public int? PendingLeaveCount { get; set; }
}

public class TeamTodayPersonDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>« in », « out », « leave ».</summary>
    public string State { get; set; } = string.Empty;
    public string? Since { get; set; }
    public string? LeaveTypeLabel { get; set; }

    /// <summary>The congé covers half the day: she is expected for the other half, so the state stays « out ».</summary>
    public bool LeaveHalfDay { get; set; }

    /// <summary>A line left open on an earlier day, still to be corrected.</summary>
    public DateOnly? MissingDepartureOn { get; set; }
}

/// <summary>« Mes présences » and the schéma card: her own month, and whether she has what the borne needs.</summary>
public class MyAttendanceDto
{
    public bool Tracked { get; set; }
    public bool HasSchema { get; set; }
    public AttendanceSheetDto Sheet { get; set; } = new();
}
