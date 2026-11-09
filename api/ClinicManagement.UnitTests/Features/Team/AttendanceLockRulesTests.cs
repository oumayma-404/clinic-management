using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// « Pointez votre arrivée »: the one rule the screen and the 423 both ask, the opening hours it is judged against,
/// when the reminder is due, and how a declared departure reads on the line, in the totals and in the export.
/// </summary>
public class AttendanceLockRulesTests
{
    private static readonly DateOnly Friday = new(2026, 11, 6);
    private static readonly AttendanceLockFacts AllHold = new(
        AttendanceEnabled: true, LockEnabled: true, Tracked: true, InsideOpeningHours: true, HasLineToday: false,
        OnLeaveToday: false, OnPcDeSecours: false, MayRecordWork: true, HasActiveBorne: true, LockStarted: true);

    private static string Hours(bool fridayOpen = true, string from = "08:00", string to = "17:00") =>
        "[" + string.Join(",", new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(d => d == "Friday"
                ? $"{{\"Day\":\"Friday\",\"Enabled\":{(fridayOpen ? "true" : "false")},\"From\":\"{from}\",\"To\":\"{to}\"}}"
                : $"{{\"Day\":\"{d}\",\"Enabled\":true,\"From\":\"09:00\",\"To\":\"17:00\"}}")) + "]";

    private static DateTime FridayAt(string local) => AttendanceRules.ParseLocalTime(Friday, local)!.Value;

    // ---- the lock rule ----------------------------------------------------------------------------------------

    [Fact]
    public void Every_Condition_Holding_Locks()
    {
        Assert.True(AttendanceLockRules.IsLocked(AllHold));
    }

    public static TheoryData<string, AttendanceLockFacts> EachLiftingFact => new()
    {
        { "pointage off", AllHold with { AttendanceEnabled = false } },
        { "lock off", AllHold with { LockEnabled = false } },
        { "not tracked", AllHold with { Tracked = false } },
        { "outside opening hours", AllHold with { InsideOpeningHours = false } },
        { "a line today", AllHold with { HasLineToday = true } },
        { "a congé today", AllHold with { OnLeaveToday = true } },
        { "on the PC de secours", AllHold with { OnPcDeSecours = true } },
        { "may not record work", AllHold with { MayRecordWork = false } },
        { "no borne to punch on", AllHold with { HasActiveBorne = false } },
        { "armed after today's opening", AllHold with { LockStarted = false } },
    };

    [Theory]
    [MemberData(nameof(EachLiftingFact))]
    public void Any_One_Lifting_Fact_Alone_Lifts_The_Lock(string why, AttendanceLockFacts facts)
    {
        Assert.False(AttendanceLockRules.IsLocked(facts), why);
    }

    // ---- opening hours ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Unset_Or_Unreadable_Hours_Give_No_Window(string? json)
    {
        Assert.Null(AttendanceLockRules.HoursOn(Friday, json));
        Assert.False(AttendanceLockRules.IsInsideOpeningHours(json, FridayAt("10:00")));
        Assert.Null(AttendanceLockRules.ReminderDueUtc(Friday, json));
    }

    [Fact]
    public void A_Closed_Day_Gives_No_Window()
    {
        Assert.Null(AttendanceLockRules.HoursOn(Friday, Hours(fridayOpen: false)));
        Assert.Null(AttendanceLockRules.ReminderDueUtc(Friday, Hours(fridayOpen: false)));
    }

    [Theory]
    [InlineData("17:00", "08:00")]
    [InlineData("08:00", "08:00")]
    public void A_Closing_Not_After_The_Opening_Gives_No_Window(string from, string to)
    {
        Assert.Null(AttendanceLockRules.HoursOn(Friday, Hours(from: from, to: to)));
    }

    [Theory]
    [InlineData("07:59", false)]
    [InlineData("08:00", true)]
    [InlineData("12:30", true)]
    [InlineData("16:59", true)]
    [InlineData("17:00", false)]
    [InlineData("21:00", false)]
    public void The_Window_Runs_From_Opening_To_Just_Before_Closing_On_The_Cabinets_Clock(string local, bool inside)
    {
        Assert.Equal(inside, AttendanceLockRules.IsInsideOpeningHours(Hours(), FridayAt(local)));
    }

    [Fact]
    public void The_Reminder_Is_Due_Fifteen_Minutes_After_Closing()
    {
        Assert.Equal(FridayAt("17:15"), AttendanceLockRules.ReminderDueUtc(Friday, Hours()));
    }

    // ---- the lock starts at the next opening ----------------------------------------------------------------

    private static readonly LiveBornes OneLiveBorne = new(true, null);

    [Fact]
    public void The_Lock_Waits_For_The_Later_Of_The_Switch_And_The_First_Live_Borne()
    {
        var armed = FridayAt("10:00");
        var live = FridayAt("11:30");

        Assert.Equal(live, AttendanceLockRules.LockStartsAfterUtc(armed, live));
        Assert.Equal(live, AttendanceLockRules.LockStartsAfterUtc(live, armed));
        Assert.Equal(armed, AttendanceLockRules.LockStartsAfterUtc(armed, null));
        Assert.Null(AttendanceLockRules.LockStartsAfterUtc(null, null));
    }

    [Theory]
    [InlineData("07:30", "10:00", true)]   // armed before opening: today's opening counts
    [InlineData("08:00", "10:00", true)]   // armed at the opening instant: it counts too
    [InlineData("08:01", "10:00", false)]  // armed a minute into the day: never mid-day
    [InlineData("14:00", "16:59", false)]
    public void Armed_During_A_Day_It_Locks_Nobody_That_Day(string armedAt, string now, bool started)
    {
        Assert.Equal(started, AttendanceLockRules.HasLockStarted(FridayAt(armedAt), Hours(), FridayAt(now)));
    }

    [Fact]
    public void Armed_Long_Ago_Or_Never_Recorded_It_Has_Started()
    {
        Assert.True(AttendanceLockRules.HasLockStarted(null, Hours(), FridayAt("10:00")));
        Assert.True(AttendanceLockRules.HasLockStarted(FridayAt("10:00").AddDays(-30), Hours(), FridayAt("10:00")));
    }

    [Fact]
    public void Armed_On_Friday_Afternoon_It_Starts_At_Saturdays_Opening()
    {
        var saturdayNine = AttendanceRules.ParseLocalTime(Friday.AddDays(1), "09:00")!.Value;

        Assert.Equal(saturdayNine, AttendanceLockRules.FirstOpeningFromUtc(FridayAt("14:00"), Hours()));
        Assert.False(AttendanceLockRules.HasLockStarted(FridayAt("14:00"), Hours(), saturdayNine.AddMinutes(-1)));
        Assert.True(AttendanceLockRules.HasLockStarted(FridayAt("14:00"), Hours(), saturdayNine));
    }

    [Fact]
    public void A_Closed_Day_Is_Skipped_To_The_Next_Opening()
    {
        var thursday = Friday.AddDays(-1);
        var armed = AttendanceRules.ParseLocalTime(thursday, "18:00")!.Value;

        Assert.Equal(AttendanceRules.ParseLocalTime(Friday.AddDays(1), "09:00"),
            AttendanceLockRules.FirstOpeningFromUtc(armed, Hours(fridayOpen: false)));
    }

    [Fact]
    public void The_Setup_Says_Off_When_The_Switch_Is_Off()
    {
        var setup = AttendanceLockRules.LockSetup(true, false, Hours(), OneLiveBorne, false, null, FridayAt("10:00"));

        Assert.Equal((AttendanceLockRules.LockStateOff, (DateTime?)null, (string?)null), setup);
    }

    [Theory]
    [InlineData(null, false, false, "hours_unset")]
    [InlineData("hours", false, false, "no_borne")]
    [InlineData("hours", false, true, "borne_pending")]
    public void The_Setup_Names_Why_The_Lock_Cannot_Lock(string? hours, bool live, bool pending, string reason)
    {
        var bornes = live ? OneLiveBorne : LiveBornes.None;
        var (state, from, why) = AttendanceLockRules.LockSetup(
            true, true, hours is null ? null : Hours(), bornes, pending, null, FridayAt("10:00"));

        Assert.Equal(AttendanceLockRules.LockStateInactive, state);
        Assert.Null(from);
        Assert.Equal(reason, why);
    }

    [Fact]
    public void Armed_Mid_Day_The_Setup_Says_From_The_Next_Opening_And_The_Rule_Agrees()
    {
        var armed = FridayAt("14:00");
        var saturdayNine = AttendanceRules.ParseLocalTime(Friday.AddDays(1), "09:00")!.Value;

        var (state, from, why) = AttendanceLockRules.LockSetup(true, true, Hours(), OneLiveBorne, false, armed, FridayAt("15:00"));

        Assert.Equal(AttendanceLockRules.LockStateFrom, state);
        Assert.Equal(saturdayNine, from);
        Assert.Null(why);
        Assert.False(AttendanceLockRules.HasLockStarted(armed, Hours(), FridayAt("15:00")));
    }

    [Fact]
    public void A_Borne_Gone_Live_Mid_Day_Also_Waits_For_The_Next_Opening()
    {
        var bornes = new LiveBornes(true, FridayAt("11:00"));

        var (state, from, _) = AttendanceLockRules.LockSetup(true, true, Hours(), bornes, false, null, FridayAt("12:00"));

        Assert.Equal(AttendanceLockRules.LockStateFrom, state);
        Assert.Equal(AttendanceRules.ParseLocalTime(Friday.AddDays(1), "09:00"), from);
    }

    [Fact]
    public void Once_Started_The_Setup_Says_Active()
    {
        var (state, from, why) = AttendanceLockRules.LockSetup(
            true, true, Hours(), new LiveBornes(true, FridayAt("07:00")), false, FridayAt("07:30"), FridayAt("10:00"));

        Assert.Equal(AttendanceLockRules.LockStateActive, state);
        Assert.Null(from);
        Assert.Null(why);
    }

    [Fact]
    public void Live_Bornes_Fold_To_The_Earliest_Activation_And_An_Unrecorded_One_Is_Old()
    {
        Assert.Equal(LiveBornes.None, LiveBornes.From(Array.Empty<DateTime?>()));
        Assert.Equal(new LiveBornes(true, FridayAt("08:00")), LiveBornes.From(new DateTime?[] { FridayAt("09:00"), FridayAt("08:00") }));
        Assert.Equal(new LiveBornes(true, null), LiveBornes.From(new DateTime?[] { FridayAt("09:00"), null }));
    }

    [Fact]
    public void The_Sentences_Name_The_Hours()
    {
        Assert.Equal("Arrivée à 08:02 : pointez votre départ à la borne « Accueil ».",
            AttendanceLockRules.ReminderMessage("08:02", new[] { "Accueil" }));
        Assert.Equal("Arrivée à 08:02 : pointez votre départ à la borne « Accueil » ou « Salle 2 ».",
            AttendanceLockRules.ReminderMessage("08:02", new[] { "Accueil", "Salle 2" }));
        Assert.DoesNotContain("présente", AttendanceLockRules.ReminderMessage("08:02", new[] { "Accueil" }));
        Assert.Equal("L'heure de départ doit être après l'arrivée (08:02).",
            AttendanceLockRules.DeclaredDepartureInvalid("08:02"));
        Assert.Equal("2026-11-06", AttendanceLockRules.DayKey(Friday));
    }

    [Fact]
    public void The_Journal_Lookup_Ignores_What_Is_Not_Cabinet_Work()
    {
        Assert.Equal(
            new[] { nameof(AttendanceEntry), nameof(AttendanceTerminal), nameof(User), nameof(DeviceRegistration), nameof(StaffLeaveRequest) },
            AttendanceLockRules.NotWorkInTheJournal);
    }

    [Fact]
    public void The_Previous_Day_Is_Named_Hier_Only_When_It_Was_Yesterday()
    {
        Assert.Equal("Votre départ d'hier n'a pas été pointé. Indiquez votre heure de départ.",
            AttendanceLockRules.PreviousDepartureMissing(Friday.AddDays(-1), Friday));
        Assert.Equal("Votre départ du vendredi 6 novembre n'a pas été pointé. Indiquez votre heure de départ.",
            AttendanceLockRules.PreviousDepartureMissing(Friday, Friday.AddDays(3)));
    }

    // ---- the declared departure on the line -------------------------------------------------------------------

    private static AttendanceEntry Line(DateOnly day, string arrival) =>
        AttendanceEntry.Arrive(Guid.NewGuid(), Guid.NewGuid(), "local|ines", day, Guid.NewGuid(), "Accueil",
            AttendanceRules.ParseLocalTime(day, arrival)!.Value);

    [Fact]
    public void A_Declared_Departure_Closes_An_Earlier_Day_As_Declared_With_Her_Last_Save()
    {
        var thursday = Friday.AddDays(-1);
        var line = Line(thursday, "08:02");
        var departure = AttendanceRules.ParseLocalTime(thursday, "17:30")!.Value;
        var lastSave = AttendanceRules.ParseLocalTime(thursday, "17:46")!.Value;

        line.DeclareDeparture(departure, lastSave, Friday, FridayAt("08:05"));

        Assert.Equal(departure, line.DepartureUtc);
        Assert.Equal(AttendanceDepartureSource.DeclaredBySelf, line.DepartureSource);
        Assert.True(line.IsDeclared);
        Assert.Equal(lastSave, line.DeclaredLastSaveUtc);
        Assert.Null(line.PunchedDepartureUtc);
    }

    [Fact]
    public void Todays_Line_Is_Never_Declared_It_Closes_At_The_Borne()
    {
        var line = Line(Friday, "08:02");

        Assert.Throws<InvalidOperationException>(() => line.DeclareDeparture(FridayAt("17:00"), null, Friday, FridayAt("17:01")));
    }

    [Fact]
    public void A_Closed_Line_Cannot_Be_Declared_Again()
    {
        var thursday = Friday.AddDays(-1);
        var line = Line(thursday, "08:02");
        line.DeclareDeparture(AttendanceRules.ParseLocalTime(thursday, "17:30")!.Value, null, Friday, FridayAt("08:05"));

        Assert.Throws<InvalidOperationException>(() =>
            line.DeclareDeparture(AttendanceRules.ParseLocalTime(thursday, "18:00")!.Value, null, Friday, FridayAt("08:06")));
    }

    [Fact]
    public void A_Declared_Departure_Must_Come_After_The_Arrival()
    {
        var thursday = Friday.AddDays(-1);
        var line = Line(thursday, "08:02");

        Assert.Throws<ArgumentException>(() =>
            line.DeclareDeparture(AttendanceRules.ParseLocalTime(thursday, "08:02")!.Value, null, Friday, FridayAt("08:05")));
        Assert.True(line.IsOpen);
    }

    [Fact]
    public void An_Admins_Correction_Of_A_Declared_Line_Reads_Corrected()
    {
        var thursday = Friday.AddDays(-1);
        var line = Line(thursday, "08:02");
        var declared = AttendanceRules.ParseLocalTime(thursday, "17:30")!.Value;
        line.DeclareDeparture(declared, null, Friday, FridayAt("08:05"));

        line.Correct(line.ArrivalUtc, declared, "Vérifié avec elle", "local|admin", FridayAt("09:00"));

        Assert.False(line.IsDeclared);
        Assert.True(line.IsCorrected);
        Assert.Equal(AttendanceDepartureSource.CorrectedByAdmin, line.DepartureSource);
    }

    [Fact]
    public void The_Sheet_Reads_A_Declared_Line_With_Her_Last_Save_And_The_Totals_Count_It()
    {
        var thursday = Friday.AddDays(-1);
        var line = Line(thursday, "08:00");
        line.DeclareDeparture(AttendanceRules.ParseLocalTime(thursday, "17:30")!.Value,
            AttendanceRules.ParseLocalTime(thursday, "17:46"), Friday, FridayAt("08:05"));

        var dto = AttendanceRules.ToDto(line, new Dictionary<string, User>(), Friday);
        var totals = AttendanceRules.Totals("local|ines", new[] { line }, Friday);

        Assert.True(dto.DepartureDeclared);
        Assert.Equal("17:46", dto.DeclaredLastSave);
        Assert.Equal("Déclaré · dernière action 17:46", AttendanceRules.StateLabel(dto));
        Assert.Equal(1, totals.DeclaredDepartures);
        Assert.Equal(570, totals.Minutes);
        Assert.Equal(0, totals.MissingDepartures);
    }

    [Theory]
    [InlineData(false, false, false, false, null, "")]
    [InlineData(true, false, false, false, null, "Départ non pointé")]
    [InlineData(false, true, false, false, null, "En cours")]
    [InlineData(false, false, true, false, null, "Corrigé")]
    [InlineData(false, false, false, true, null, "Déclaré · aucune action ce jour")]
    [InlineData(false, false, true, true, "17:46", "Corrigé · Déclaré · dernière action 17:46")]
    public void The_Export_State_Keeps_Every_Fact_Of_A_Closed_Line(
        bool missing, bool present, bool corrected, bool declared, string? lastSave, string expected)
    {
        var dto = new AttendanceEntryDto
        {
            DepartureMissing = missing, IsPresent = present, IsCorrected = corrected,
            DepartureDeclared = declared, DeclaredLastSave = lastSave,
        };

        Assert.Equal(expected, AttendanceRules.StateLabel(dto));
    }

    // ---- the reminder's bell row ------------------------------------------------------------------------------

    [Fact]
    public void The_Reminder_Is_For_Her_Alone_And_Opens_Mon_Profil()
    {
        var clinicId = Guid.NewGuid();
        var row = StaffNotification.ForDepartureMissing(
            Guid.NewGuid(), clinicId, AttendanceLockRules.ReminderTitle, AttendanceLockRules.ReminderMessage("08:02", new[] { "Accueil" }),
            "local|ines", FridayAt("17:16"));

        Assert.Equal(NotificationCategory.AttendanceDepartureMissing, row.Category);
        Assert.Equal(NotificationTargetKind.Attendance, row.TargetKind);
        Assert.Equal("local|ines", row.TargetUserId);
        Assert.Null(row.TargetRole);
        Assert.Equal("Départ non pointé", row.Title);
        Assert.Equal(clinicId, row.ClinicId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_Reminder_With_No_Target_Is_Refused(string target)
    {
        Assert.Throws<ArgumentException>(() => StaffNotification.ForDepartureMissing(
            Guid.NewGuid(), Guid.NewGuid(), "Départ non pointé", "…", target, DateTime.UtcNow));
    }
}
