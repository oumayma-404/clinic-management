using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// The pointage's pure rules and its two aggregates: the schéma's shape, the cabinet's 24-hour clock, the month's
/// arithmetic (an open line counts its day, never its hours), which borne a secret names, and the sheet the admin, a
/// secretary's file and « Mes présences » all read. Every date is a fixed literal.
/// </summary>
public class AttendanceRulesTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateOnly Sun1Nov = new(2026, 11, 1);
    private static readonly DateOnly Mon2Nov = new(2026, 11, 2);
    private static readonly DateOnly Tue3Nov = new(2026, 11, 3);
    private static readonly DateOnly Wed4Nov = new(2026, 11, 4);
    private static readonly DateOnly Thu5Nov = new(2026, 11, 5);
    private static readonly DateOnly Sat7Nov = new(2026, 11, 7);
    private static readonly DateTime Noon5NovUtc = new(2026, 11, 5, 11, 0, 0, DateTimeKind.Utc);

    private readonly User _ines = Person(User.RoleSecretary, "Ines Gharbi");
    private readonly User _leila = Person(User.RoleSecretary, "Leila Trabelsi");
    private readonly User _admin = Person(User.RoleAdmin, "Dr Salma Ben Youssef");

    private static User Person(string role, string name) =>
        User.CreateLocalUser(ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);

    private static DateTime At(DateOnly day, string hhmm) => AttendanceRules.ParseLocalTime(day, hhmm)!.Value;

    private static AttendanceEntry Line(User who, DateOnly day, string arrival, string? departure)
    {
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, who.Id, day, Guid.NewGuid(), "Accueil", At(day, arrival));
        if (departure is not null) line.Depart(At(day, departure), day);
        return line;
    }

    private StaffLeaveRequest Leave(User who, DateOnly start, DateOnly end, bool halfDay = false, bool approved = true)
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, who.Id, StaffLeaveType.PaidLeave, start, end, halfDay, "Mariage", 1m);
        if (approved) leave.Approve(_admin.Id, halfDay ? 0.5m : 1m);
        return leave;
    }

    // ---- the schéma ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(new[] { 0, 1, 2, 5 }, null)]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, null)]
    [InlineData(new[] { 0, 1, 2 }, "Reliez au moins 4 points.")]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 0 }, "Un schéma relie au plus 9 points.")]
    [InlineData(new[] { 0, 1, 2, -1 }, "Ce schéma n'est pas valide.")]
    [InlineData(new[] { 0, 1, 4, 1 }, "Chaque point ne sert qu'une fois.")]
    public void A_Schema_Is_4_To_9_Distinct_Dots_Of_The_Pad(int[] pattern, string? expected) =>
        Assert.Equal(expected, AttendanceRules.SchemaRefusalFor(pattern));

    [Fact]
    public void No_Pattern_At_All_Is_Refused_Like_A_Short_One() =>
        Assert.Equal("Reliez au moins 4 points.", AttendanceRules.SchemaRefusalFor(null));

    [Fact]
    public void The_Order_The_Dots_Were_Drawn_In_Is_Part_Of_The_Schema() =>
        Assert.NotEqual(AttendanceRules.PatternText(new[] { 0, 1, 2, 5 }), AttendanceRules.PatternText(new[] { 5, 2, 1, 0 }));

    [Fact]
    public void The_Tries_Left_Are_Counted_In_French()
    {
        Assert.Equal("Schéma incorrect. Encore 1 essai avant le blocage.", AttendanceRules.SchemaWrong(1));
        Assert.Equal("Schéma incorrect. Encore 3 essais avant le blocage.", AttendanceRules.SchemaWrong(3));
    }

    [Theory]
    [InlineData(15 * 60, 15)]
    [InlineData(14 * 60 + 1, 15)]
    [InlineData(30, 1)]
    [InlineData(-5, 1)]
    public void The_Lock_Names_The_Minutes_Left_Rounded_Up_And_Never_Zero(int secondsLeft, int minutes) =>
        Assert.Contains($"Réessayez dans {minutes} min", AttendanceRules.SchemaLocked(Noon5NovUtc.AddSeconds(secondsLeft), Noon5NovUtc));

    // ---- the cabinet's clock --------------------------------------------------------------------------------

    [Fact]
    public void A_Time_Is_Read_And_Shown_On_The_Cabinets_24_Hour_Clock()
    {
        Assert.Equal(new DateTime(2026, 11, 3, 7, 30, 0, DateTimeKind.Utc), AttendanceRules.ParseLocalTime(Tue3Nov, "08:30"));
        Assert.Equal("08:02", AttendanceRules.LocalTime(new DateTime(2026, 11, 3, 7, 2, 0, DateTimeKind.Utc)));
        Assert.Equal("17:45", AttendanceRules.LocalTime(new DateTime(2026, 11, 3, 16, 45, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void A_Line_Belongs_To_The_Cabinets_Day_Not_To_UTCs() =>
        Assert.Equal(Wed4Nov, AttendanceRules.LocalDayOf(new DateTime(2026, 11, 3, 23, 30, 0, DateTimeKind.Utc)));

    [Theory]
    [InlineData("8h30")]
    [InlineData("08:30 PM")]
    [InlineData("25:00")]
    [InlineData("8:30")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_That_Is_Not_A_24_Hour_Time_Is_Not_Read(string? text) =>
        Assert.Null(AttendanceRules.ParseLocalTime(Tue3Nov, text));

    [Fact]
    public void An_Admins_Hours_Are_Accepted_With_A_Motif_And_An_Open_Departure()
    {
        var closed = AttendanceRules.HoursRefusalFor(Tue3Nov, "08:30", " 17:00 ", "Oubli", Noon5NovUtc);
        var open = AttendanceRules.HoursRefusalFor(Tue3Nov, "08:30", "  ", "Oubli", Noon5NovUtc);

        Assert.True(closed.IsSuccess, closed.Error);
        Assert.Equal((At(Tue3Nov, "08:30"), (DateTime?)At(Tue3Nov, "17:00")), closed.Value);
        Assert.True(open.IsSuccess, open.Error);
        Assert.Null(open.Value.Departure);
    }

    [Fact]
    public void An_Hour_Still_To_Come_Is_Never_Recorded()
    {
        var result = AttendanceRules.HoursRefusalFor(Thu5Nov, "08:30", "17:00", "Oubli", Noon5NovUtc);

        Assert.Equal(AttendanceRules.InFuture, result.Error);
    }

    [Fact]
    public void A_Motif_Too_Long_Is_Refused()
    {
        var result = AttendanceRules.HoursRefusalFor(Tue3Nov, "08:30", "17:00", new string('x', AttendanceEntry.MaxReasonLength + 1), Noon5NovUtc);

        Assert.Contains("ne peut pas dépasser", result.Error);
    }

    // ---- the month's arithmetic ----------------------------------------------------------------------------

    [Fact]
    public void An_Open_Line_Counts_Its_Day_And_Never_Its_Hours()
    {
        var lines = new[]
        {
            Line(_ines, Mon2Nov, "08:00", "12:00"),
            Line(_ines, Mon2Nov, "13:00", "17:00"),
            Line(_ines, Tue3Nov, "08:00", null),
            Line(_ines, Thu5Nov, "08:00", null),
            Line(_leila, Mon2Nov, "08:00", "18:00"),
        };

        var totals = AttendanceRules.Totals(_ines.Id, lines, Thu5Nov);

        Assert.Equal(3, totals.Days);
        Assert.Equal(480, totals.Minutes);
        Assert.Equal(1, totals.MissingDepartures);
        Assert.True(totals.OpenToday);
    }

    [Fact]
    public void A_Line_Reads_Present_Today_And_Departure_Missing_Afterwards()
    {
        var roster = new Dictionary<string, User> { [_ines.Id] = _ines };
        var openToday = AttendanceRules.ToDto(Line(_ines, Thu5Nov, "08:02", null), roster, Thu5Nov);
        var openYesterday = AttendanceRules.ToDto(Line(_ines, Wed4Nov, "08:02", null), roster, Thu5Nov);

        Assert.True(openToday.IsPresent);
        Assert.False(openToday.DepartureMissing);
        Assert.Null(openToday.Minutes);
        Assert.False(openYesterday.IsPresent);
        Assert.True(openYesterday.DepartureMissing);
        Assert.Null(openYesterday.Departure);
        Assert.Equal("Accueil", openYesterday.TerminalName);
    }

    [Fact]
    public void A_Line_Of_A_Deleted_Account_Still_Reads()
    {
        var dto = AttendanceRules.ToDto(Line(_ines, Mon2Nov, "08:00", "16:00"), new Dictionary<string, User>(), Thu5Nov);

        Assert.Equal("Compte supprimé", dto.UserName);
        Assert.Equal(480, dto.Minutes);
    }

    [Fact]
    public void A_Month_Key_Gives_Its_First_And_Last_Day()
    {
        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)), AttendanceRules.MonthDays("2026-02"));
        Assert.Equal((new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29)), AttendanceRules.MonthDays("2028-02"));
        Assert.Null(AttendanceRules.MonthDays("2026-13"));
        Assert.Null(AttendanceRules.MonthDays("novembre"));
        Assert.Null(AttendanceRules.MonthDays(null));
        Assert.Equal((new DateOnly(9999, 12, 1), new DateOnly(9999, 12, 31)), AttendanceRules.MonthDays("9999-12"));
    }

    [Fact]
    public void Lines_Of_One_Day_Never_Overlap_And_An_Open_Line_Runs_Until_Closed()
    {
        var morning = Line(_ines, Mon2Nov, "08:00", "12:00");
        var open = Line(_ines, Mon2Nov, "14:00", null);
        var day = new[] { morning, open };

        Assert.Null(AttendanceRules.OverlapRefusalFor(day, At(Mon2Nov, "12:00"), At(Mon2Nov, "13:30")));
        Assert.Equal(AttendanceRules.Overlaps, AttendanceRules.OverlapRefusalFor(day, At(Mon2Nov, "11:00"), At(Mon2Nov, "13:00")));
        Assert.Equal(AttendanceRules.Overlaps, AttendanceRules.OverlapRefusalFor(day, At(Mon2Nov, "18:00"), null));
        Assert.Equal(AttendanceRules.Overlaps, AttendanceRules.OverlapRefusalFor(day, At(Mon2Nov, "12:30"), null));
        Assert.Null(AttendanceRules.OverlapRefusalFor(day, At(Mon2Nov, "07:00"), At(Mon2Nov, "13:00"), excludingId: morning.Id));
    }

    [Fact]
    public void Only_A_Whole_Accepted_Day_Of_Congé_Covers_Today()
    {
        Assert.True(AttendanceRules.IsOnFullDayLeave(_ines.Id, new[] { Leave(_ines, Wed4Nov, Sat7Nov) }, Thu5Nov));
        Assert.False(AttendanceRules.IsOnFullDayLeave(_ines.Id, new[] { Leave(_ines, Thu5Nov, Thu5Nov, halfDay: true) }, Thu5Nov));
        Assert.False(AttendanceRules.IsOnFullDayLeave(_ines.Id, new[] { Leave(_ines, Thu5Nov, Thu5Nov, approved: false) }, Thu5Nov));
        Assert.False(AttendanceRules.IsOnFullDayLeave(_ines.Id, new[] { Leave(_leila, Thu5Nov, Thu5Nov) }, Thu5Nov));
        Assert.False(AttendanceRules.IsOnFullDayLeave(_ines.Id, new[] { Leave(_ines, Mon2Nov, Wed4Nov) }, Thu5Nov));
    }

    [Fact]
    public void Initials_Are_The_First_Two_Words_Letters_Like_Every_Avatar()
    {
        Assert.Equal("AB", AttendanceRules.Initials("amira ben salah"));
        Assert.Equal("I", AttendanceRules.Initials("Ines"));
        Assert.Equal("?", AttendanceRules.Initials("  "));
    }

    // ---- the borne's secret --------------------------------------------------------------------------------

    [Fact]
    public void A_Borne_Secret_Is_256_Random_Bits_Url_Safe()
    {
        var first = AttendanceRules.NewTerminalSecret();
        var second = AttendanceRules.NewTerminalSecret();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", first);
        Assert.NotEqual(first, second);
    }

    private static (Mock<IAttendanceTerminalRepository> Repo, AttendanceTerminal Borne) KnownBorne(Guid clinicId)
    {
        var borne = AttendanceTerminal.Register(Guid.NewGuid(), clinicId, "Accueil", SessionCredential.Hash("secret"), "admin", Noon5NovUtc);
        var repo = new Mock<IAttendanceTerminalRepository>();
        repo.Setup(r => r.FindActiveBySecretHashAcrossClinicsAsync(SessionCredential.Hash("secret"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(borne);
        return (repo, borne);
    }

    [Fact]
    public async Task A_Known_Secret_Scopes_An_Anonymous_Request_To_Its_Borne_Cabinet()
    {
        var (repo, borne) = KnownBorne(ClinicId);
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);

        var found = await AttendanceRules.FindBorneAsync(repo.Object, scope, "secret", default);

        Assert.Same(borne, found);
        Assert.Equal(TenantScopeKind.Clinic, scope.Kind);
        Assert.Equal(ClinicId, scope.ClinicId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_Secret_Is_No_Borne_And_Nothing_Is_Looked_Up(string? secret)
    {
        var (repo, _) = KnownBorne(ClinicId);
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);

        Assert.Null(await AttendanceRules.FindBorneAsync(repo.Object, scope, secret, default));
        repo.Verify(r => r.FindActiveBySecretHashAcrossClinicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(TenantScopeKind.Unset, scope.Kind);
    }

    [Fact]
    public async Task A_Request_Scoped_Elsewhere_Or_System_Wide_Never_Gets_The_Borne()
    {
        var (repo, _) = KnownBorne(ClinicId);
        var elsewhere = new TenantScope(NullLogger<TenantScope>.Instance);
        elsewhere.UseClinic(OtherClinicId);
        var systemWide = new TenantScope(NullLogger<TenantScope>.Instance);
        systemWide.UseSystemWide("test");
        var same = new TenantScope(NullLogger<TenantScope>.Instance);
        same.UseClinic(ClinicId);

        Assert.Null(await AttendanceRules.FindBorneAsync(repo.Object, elsewhere, "secret", default));
        Assert.Null(await AttendanceRules.FindBorneAsync(repo.Object, systemWide, "secret", default));
        Assert.NotNull(await AttendanceRules.FindBorneAsync(repo.Object, same, "secret", default));
        Assert.Equal(OtherClinicId, elsewhere.ClinicId);
    }

    // ---- the aggregates ------------------------------------------------------------------------------------

    [Fact]
    public void A_Punch_Never_Closes_A_Line_From_Another_Day()
    {
        var line = Line(_ines, Wed4Nov, "08:00", null);

        Assert.Throws<InvalidOperationException>(() => line.Depart(At(Thu5Nov, "09:00"), Thu5Nov));
        Assert.True(line.IsOpen);
    }

    [Fact]
    public void A_Closed_Line_Cannot_Be_Departed_Twice()
    {
        var line = Line(_ines, Wed4Nov, "08:00", "17:00");

        Assert.Throws<InvalidOperationException>(() => line.Depart(At(Wed4Nov, "18:00"), Wed4Nov));
        Assert.Equal(At(Wed4Nov, "17:00"), line.DepartureUtc);
    }

    [Fact]
    public void A_Correction_That_Clears_The_Departure_Leaves_No_Source()
    {
        var line = Line(_ines, Wed4Nov, "08:00", "17:00");

        line.Correct(At(Wed4Nov, "08:00"), null, "Départ pointé par erreur", _admin.Id, Noon5NovUtc);

        Assert.True(line.IsOpen);
        Assert.Null(line.DepartureSource);
        Assert.Equal(At(Wed4Nov, "17:00"), line.PunchedDepartureUtc);
    }

    [Fact]
    public void An_Added_Line_Carries_No_Borne_Hours_And_Needs_A_Motif()
    {
        var added = AttendanceEntry.RecordByAdmin(
            Guid.NewGuid(), ClinicId, _ines.Id, Wed4Nov, At(Wed4Nov, "08:00"), null, " Borne en panne ", _admin.Id, Noon5NovUtc);

        Assert.Null(added.PunchedArrivalUtc);
        Assert.Null(added.TerminalId);
        Assert.Null(added.DepartureSource);
        Assert.Equal("Borne en panne", added.CorrectionReason);
        Assert.Throws<ArgumentException>(() => AttendanceEntry.RecordByAdmin(
            Guid.NewGuid(), ClinicId, _ines.Id, Wed4Nov, At(Wed4Nov, "08:00"), null, " ", _admin.Id, Noon5NovUtc));
    }

    [Fact]
    public void A_Borne_Name_Is_Trimmed_And_Bounded_And_Retiring_Keeps_The_First_Date()
    {
        var borne = AttendanceTerminal.Register(Guid.NewGuid(), ClinicId, "  Accueil  ", "hash", _admin.Id, Noon5NovUtc);

        Assert.Equal("Accueil", borne.Name);
        Assert.NotNull(AttendanceTerminal.NameRefusalFor(new string('x', AttendanceTerminal.MaxNameLength + 1)));
        Assert.Null(AttendanceTerminal.NameRefusalFor(new string('x', AttendanceTerminal.MaxNameLength)));

        borne.Revoke(Noon5NovUtc);
        borne.Revoke(Noon5NovUtc.AddDays(1));
        Assert.Equal(Noon5NovUtc, borne.RevokedAt);
        Assert.False(borne.IsActive);
    }

    [Fact]
    public void Four_Wrong_Schemas_Count_And_The_Fifth_Locks_For_Fifteen_Minutes()
    {
        for (var i = 1; i < User.MaxSchemaFailures; i++)
            Assert.False(_ines.RegisterSchemaFailure(Noon5NovUtc));
        Assert.Equal(User.MaxSchemaFailures - 1, _ines.SchemaFailedAttempts);

        Assert.True(_ines.RegisterSchemaFailure(Noon5NovUtc));
        Assert.Equal(Noon5NovUtc.AddMinutes(15), _ines.SchemaLockedUntilUtc);
        Assert.Equal(0, _ines.SchemaFailedAttempts);
        Assert.True(_ines.IsSchemaLocked(Noon5NovUtc.AddMinutes(14)));
        Assert.False(_ines.IsSchemaLocked(Noon5NovUtc.AddMinutes(15)));
    }

    [Fact]
    public void Who_Pointe_Follows_The_Role_Unless_An_Admin_Said_Otherwise()
    {
        var doctor = Person(User.RoleDoctor, "Dr Karim Hamdi");

        Assert.True(_ines.IsAttendanceTracked);
        Assert.False(doctor.IsAttendanceTracked);
        Assert.False(_admin.IsAttendanceTracked);

        _ines.SetAttendanceTracked(false);
        doctor.SetAttendanceTracked(true);
        Assert.False(_ines.IsAttendanceTracked);
        Assert.True(doctor.IsAttendanceTracked);
    }

    // ---- the sheet -----------------------------------------------------------------------------------------

    [Fact]
    public void The_Sheet_Has_A_Row_Per_Day_With_Closed_Days_Congés_And_Totals()
    {
        var roster = new Dictionary<string, User> { [_ines.Id] = _ines, [_leila.Id] = _leila, [_admin.Id] = _admin };
        var lines = new[]
        {
            Line(_ines, Mon2Nov, "08:00", "16:30"),
            Line(_ines, Wed4Nov, "08:00", null),
            Line(_ines, Thu5Nov, "08:10", null),
        };
        var leaves = new[]
        {
            Leave(_leila, Tue3Nov, Wed4Nov),
            Leave(_leila, Sat7Nov, Sat7Nov, approved: false),
            Leave(_admin, Tue3Nov, Tue3Nov),
        };

        var sheet = AttendanceSheetBuilder.Build(
            "2026-11", Sun1Nov, new DateOnly(2026, 11, 30), new[] { _ines, _leila }, lines, leaves, roster, null, Thu5Nov);

        Assert.Equal("2026-11", sheet.Month);
        Assert.Equal(30, sheet.Days.Count);
        Assert.False(sheet.Days[0].IsOpenDay);
        Assert.True(sheet.Days[1].IsOpenDay);
        Assert.True(sheet.Days[6].IsOpenDay);
        Assert.Single(sheet.Days, d => d.IsToday);
        Assert.Equal(Thu5Nov, sheet.Days.Single(d => d.IsToday).Day);

        Assert.Equal("08:00", Assert.Single(sheet.Days[1].Entries).Arrival);
        Assert.True(Assert.Single(sheet.Days[3].Entries).DepartureMissing);
        Assert.True(Assert.Single(sheet.Days[4].Entries).IsPresent);

        var tuesday = sheet.Days[2];
        var mark = Assert.Single(tuesday.Leaves);
        Assert.Equal(_leila.Id, mark.UserId);
        Assert.False(mark.HalfDay);
        Assert.Empty(sheet.Days[6].Leaves);

        Assert.Equal(new[] { _ines.Id, _leila.Id }, sheet.People.Select(p => p.UserId));
        var ines = sheet.Totals.Single(t => t.UserId == _ines.Id);
        Assert.Equal(3, ines.Days);
        Assert.Equal(510, ines.Minutes);
        Assert.Equal(1, ines.MissingDepartures);
        Assert.True(ines.OpenToday);
        Assert.Equal(0, sheet.Totals.Single(t => t.UserId == _leila.Id).Days);
    }

    [Fact]
    public void An_Empty_Day_Counts_As_Absent_Only_From_The_First_Line_And_The_Account_Creation()
    {
        var created = AttendanceRules.LocalDayOf(_ines.CreatedAt);

        Assert.Null(AttendanceSheetBuilder.CountsFrom(null, _ines));
        Assert.Equal(created, AttendanceSheetBuilder.CountsFrom(created.AddDays(-30), _ines));
        Assert.Equal(created.AddDays(5), AttendanceSheetBuilder.CountsFrom(created.AddDays(5), _ines));

        var roster = new Dictionary<string, User> { [_ines.Id] = _ines };
        var sheet = AttendanceSheetBuilder.Build(
            "2026-11", Sun1Nov, new DateOnly(2026, 11, 30), new[] { _ines }, Array.Empty<AttendanceEntry>(),
            Array.Empty<StaffLeaveRequest>(), roster, null, Thu5Nov, created.AddDays(5));
        Assert.Equal(created.AddDays(5), Assert.Single(sheet.People).CountsFrom);
    }

    [Fact]
    public void The_Sheet_Has_A_Column_For_Who_Pointe_Plus_Anyone_With_A_Line_That_Month()
    {
        var gone = Person(User.RoleSecretary, "Sana Partie");
        gone.Deactivate();
        var goneWithoutLines = Person(User.RoleSecretary, "Rim Partie");
        goneWithoutLines.Deactivate();
        var doctor = Person(User.RoleDoctor, "Dr Karim Hamdi");
        var roster = new[] { _ines, gone, goneWithoutLines, doctor, _admin }.ToDictionary(u => u.Id);

        var people = AttendanceSheetBuilder.PeopleFor(roster, new[] { Line(gone, Mon2Nov, "08:00", "12:00") });

        Assert.Equal(new[] { "Ines Gharbi", "Sana Partie" }, people.Select(StaffLeaveRules.DisplayName));
    }

    [Fact]
    public void Today_An_Open_Line_Wins_Over_A_Congé_And_An_Old_Open_Line_Is_Named()
    {
        var lines = new[] { Line(_ines, Mon2Nov, "08:00", null), Line(_ines, Thu5Nov, "08:02", null) };
        var amira = Person(User.RoleSecretary, "Amira Ben Salah");
        var leaves = new[] { Leave(_ines, Thu5Nov, Thu5Nov), Leave(_leila, Thu5Nov, Thu5Nov), Leave(amira, Thu5Nov, Thu5Nov, halfDay: true) };

        var ines = TeamTodayRules.PersonToday(_ines, lines, leaves, Noon5NovUtc);
        var leila = TeamTodayRules.PersonToday(_leila, lines, leaves, Noon5NovUtc);
        var halfDay = TeamTodayRules.PersonToday(amira, lines, leaves, Noon5NovUtc);
        var nobody = TeamTodayRules.PersonToday(_admin, lines, leaves, Noon5NovUtc);

        Assert.Equal(AttendanceRules.StateOut, halfDay.State);
        Assert.True(halfDay.LeaveHalfDay);
        Assert.NotNull(halfDay.LeaveTypeLabel);

        Assert.Equal("in", ines.State);
        Assert.Equal("08:02", ines.Since);
        Assert.Equal(Mon2Nov, ines.MissingDepartureOn);
        Assert.Equal("leave", leila.State);
        Assert.NotNull(leila.LeaveTypeLabel);
        Assert.Equal("out", nobody.State);
        Assert.Null(nobody.Since);
    }
}
