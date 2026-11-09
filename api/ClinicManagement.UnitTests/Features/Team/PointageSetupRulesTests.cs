using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// What tells the doctor and the secretary where the pointage stands: « partie à 17:02 » rather than « pas encore », a
/// borne that has gone silent, the setup checklist, the device a borne may be, and the bell rows that announce each
/// step. Every instant is a fixed literal.
/// </summary>
public class PointageSetupRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly Thu5Nov = new(2026, 11, 5);
    private const string MonToSat = "["
        + "{\"Day\":\"Monday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Tuesday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},"
        + "{\"Day\":\"Wednesday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Thursday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},"
        + "{\"Day\":\"Friday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Saturday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"13:00\"},"
        + "{\"Day\":\"Sunday\",\"Enabled\":false,\"From\":\"08:00\",\"To\":\"17:00\"}]";

    private readonly User _ines = Person(User.RoleSecretary, "Ines Gharbi", withSchema: true);
    private readonly User _leila = Person(User.RoleSecretary, "Leila Trabelsi");
    private readonly User _admin = Person(User.RoleAdmin, "Dr Salma Ben Youssef");

    private static User Person(string role, string name, bool withSchema = false)
    {
        var user = User.CreateLocalUser(ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        if (withSchema) user.SetAttendanceSchema("HASH:0-1-2-5");
        return user;
    }

    private static DateTime At(DateOnly day, string hhmm) => AttendanceRules.ParseLocalTime(day, hhmm)!.Value;

    private static AttendanceEntry Line(User who, DateOnly day, string arrival, string? departure)
    {
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, who.Id, day, Guid.NewGuid(), "Accueil", At(day, arrival));
        if (departure is not null) line.Depart(At(day, departure), day);
        return line;
    }

    private static AttendanceTerminal LiveBorne(string name, DateTime activatedUtc) =>
        AttendanceTerminal.Register(Guid.NewGuid(), ClinicId, name, $"hash-{Guid.NewGuid():N}", "admin", activatedUtc);

    private static AttendanceTerminal WaitingBorne(string name) =>
        AttendanceTerminal.AssignToDevice(Guid.NewGuid(), ClinicId, name, Guid.NewGuid(), "Ines Gharbi · Chrome sur Windows",
            "admin", At(Thu5Nov, "07:00"));

    // ---- « left » ----------------------------------------------------------------------------------------

    [Fact]
    public void After_Her_Departure_She_Reads_Left_At_Never_Pas_Encore_Arrivee()
    {
        var lines = new[] { Line(_ines, Thu5Nov, "08:02", "12:05") };

        var ines = TeamTodayRules.PersonToday(_ines, lines, Array.Empty<StaffLeaveRequest>(), At(Thu5Nov, "13:00"));

        Assert.Equal(AttendanceRules.StateLeft, ines.State);
        Assert.Equal("12:05", ines.LeftAt);
        Assert.Null(ines.Since);
    }

    [Fact]
    public void Back_From_Lunch_She_Is_In_Again_And_Not_Left()
    {
        var lines = new[] { Line(_ines, Thu5Nov, "08:02", "12:05"), Line(_ines, Thu5Nov, "13:10", null) };

        var ines = TeamTodayRules.PersonToday(_ines, lines, Array.Empty<StaffLeaveRequest>(), At(Thu5Nov, "15:00"));

        Assert.Equal(AttendanceRules.StateIn, ines.State);
        Assert.Equal("13:10", ines.Since);
        Assert.Null(ines.LeftAt);
    }

    [Fact]
    public void Two_Closed_Lines_Read_The_Last_Departure()
    {
        var lines = new[] { Line(_ines, Thu5Nov, "13:10", "17:02"), Line(_ines, Thu5Nov, "08:02", "12:05") };

        Assert.Equal("17:02", TeamTodayRules.PersonToday(_ines, lines, Array.Empty<StaffLeaveRequest>(), At(Thu5Nov, "18:00")).LeftAt);
    }

    [Fact]
    public void A_Line_Closed_On_An_Earlier_Day_Is_Not_Today_So_She_Is_Out()
    {
        var lines = new[] { Line(_ines, Thu5Nov.AddDays(-1), "08:02", "17:00") };

        var ines = TeamTodayRules.PersonToday(_ines, lines, Array.Empty<StaffLeaveRequest>(), At(Thu5Nov, "09:00"));

        Assert.Equal(AttendanceRules.StateOut, ines.State);
        Assert.Null(ines.LeftAt);
    }

    [Fact]
    public void The_Schema_Facts_Ride_On_Her_Today()
    {
        var now = At(Thu5Nov, "09:00");
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            _ines.RegisterSchemaFailure(now);

        var ines = TeamTodayRules.PersonToday(_ines, Array.Empty<AttendanceEntry>(), Array.Empty<StaffLeaveRequest>(), now);
        var leila = TeamTodayRules.PersonToday(_leila, Array.Empty<AttendanceEntry>(), Array.Empty<StaffLeaveRequest>(), now);

        Assert.False(ines.NeedsSchema);
        Assert.Equal(now + User.SchemaLockDuration, ines.SchemaLockedUntil);
        Assert.True(leila.NeedsSchema);
        Assert.Null(leila.SchemaLockedUntil);
    }

    [Fact]
    public void Here_Then_Expected_Then_Gone_Then_On_Conge()
    {
        var ranks = new[] { AttendanceRules.StateLeave, AttendanceRules.StateLeft, AttendanceRules.StateOut, AttendanceRules.StateIn }
            .OrderBy(TeamTodayRules.Rank);

        Assert.Equal(new[] { "in", "out", "left", "leave" }, ranks);
    }

    // ---- a borne that may be dead -------------------------------------------------------------------------

    private bool NoPunch(string now, IReadOnlyCollection<AttendanceTerminal>? bornes = null, bool anyLine = false,
        IEnumerable<StaffLeaveRequest>? leaves = null, IEnumerable<User>? tracked = null, string? hours = MonToSat) =>
        AttendanceSetupRules.NoPunchSinceOpening(
            At(Thu5Nov, now), hours, bornes ?? new[] { LiveBorne("Accueil", At(Thu5Nov, "07:00").AddDays(-3)) },
            tracked ?? new[] { _ines, _leila }, leaves ?? Array.Empty<StaffLeaveRequest>(), anyLine);

    [Fact]
    public void Half_An_Hour_Past_Opening_With_Nobody_Pointed_The_Borne_May_Be_Dead()
    {
        Assert.False(NoPunch("08:29"));
        Assert.True(NoPunch("08:30"));
        Assert.True(NoPunch("15:00"));
    }

    [Fact]
    public void One_Line_In_The_Cabinet_Today_Proves_The_Borne_Works()
    {
        Assert.False(NoPunch("10:00", anyLine: true));
    }

    [Fact]
    public void A_Closed_Day_Or_No_Hours_Raise_Nothing()
    {
        Assert.False(NoPunch("10:00", hours: null));
        Assert.False(AttendanceSetupRules.NoPunchSinceOpening(
            At(Thu5Nov.AddDays(3), "10:00"), MonToSat, new[] { LiveBorne("Accueil", At(Thu5Nov, "07:00")) },
            new[] { _ines }, Array.Empty<StaffLeaveRequest>(), false));
    }

    [Fact]
    public void A_Borne_Installed_After_Opening_Is_Not_Blamed_For_The_Morning()
    {
        Assert.False(NoPunch("10:00", bornes: new[] { LiveBorne("Accueil", At(Thu5Nov, "09:00")) }));
        Assert.False(NoPunch("10:00", bornes: new[] { WaitingBorne("Accueil") }));
    }

    [Fact]
    public void Nobody_Expected_Raises_Nothing()
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, _ines.Id, StaffLeaveType.PaidLeave, Thu5Nov, Thu5Nov, false, "Mariage", 1m);
        leave.Approve(_admin.Id, 1m);

        // Ines is on congé all day and Leila has no schéma yet: the borne has nobody to take.
        Assert.False(NoPunch("10:00", leaves: new[] { leave }));
        Assert.False(NoPunch("10:00", tracked: Array.Empty<User>()));
    }

    [Theory]
    [InlineData(false, false, false, "no_borne")]
    [InlineData(false, true, false, "borne_pending")]
    [InlineData(true, false, true, "no_punch_since_opening")]
    [InlineData(true, true, false, null)]
    public void The_Setup_Line_Above_The_Team_Names_One_Problem(bool live, bool pending, bool noPunch, string? issue)
    {
        var bornes = new List<AttendanceTerminal>();
        if (live) bornes.Add(LiveBorne("Accueil", At(Thu5Nov, "07:00")));
        if (pending) bornes.Add(WaitingBorne("Salle 2"));
        var retired = LiveBorne("Ancienne", At(Thu5Nov, "07:00").AddDays(-9));
        retired.Revoke(At(Thu5Nov, "07:00").AddDays(-8));
        bornes.Add(retired);

        Assert.Equal(issue, AttendanceSetupRules.SetupIssue(bornes, noPunch));
    }

    // ---- the checklist -------------------------------------------------------------------------------------

    [Fact]
    public void The_Checklist_Counts_Who_Pointe_And_Names_Who_Has_No_Schema()
    {
        var clinic = new Clinic(ClinicId, "Cabinet", code: "CODE01");
        clinic.SetWorkingHours(MonToSat);
        clinic.SetAttendanceEnabled(true, At(Thu5Nov, "07:00").AddDays(-10));
        var amira = Person(User.RoleSecretary, "Amira Ben Salah");
        var tracked = AttendanceSetupRules.TrackedPeople(new[] { _ines, _leila, amira, _admin });

        var setup = AttendanceSetupRules.Setup(
            clinic, new[] { LiveBorne("Accueil", At(Thu5Nov, "07:00").AddDays(-5)) }, tracked, false, At(Thu5Nov, "10:00"));

        Assert.True(setup.HoursSet);
        Assert.True(setup.BorneLive);
        Assert.False(setup.BornePending);
        Assert.Equal(3, setup.TrackedCount);
        Assert.Equal(new[] { "Amira Ben Salah", "Leila Trabelsi" }, setup.SchemasMissing);
        Assert.Equal(AttendanceLockRules.LockStateActive, setup.LockState);
        Assert.Null(setup.LockActiveFrom);
    }

    [Fact]
    public void The_Pointage_Switched_On_This_Morning_Reads_From_Tomorrows_Opening()
    {
        var clinic = new Clinic(ClinicId, "Cabinet", code: "CODE01");
        clinic.SetWorkingHours(MonToSat);
        clinic.SetAttendanceEnabled(true, At(Thu5Nov, "09:15"));

        var setup = AttendanceSetupRules.Setup(
            clinic, new[] { LiveBorne("Accueil", At(Thu5Nov, "07:00").AddDays(-5)) }, new[] { _ines }, false, At(Thu5Nov, "10:00"));

        Assert.Equal(AttendanceLockRules.LockStateFrom, setup.LockState);
        Assert.Equal(At(Thu5Nov.AddDays(1), "08:00"), setup.LockActiveFrom);
    }

    // ---- when the lock is armed ----------------------------------------------------------------------------

    [Fact]
    public void Both_Switches_Becoming_On_Arm_The_Lock_And_Nothing_Else_Does()
    {
        var clinic = new Clinic(ClinicId, "Cabinet", code: "CODE01");
        Assert.Null(clinic.AttendanceLockArmedAtUtc);

        clinic.SetAttendanceEnabled(true, At(Thu5Nov, "10:00"));
        Assert.Equal(At(Thu5Nov, "10:00"), clinic.AttendanceLockArmedAtUtc);

        clinic.SetLeaveRequestsEnabled(true);
        clinic.SetAttendanceEnabled(true, At(Thu5Nov, "11:00"));
        Assert.Equal(At(Thu5Nov, "10:00"), clinic.AttendanceLockArmedAtUtc);

        clinic.SetAttendanceLockEnabled(false, At(Thu5Nov, "12:00"));
        Assert.Equal(At(Thu5Nov, "10:00"), clinic.AttendanceLockArmedAtUtc);

        clinic.SetAttendanceLockEnabled(true, At(Thu5Nov, "14:00"));
        Assert.Equal(At(Thu5Nov, "14:00"), clinic.AttendanceLockArmedAtUtc);
    }

    [Fact]
    public void A_Borne_Is_Live_From_Its_Registration_Or_Its_Claim_Never_While_It_Waits()
    {
        var direct = LiveBorne("Accueil", At(Thu5Nov, "07:00"));
        var waiting = WaitingBorne("Salle 2");

        Assert.Equal(At(Thu5Nov, "07:00"), direct.ActivatedAtUtc);
        Assert.True(direct.IsLive);
        Assert.Null(waiting.ActivatedAtUtc);
        Assert.False(waiting.IsLive);

        waiting.Claim("HASH", At(Thu5Nov, "09:30"));

        Assert.Equal(At(Thu5Nov, "09:30"), waiting.ActivatedAtUtc);
        Assert.True(waiting.IsLive);
    }

    // ---- which device can be a borne -----------------------------------------------------------------------

    [Theory]
    [InlineData("Chrome sur Windows", "pc")]
    [InlineData("Edge sur Windows", "pc")]
    [InlineData("Safari sur Mac", "pc")]
    [InlineData("Firefox sur Linux", "pc")]
    [InlineData("Application Windows", "pc")]
    [InlineData("Chrome sur Android", "phone")]
    [InlineData("Safari sur iOS", "phone")]
    [InlineData("Application android", "phone")]
    [InlineData("Application ios", "phone")]
    [InlineData("Safari sur iPad", "tablet")]
    [InlineData("Chrome", "unknown")]
    [InlineData("Visite de démonstration", "unknown")]
    [InlineData(null, "unknown")]
    [InlineData("  ", "unknown")]
    public void A_Device_Label_Says_What_The_Device_Is(string? label, string kind)
    {
        Assert.Equal(kind, AttendanceRules.DeviceKindOf(label));
    }

    // ---- names and sentences ------------------------------------------------------------------------------

    [Fact]
    public void A_Name_Is_Taken_By_A_Live_Or_Waiting_Borne_But_Not_By_A_Retired_One_Or_The_One_Replaced()
    {
        var live = LiveBorne("Accueil", At(Thu5Nov, "07:00"));
        var waiting = WaitingBorne("Salle 2");
        var retired = LiveBorne("Bureau", At(Thu5Nov, "07:00"));
        retired.Revoke(At(Thu5Nov, "08:00"));
        var bornes = new[] { live, waiting, retired };

        Assert.True(AttendanceRules.IsNameTaken(" accueil ", bornes, Array.Empty<Guid>()));
        Assert.True(AttendanceRules.IsNameTaken("Salle 2", bornes, Array.Empty<Guid>()));
        Assert.False(AttendanceRules.IsNameTaken("Bureau", bornes, Array.Empty<Guid>()));
        Assert.False(AttendanceRules.IsNameTaken("Accueil", bornes, new[] { live.Id }));
    }

    [Fact]
    public void The_Refusals_Name_The_Borne_And_The_People_Without_A_Gendered_Word()
    {
        Assert.Equal("Une autre borne s'appelle déjà « Accueil » : choisissez un autre nom.", AttendanceRules.BorneNameTaken(" Accueil "));
        Assert.Equal("Ines Gharbi n'a pas encore pointé son départ : gardez une borne active jusqu'à son départ.",
            AttendanceRules.BorneLastWhilePresent(new[] { "Ines Gharbi" }));
        Assert.Equal(
            "Amira Ben Salah, Ines Gharbi et Leila Trabelsi n'ont pas encore pointé leur départ : gardez une borne active jusqu'à leur départ.",
            AttendanceRules.BorneLastWhilePresent(new[] { "Amira Ben Salah", "Ines Gharbi", "Leila Trabelsi" }));
        Assert.DoesNotContain("présente", AttendanceRules.SchemaMissing);
    }

    // ---- the bell rows ------------------------------------------------------------------------------------

    [Fact]
    public void A_Setup_Row_For_One_Person_Opens_Her_Pointage_And_One_For_The_Admins_Opens_The_Settings()
    {
        var mine = StaffNotification.ForAttendance(
            Guid.NewGuid(), ClinicId, NotificationCategory.AttendanceSchemaToCreate, "t", "m", _ines.Id, null, At(Thu5Nov, "09:00"));
        var admins = StaffNotification.ForAttendance(
            Guid.NewGuid(), ClinicId, NotificationCategory.AttendanceSchemaBlocked, "t", "m", null, null, At(Thu5Nov, "09:00"));

        Assert.Equal(_ines.Id, mine.TargetUserId);
        Assert.Null(mine.TargetRole);
        Assert.Equal(NotificationTargetKind.Attendance, mine.TargetKind);
        Assert.Null(admins.TargetUserId);
        Assert.Equal(User.RoleAdmin, admins.TargetRole);
        Assert.Equal(NotificationTargetKind.AttendanceSettings, admins.TargetKind);
        Assert.Throws<ArgumentOutOfRangeException>(() => StaffNotification.ForAttendance(
            Guid.NewGuid(), ClinicId, NotificationCategory.LowStock, "t", "m", null, null, At(Thu5Nov, "09:00")));
    }

    [Fact]
    public void Only_Create_Your_Schema_Reaches_Her_Phone_And_Its_Push_Says_The_Bells_Title()
    {
        Assert.True(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.AttendanceSchemaToCreate));
        Assert.False(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.AttendanceBorneInstalled));
        Assert.False(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.AttendanceBorneNotReceived));
        Assert.False(StaffNotificationRules.ReachesALockedPhone(NotificationCategory.AttendanceSchemaBlocked));
        Assert.Equal(AttendanceNotices.SchemaToCreate("Accueil").Title,
            StaffNotificationRules.PushLabel(NotificationCategory.AttendanceSchemaToCreate));
    }

    [Fact]
    public void The_Bell_Sentences_Name_The_Borne_The_Device_And_The_Hour()
    {
        Assert.Equal("La borne « Accueil » est en service : créez votre schéma pour y pointer.",
            AttendanceNotices.SchemaToCreate("Accueil").Message);
        Assert.Equal("La borne « Accueil » est installée sur l'appareil de Ines Gharbi.",
            AttendanceNotices.BorneInstalledForAdmins("Accueil", "Ines Gharbi").Message);
        Assert.Equal("Dr Salma a fait de cet appareil la borne « Accueil ».",
            AttendanceNotices.BorneInstalledForOwner("Accueil", "Dr Salma").Message);
        Assert.Equal(
            "La borne « Accueil » n'a pas été installée : la session de l'appareil choisi (Ines Gharbi · Chrome sur Windows) a été fermée. Choisissez un autre appareil.",
            AttendanceNotices.BorneNotReceived("Accueil", "Ines Gharbi · Chrome sur Windows").Message);
        Assert.Equal("Le schéma de Ines Gharbi est bloqué jusqu'à 08:47 après 5 essais.",
            AttendanceNotices.SchemaBlocked("Ines Gharbi", "08:47").Message);
    }

    [Fact]
    public void The_Sign_In_Lockout_Sentence_Is_The_One_The_Schema_Change_Reuses()
    {
        Assert.Contains("temporairement bloqué", LoginCommandHandler.LockedOutError);
    }
}
