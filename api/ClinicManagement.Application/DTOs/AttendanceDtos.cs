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

    /// <summary>« out » (no line today), « in » (a line open), « left » (today's lines all closed), « leave » (congé all day).</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>« 08:02 » — the arrival, while « in ».</summary>
    public string? Since { get; set; }

    public bool NeedsSchema { get; set; }
    public DateTime? LockedUntil { get; set; }

    /// <summary>« 17:02 » — the last departure, while « left ».</summary>
    public string? LeftAt { get; set; }
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

    /// <summary>Chosen for another signed-in device that has not picked it up yet: nobody can punch on it.</summary>
    public bool IsPending { get; set; }

    /// <summary>« Ines Gharbi · Chrome sur Windows » — the device it was chosen for, when chosen from the list.</summary>
    public string? DeviceDescription { get; set; }

    /// <summary>Still waiting while that device's session has ended: it will never pick it up.</summary>
    public bool DeviceSignedOut { get; set; }

    /// <summary>When it became a working borne (registered on its device, or claimed); null while it waits.</summary>
    public DateTime? ActivatedAt { get; set; }

    public uint Version { get; set; }
}

/// <summary>
/// « Ajouter une borne »: one signed-in device of the cabinet the admin may make a borne. It is a session — the only
/// thing the server knows a device by until it carries a borne cookie.
/// </summary>
public class BorneDeviceDto
{
    public Guid SessionId { get; set; }
    public string PersonName { get; set; } = string.Empty;

    /// <summary>« Chrome sur Windows »; null when the device gave no name.</summary>
    public string? DeviceLabel { get; set; }
    public DateTime LastActiveAt { get; set; }

    /// <summary>« pc », « tablet », « phone » or « unknown », read off the device label.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>The admin's own device — offered as « Cet appareil », which takes the cookie at once.</summary>
    public bool IsThisDevice { get; set; }
}

/// <summary>
/// A borne just registered, with its secret — the controller puts it in the cookie and never in the body. Empty when
/// it was chosen for another device: that device receives its own secret when it claims it.
/// </summary>
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
    public AttendanceSetupDto Setup { get; set; } = new();
}

/// <summary>Where the pointage setup stands, read from the facts the lock rule reads so the checklist cannot disagree with it.</summary>
public class AttendanceSetupDto
{
    public bool HoursSet { get; set; }
    public bool BorneLive { get; set; }
    public bool BornePending { get; set; }

    /// <summary>An open day past opening + 30 min with no line in the cabinet, while a borne is live: it may be dead.</summary>
    public bool NoPunchSinceOpening { get; set; }

    /// <summary>Active accounts that pointent.</summary>
    public int TrackedCount { get; set; }

    /// <summary>Full names of active tracked people with no schéma.</summary>
    public List<string> SchemasMissing { get; set; } = new();

    /// <summary>« off » · « active » · « from » (starts at <see cref="LockActiveFrom"/>) · « inactive » (see the reason).</summary>
    public string LockState { get; set; } = string.Empty;
    public DateTime? LockActiveFrom { get; set; }

    /// <summary>« hours_unset », « no_borne » or « borne_pending » while « inactive »; null otherwise.</summary>
    public string? LockInactiveReason { get; set; }
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

    /// <summary>The departure is the hour she declared the next morning (« Déclaré »), untouched since.</summary>
    public bool DepartureDeclared { get; set; }

    /// <summary>
    /// Beside a declared departure: her last save in the app that day, as she was shown it; null for « aucune » or
    /// a departure that was not declared.
    /// </summary>
    public string? DeclaredLastSave { get; set; }

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

    /// <summary>First day an empty open day counts as « Aucun pointage »: the cabinet's first line, or her account's creation if later; null = never.</summary>
    public DateOnly? CountsFrom { get; set; }
}

/// <summary>A month's figures for one person: days pointed, hours of closed lines, missing departures.</summary>
public class AttendanceTotalsDto
{
    public string UserId { get; set; } = string.Empty;
    public int Days { get; set; }
    public int Minutes { get; set; }
    public int MissingDepartures { get; set; }

    /// <summary>Lines whose departure she declared — counted in <see cref="Minutes"/> like any closed line.</summary>
    public int DeclaredDepartures { get; set; }

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

    /// <summary>The cabinet opens today (Paramètres → Horaires; Monday–Saturday unset).</summary>
    public bool IsOpenDay { get; set; }

    /// <summary>« no_borne », « borne_pending » or « no_punch_since_opening » — one setup problem worth a line; null otherwise.</summary>
    public string? SetupIssue { get; set; }

    /// <summary>Congé requests waiting for a decision; null while the congés module is off.</summary>
    public int? PendingLeaveCount { get; set; }
}

public class TeamTodayPersonDto
{
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;

    /// <summary>« in », « out », « left », « leave ».</summary>
    public string State { get; set; } = string.Empty;
    public string? Since { get; set; }
    public string? LeaveTypeLabel { get; set; }

    /// <summary>The congé covers half the day: she is expected for the other half, so the state stays « out ».</summary>
    public bool LeaveHalfDay { get; set; }

    /// <summary>A line left open on an earlier day, still to be corrected.</summary>
    public DateOnly? MissingDepartureOn { get; set; }

    /// <summary>« 17:02 » — the last departure, while « left ».</summary>
    public string? LeftAt { get; set; }

    /// <summary>No schéma yet: the borne cannot take her punch.</summary>
    public bool NeedsSchema { get; set; }

    /// <summary>While 5 wrong schémas block her tile.</summary>
    public DateTime? SchemaLockedUntil { get; set; }
}

/// <summary>« Mes présences » and the schéma card: her own month, and whether she has what the borne needs.</summary>
public class MyAttendanceDto
{
    public bool Tracked { get; set; }
    public bool HasSchema { get; set; }
    public AttendanceSheetDto Sheet { get; set; } = new();
}

/// <summary>
/// « Pointez votre arrivée » and the sign-out prompt (<c>pointage-obligatoire</c>): whether her account is locked
/// right now, where she can pointer, and whether this very device is one of those bornes.
/// </summary>
public class ClockStatusDto
{
    /// <summary>Her own account — what the pad inside the lock punches for.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Her name as the borne's tile shows it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="Features.Team.AttendanceLockRules.IsLocked"/> — the rule the 423 on a save asks too.</summary>
    public bool Locked { get; set; }

    /// <summary>The cabinet's active bornes, by name.</summary>
    public List<string> Bornes { get; set; } = new();

    /// <summary>« 08:02 » while today's line is open: « Pointer votre départ ? » at sign-out.</summary>
    public string? PresentSince { get; set; }

    /// <summary>This device is an active borne of her cabinet, so the pad can open right here.</summary>
    public bool CanClockOutHere { get; set; }

    /// <summary>She has a schéma — without one the borne cannot take her punch, so the lock says where to make it.</summary>
    public bool HasSchema { get; set; }

    /// <summary>An admin chose this very device as the borne named here: the app claims it and receives its cookie.</summary>
    public string? BorneWaiting { get; set; }

    /// <summary>Her account pointe in a cabinet whose pointage is on.</summary>
    public bool Tracked { get; set; }

    /// <summary>« 12:05 » — today's last departure, while no line is open today.</summary>
    public string? LeftAt { get; set; }

    /// <summary>While 5 wrong schémas block her.</summary>
    public DateTime? SchemaLockedUntil { get; set; }
}

/// <summary>An earlier day's line she left open, as the borne's declaration step shows it (409 previous_departure_missing).</summary>
public sealed record PreviousDayDto(string Day, string Arrival, string? LastSaveAt);
