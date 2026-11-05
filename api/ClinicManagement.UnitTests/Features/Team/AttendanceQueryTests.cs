using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// The reads: the admin's month sheet and its CSV, a secretary's « Mes présences », « Équipe aujourd'hui » and the
/// réglages. Who may read each, that a month key and an account are checked before anything is read, and that every
/// read is scoped to the caller's cabinet. Sheet arithmetic itself is <see cref="AttendanceRulesTests"/>' business.
/// </summary>
public class AttendanceQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Month = "2026-10";
    private static readonly DateOnly Oct1 = new(2026, 10, 1);
    private static readonly DateOnly Oct31 = new(2026, 10, 31);

    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IStaffLeaveRequestRepository> _leaves = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly List<User> _roster = new();
    private readonly List<AttendanceEntry> _lines = new();
    private readonly List<StaffLeaveRequest> _accepted = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");
    private readonly User _ines;
    private readonly User _leila;
    private readonly User _admin;

    public AttendanceQueryTests()
    {
        _clinic.SetAttendanceEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _entries.Setup(r => r.ListBetweenAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly from, DateOnly to, string? userId, CancellationToken _) =>
                _lines.Where(e => e.LocalDay >= from && e.LocalDay <= to && (userId is null || e.UserId == userId)).ToList());
        _leaves.Setup(r => r.GetApprovedOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _accepted.ToList());

        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _leila = Account(User.RoleSecretary, "Leila Trabelsi");
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
    }

    private User Account(string role, string name, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        if (clinicId is null || clinicId == ClinicId) _roster.Add(user);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private void SignIn(User user) => _context.Setup(c => c.GetUserId()).Returns(user.Id);

    private static DateOnly Today => AttendanceRules.Today();

    private AttendanceEntry Line(User who, DateOnly day, string arrival, string? departure)
    {
        var line = AttendanceEntry.Arrive(
            Guid.NewGuid(), ClinicId, who.Id, day, Guid.NewGuid(), "Accueil", AttendanceRules.ParseLocalTime(day, arrival)!.Value);
        if (departure is not null) line.Depart(AttendanceRules.ParseLocalTime(day, departure)!.Value, day);
        _lines.Add(line);
        return line;
    }

    private GetAttendanceSheetQueryHandler Sheet() =>
        new(_entries.Object, _leaves.Object, _users.Object, _clinics.Object, _context.Object);

    private ExportAttendanceQueryHandler Export() =>
        new(_entries.Object, _leaves.Object, _users.Object, _clinics.Object, _context.Object);

    private GetMyAttendanceQueryHandler Mine() =>
        new(_entries.Object, _leaves.Object, _users.Object, _clinics.Object, _context.Object);

    private GetTeamTodayQueryHandler TeamToday() =>
        new(_entries.Object, _leaves.Object, _users.Object, _clinics.Object, _context.Object);

    private GetAttendanceSettingsQueryHandler Settings() =>
        new(_terminals.Object, _users.Object, _clinics.Object, _context.Object);

    private void VerifyNothingRead() =>
        _entries.Verify(r => r.ListBetweenAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

    // ---- the month sheet ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_Admin_Reads_A_Month_For_The_Whole_Team_On_Her_Cabinet()
    {
        Line(_ines, new DateOnly(2026, 10, 5), "08:00", "16:00");
        Line(_leila, new DateOnly(2026, 10, 6), "09:00", "13:00");
        SignIn(_admin);

        var result = await Sheet().Handle(new GetAttendanceSheetQuery { Month = Month }, default);

        Assert.True(result.IsSuccess, result.Error);
        var sheet = result.Value!;
        Assert.Equal(31, sheet.Days.Count);
        Assert.Equal(new[] { "Ines Gharbi", "Leila Trabelsi" }, sheet.People.Select(p => p.FullName));
        Assert.Equal(480, sheet.Totals.Single(t => t.UserId == _ines.Id).Minutes);
        _entries.Verify(r => r.ListBetweenAsync(ClinicId, Oct1, Oct31, null, It.IsAny<CancellationToken>()), Times.Once);
        _leaves.Verify(r => r.GetApprovedOverlappingAsync(ClinicId, Oct1, Oct31, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Secretarys_File_Reads_Her_Lines_Only()
    {
        Line(_ines, new DateOnly(2026, 10, 5), "08:00", "16:00");
        Line(_leila, new DateOnly(2026, 10, 6), "09:00", "13:00");
        SignIn(_admin);

        var result = await Sheet().Handle(new GetAttendanceSheetQuery { Month = Month, UserId = _leila.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(_leila.Id, Assert.Single(result.Value!.People).UserId);
        Assert.All(result.Value.Days.SelectMany(d => d.Entries), e => Assert.Equal(_leila.Id, e.UserId));
        _entries.Verify(r => r.ListBetweenAsync(ClinicId, Oct1, Oct31, _leila.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task No_Month_Means_The_Cabinets_Current_Month()
    {
        SignIn(_admin);

        var result = await Sheet().Handle(new GetAttendanceSheetQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Contains(result.Value!.Days, d => d.Day == Today && d.IsToday);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("octobre")]
    public async Task A_Month_That_Is_Not_One_Is_Refused_Before_Anything_Is_Read(string month)
    {
        SignIn(_admin);

        var result = await Sheet().Handle(new GetAttendanceSheetQuery { Month = month }, default);

        Assert.Equal(AttendanceRules.MonthInvalid, result.Error);
        VerifyNothingRead();
    }

    [Fact]
    public async Task Another_Cabinets_Account_Has_No_File_Here()
    {
        var stranger = Account(User.RoleSecretary, "Amira Autre", OtherClinicId);
        SignIn(_admin);

        var result = await Sheet().Handle(new GetAttendanceSheetQuery { Month = Month, UserId = stranger.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        VerifyNothingRead();
    }

    [Fact]
    public async Task Only_An_Admin_Reads_The_Teams_Sheet_Its_Export_And_Today()
    {
        SignIn(_ines);

        Assert.Equal(AttendanceRules.AdminOnly, (await Sheet().Handle(new GetAttendanceSheetQuery { Month = Month }, default)).Error);
        Assert.Equal(AttendanceRules.AdminOnly, (await Export().Handle(new ExportAttendanceQuery { Month = Month }, default)).Error);
        Assert.Equal(AttendanceRules.AdminOnly, (await TeamToday().Handle(new GetTeamTodayQuery(), default)).Error);
        Assert.Equal(AttendanceRules.AdminOnly, (await Settings().Handle(new GetAttendanceSettingsQuery(), default)).Error);
        VerifyNothingRead();
    }

    [Fact]
    public async Task With_The_Pointage_Off_Every_Read_Is_Refused_By_Code()
    {
        _clinic.SetAttendanceEnabled(false);
        SignIn(_admin);

        Assert.Equal(AttendanceRules.ModuleOffCode, (await Sheet().Handle(new GetAttendanceSheetQuery { Month = Month }, default)).Code);
        Assert.Equal(AttendanceRules.ModuleOffCode, (await TeamToday().Handle(new GetTeamTodayQuery(), default)).Code);
        SignIn(_ines);
        Assert.Equal(AttendanceRules.ModuleOffCode, (await Mine().Handle(new GetMyAttendanceQuery { Month = Month }, default)).Code);
        VerifyNothingRead();
    }

    // ---- « Exporter » -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Export_Has_A_Row_Per_Line_With_The_Bornes_Hours_Beside_The_Ones_That_Count()
    {
        var corrected = Line(_ines, new DateOnly(2026, 10, 5), "08:02", "16:58");
        var day = corrected.LocalDay;
        corrected.Correct(AttendanceRules.ParseLocalTime(day, "08:30")!.Value, AttendanceRules.ParseLocalTime(day, "17:00")!.Value,
            "Badge oublié", _admin.Id, DateTime.UtcNow);
        Line(_leila, new DateOnly(2026, 10, 6), "09:00", null);
        SignIn(_admin);

        var result = await Export().Handle(new ExportAttendanceQuery { Month = Month }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, result.Value!.RowCount);
        var csv = Encoding.UTF8.GetString(result.Value.ToBytes());
        Assert.Contains("Jour;Personne;Arrivée;Départ;Durée (min);État;Borne;Arrivée borne;Départ borne;Motif de correction", csv);
        Assert.Contains("Ines Gharbi;08:30;17:00;510;Corrigé;Accueil;08:02;16:58;Badge oublié", csv);
        Assert.Contains("Leila Trabelsi;09:00;;;Départ non pointé;Accueil;09:00;;", csv);
    }

    // ---- « Mes présences » --------------------------------------------------------------------------------

    [Fact]
    public async Task A_Secretary_Reads_Her_Own_Month_And_Nobody_Elses()
    {
        _ines.SetAttendanceSchema("HASH:0-1-2-5");
        Line(_ines, new DateOnly(2026, 10, 5), "08:00", "16:00");
        Line(_leila, new DateOnly(2026, 10, 5), "09:00", "17:00");
        SignIn(_ines);

        var result = await Mine().Handle(new GetMyAttendanceQuery { Month = Month }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.Tracked);
        Assert.True(result.Value.HasSchema);
        Assert.Equal(_ines.Id, Assert.Single(result.Value.Sheet.People).UserId);
        Assert.All(result.Value.Sheet.Days.SelectMany(d => d.Entries), e => Assert.Equal(_ines.Id, e.UserId));
        _entries.Verify(r => r.ListBetweenAsync(ClinicId, Oct1, Oct31, _ines.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Account_That_Does_Not_Pointe_Is_Told_So_Rather_Than_Refused()
    {
        SignIn(_admin);

        var result = await Mine().Handle(new GetMyAttendanceQuery { Month = Month }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.Tracked);
        Assert.False(result.Value.HasSchema);
    }

    // ---- « Équipe aujourd'hui » ---------------------------------------------------------------------------

    [Fact]
    public async Task Today_Lists_Who_Is_In_First_And_Who_Is_On_Congé_Last()
    {
        var amira = Account(User.RoleSecretary, "Amira Ben Salah");
        var gone = Account(User.RoleSecretary, "Sana Partie");
        gone.Deactivate();
        Line(_leila, Today, "08:02", null);
        var congé = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, amira.Id, StaffLeaveType.PaidLeave, Today, Today, false, "Mariage", 1m);
        congé.Approve(_admin.Id, 1m);
        _accepted.Add(congé);
        SignIn(_admin);

        var result = await TeamToday().Handle(new GetTeamTodayQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(Today, result.Value!.Day);
        Assert.Equal(new[] { "in", "out", "leave" }, result.Value.People.Select(p => p.State));
        Assert.Equal(new[] { _leila.Id, _ines.Id, amira.Id }, result.Value.People.Select(p => p.UserId));
        _entries.Verify(r => r.ListBetweenAsync(ClinicId, Today.AddDays(-14), Today, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Pending_Congés_Count_Only_When_The_Congés_Are_On()
    {
        _leaves.Setup(r => r.CountPendingAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        SignIn(_admin);

        var off = await TeamToday().Handle(new GetTeamTodayQuery(), default);
        _clinic.SetLeaveRequestsEnabled(true);
        var on = await TeamToday().Handle(new GetTeamTodayQuery(), default);

        Assert.Null(off.Value!.PendingLeaveCount);
        Assert.Equal(2, on.Value!.PendingLeaveCount);
    }

    // ---- Paramètres → « Équipe » --------------------------------------------------------------------------

    [Fact]
    public async Task The_Réglages_List_The_Cabinets_Bornes_And_Everyone_Active_First()
    {
        var gone = Account(User.RoleSecretary, "Aya Partie");
        gone.Deactivate();
        var borne = AttendanceTerminal.Register(Guid.NewGuid(), ClinicId, "Accueil", SessionCredential.Hash("s"), _admin.Id, DateTime.UtcNow);
        _terminals.Setup(r => r.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { borne });
        SignIn(_admin);

        var result = await Settings().Handle(new GetAttendanceSettingsQuery(), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("Accueil", Assert.Single(result.Value!.Terminals).Name);
        Assert.Equal(gone.Id, result.Value.People.Last().UserId);
        Assert.True(result.Value.People.Single(p => p.UserId == _ines.Id).Tracked);
        Assert.False(result.Value.People.Single(p => p.UserId == _admin.Id).Tracked);
        Assert.Null(result.Value.People.Single(p => p.UserId == _ines.Id).TrackedOverride);
    }
}
