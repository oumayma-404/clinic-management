using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// What an admin does to the pointage, and what a secretary does to her own schéma: who may, what is refused by name,
/// that another cabinet's borne, line or account reads as not found, and that a correction changes the hours that
/// count while what the borne recorded stays as it was.
/// </summary>
public class AttendanceAdminCommandTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Motif = "Oubli de pointer le départ";

    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<ILocalAuthService> _hasher = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<User> _roster = new();
    private readonly List<AttendanceEntry> _lines = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");
    private readonly User _ines;
    private readonly User _admin;
    private readonly User _doctor;

    public AttendanceAdminCommandTests()
    {
        _clinic.SetAttendanceEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns<string>(text => $"HASH:{text}");
        _entries.Setup(r => r.ListBetweenAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, DateOnly from, DateOnly to, string? userId, CancellationToken _) =>
                _lines.Where(e => e.LocalDay >= from && e.LocalDay <= to && (userId is null || e.UserId == userId)).ToList());

        _ines = Account(User.RoleSecretary, "Ines Gharbi");
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
        _doctor = Account(User.RoleDoctor, "Dr Karim Hamdi");
    }

    private User Account(string role, string name, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        if (clinicId is null || clinicId == ClinicId) _roster.Add(user);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private void SignIn(User user) => _context.Setup(c => c.GetUserId()).Returns(user.Id);

    private static DateOnly Today => AttendanceRules.Today();

    private static DateTime At(DateOnly day, string hhmm) => AttendanceRules.ParseLocalTime(day, hhmm)!.Value;

    /// <summary>A line the borne recorded on <paramref name="day"/>: 08:02, and a departure at 16:58 unless left open.</summary>
    private AttendanceEntry BorneLine(DateOnly day, bool closed = true, Guid? clinicId = null)
    {
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), clinicId ?? ClinicId, _ines.Id, day, Guid.NewGuid(), "Accueil", At(day, "08:02"));
        if (closed) line.Depart(At(day, "16:58"), day);
        _entries.Setup(r => r.GetByIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        if ((clinicId ?? ClinicId) == ClinicId) _lines.Add(line);
        return line;
    }

    private void VerifyNoSave() => _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);

    private RevokeAttendanceTerminalCommandHandler Revoke() =>
        new(_terminals.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private ResetAttendanceSchemaCommandHandler ResetSchema() =>
        new(_users.Object, _clinics.Object, _context.Object, _uow.Object);

    private SetAttendanceProfileCommandHandler Profile() =>
        new(_users.Object, _clinics.Object, _context.Object, _uow.Object);

    private CorrectAttendanceEntryCommandHandler Correct() =>
        new(_entries.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private RecordAttendanceEntryCommandHandler Record() =>
        new(_entries.Object, _users.Object, _clinics.Object, _context.Object, _uow.Object);

    private SetMyAttendanceSchemaCommandHandler MySchema() =>
        new(_users.Object, _clinics.Object, _context.Object, _hasher.Object, _uow.Object);

    // ---- « Retirer » --------------------------------------------------------------------------------------

    [Fact]
    public async Task An_Admin_Retires_A_Borne_Of_Her_Cabinet()
    {
        var borne = AttendanceTerminal.Register(Guid.NewGuid(), ClinicId, "Accueil", SessionCredential.Hash("s"), _admin.Id, DateTime.UtcNow);
        _terminals.Setup(r => r.GetByIdAsync(borne.Id, It.IsAny<CancellationToken>())).ReturnsAsync(borne);
        SignIn(_admin);

        var result = await Revoke().Handle(new RevokeAttendanceTerminalCommand { Id = borne.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(borne.IsActive);
        Assert.False(result.Value!.IsActive);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Another_Cabinets_Borne_Reads_As_Not_Found()
    {
        var foreign = AttendanceTerminal.Register(Guid.NewGuid(), OtherClinicId, "Ailleurs", SessionCredential.Hash("s"), "x", DateTime.UtcNow);
        _terminals.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        SignIn(_admin);

        var result = await Revoke().Handle(new RevokeAttendanceTerminalCommand { Id = foreign.Id }, default);

        Assert.Equal(AttendanceRules.NotFoundCode, result.Code);
        Assert.True(foreign.IsActive);
        VerifyNoSave();
    }

    [Fact]
    public async Task A_Secretary_Cannot_Retire_A_Borne()
    {
        SignIn(_ines);

        var result = await Revoke().Handle(new RevokeAttendanceTerminalCommand { Id = Guid.NewGuid() }, default);

        Assert.Equal(AttendanceRules.AdminOnly, result.Error);
        VerifyNoSave();
    }

    // ---- « Réinitialiser le schéma » -----------------------------------------------------------------------

    [Fact]
    public async Task A_Reset_Clears_The_Schema_And_Lifts_The_Lock()
    {
        _ines.SetAttendanceSchema("HASH:0-1-2-5");
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            _ines.RegisterSchemaFailure(DateTime.UtcNow);
        SignIn(_admin);

        var result = await ResetSchema().Handle(new ResetAttendanceSchemaCommand { UserId = _ines.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_ines.HasAttendanceSchema);
        Assert.False(_ines.IsSchemaLocked(DateTime.UtcNow));
        Assert.False(result.Value!.HasSchema);
        Assert.Null(result.Value.LockedUntil);
        _users.Verify(r => r.Update(_ines), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Account_Of_Another_Cabinet_Cannot_Be_Reset()
    {
        var stranger = Account(User.RoleSecretary, "Amira Autre", OtherClinicId);
        stranger.SetAttendanceSchema("HASH:0-1-2-5");
        SignIn(_admin);

        var result = await ResetSchema().Handle(new ResetAttendanceSchemaCommand { UserId = stranger.Id }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        Assert.True(stranger.HasAttendanceSchema);
        VerifyNoSave();
    }

    // ---- « Personnes qui pointent » ------------------------------------------------------------------------

    [Fact]
    public async Task Ticking_What_The_Role_Already_Says_Stores_Follow_The_Role()
    {
        SignIn(_admin);

        var result = await Profile().Handle(new SetAttendanceProfileCommand { UserId = _ines.Id, Tracked = true }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(_ines.AttendanceTrackedOverride);
        Assert.True(_ines.IsAttendanceTracked);
        Assert.Null(result.Value!.TrackedOverride);
    }

    [Fact]
    public async Task Ticking_A_Doctor_Is_Stored_As_An_Override_That_Survives_Her_Role()
    {
        SignIn(_admin);

        var result = await Profile().Handle(new SetAttendanceProfileCommand { UserId = _doctor.Id, Tracked = true, Version = 9 }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(_doctor.AttendanceTrackedOverride);
        Assert.True(result.Value!.Tracked);
        _uow.Verify(u => u.SetExpectedVersion(_doctor, 9u), Times.Once);
        _users.Verify(r => r.Update(_doctor), Times.Once);
    }

    [Fact]
    public async Task Unticking_A_Secretary_Takes_Her_Off_The_Borne_And_Null_Gives_Her_Back_To_Her_Role()
    {
        SignIn(_admin);

        await Profile().Handle(new SetAttendanceProfileCommand { UserId = _ines.Id, Tracked = false }, default);
        Assert.False(_ines.AttendanceTrackedOverride);
        Assert.False(_ines.IsAttendanceTracked);

        await Profile().Handle(new SetAttendanceProfileCommand { UserId = _ines.Id, Tracked = null }, default);
        Assert.Null(_ines.AttendanceTrackedOverride);
        Assert.True(_ines.IsAttendanceTracked);
    }

    [Fact]
    public async Task Who_Pointe_Is_Changed_By_An_Admin_Of_This_Cabinet_Only()
    {
        var stranger = Account(User.RoleSecretary, "Amira Autre", OtherClinicId);
        SignIn(_admin);
        Assert.Equal(StaffLeaveRules.NotFoundCode,
            (await Profile().Handle(new SetAttendanceProfileCommand { UserId = stranger.Id, Tracked = false }, default)).Code);

        SignIn(_ines);
        Assert.Equal(AttendanceRules.AdminOnly,
            (await Profile().Handle(new SetAttendanceProfileCommand { UserId = _ines.Id, Tracked = false }, default)).Error);

        Assert.Null(stranger.AttendanceTrackedOverride);
        Assert.Null(_ines.AttendanceTrackedOverride);
        VerifyNoSave();
    }

    // ---- « Corriger » -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Correction_Changes_The_Hours_That_Count_And_Keeps_What_The_Borne_Recorded()
    {
        var day = Today.AddDays(-2);
        var line = BorneLine(day);
        SignIn(_admin);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand
        {
            Id = line.Id, Arrival = "08:30", Departure = "17:00", Reason = $"  {Motif}  ", Version = 7,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(At(day, "08:30"), line.ArrivalUtc);
        Assert.Equal(At(day, "17:00"), line.DepartureUtc);
        Assert.Equal(At(day, "08:02"), line.PunchedArrivalUtc);
        Assert.Equal(At(day, "16:58"), line.PunchedDepartureUtc);
        Assert.Equal(AttendanceDepartureSource.CorrectedByAdmin, line.DepartureSource);
        Assert.Equal(Motif, line.CorrectionReason);
        Assert.Equal(_admin.Id, line.CorrectedByUserId);

        var dto = result.Value!;
        Assert.Equal("08:30", dto.Arrival);
        Assert.Equal("17:00", dto.Departure);
        Assert.Equal("08:02", dto.PunchedArrival);
        Assert.Equal("16:58", dto.PunchedDeparture);
        Assert.True(dto.IsCorrected);
        Assert.Equal("Dr Salma Ben Youssef", dto.CorrectedByName);
        Assert.Equal(510, dto.Minutes);
        _uow.Verify(u => u.SetExpectedVersion(line, 7u), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Correction_That_Keeps_The_Bornes_Departure_Still_Says_The_Borne()
    {
        var day = Today.AddDays(-2);
        var line = BorneLine(day);
        SignIn(_admin);

        await Correct().Handle(new CorrectAttendanceEntryCommand { Id = line.Id, Arrival = "08:00", Departure = "16:58", Reason = Motif }, default);

        Assert.Equal(AttendanceDepartureSource.Borne, line.DepartureSource);
    }

    [Fact]
    public async Task A_Missing_Departure_Is_Closed_Only_By_An_Admin_With_A_Motif()
    {
        var day = Today.AddDays(-1);
        var line = BorneLine(day, closed: false);
        SignIn(_admin);

        var refused = await Correct().Handle(new CorrectAttendanceEntryCommand { Id = line.Id, Arrival = "08:02", Departure = "17:00", Reason = "  " }, default);

        Assert.True(refused.IsFailure);
        Assert.Equal("Indiquez le motif de la correction.", refused.Error);
        Assert.True(line.IsOpen);
        Assert.False(line.IsCorrected);
        VerifyNoSave();

        var closed = await Correct().Handle(new CorrectAttendanceEntryCommand { Id = line.Id, Arrival = "08:02", Departure = "17:00", Reason = Motif }, default);

        Assert.True(closed.IsSuccess, closed.Error);
        Assert.False(closed.Value!.DepartureMissing);
        Assert.Null(line.PunchedDepartureUtc);
    }

    [Theory]
    [InlineData("8h30", "17:00", AttendanceRules.TimeRequired)]
    [InlineData("08:30", "5pm", AttendanceRules.TimeRequired)]
    [InlineData("", null, AttendanceRules.TimeRequired)]
    [InlineData("17:00", "08:30", "Le départ doit être après l'arrivée.")]
    public async Task Hours_That_Cannot_Be_Read_Or_Run_Backwards_Are_Refused(string arrival, string? departure, string expected)
    {
        var line = BorneLine(Today.AddDays(-2));
        var before = line.ArrivalUtc;
        SignIn(_admin);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand { Id = line.Id, Arrival = arrival, Departure = departure, Reason = Motif }, default);

        Assert.Equal(expected, result.Error);
        Assert.Equal(before, line.ArrivalUtc);
        VerifyNoSave();
    }

    [Fact]
    public async Task Another_Cabinets_Line_Reads_As_Not_Found()
    {
        var foreign = BorneLine(Today.AddDays(-2), clinicId: OtherClinicId);
        SignIn(_admin);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand { Id = foreign.Id, Arrival = "09:00", Departure = "17:00", Reason = Motif }, default);

        Assert.Equal(AttendanceRules.NotFoundCode, result.Code);
        Assert.False(foreign.IsCorrected);
        VerifyNoSave();
    }

    [Fact]
    public async Task A_Secretary_Cannot_Correct_Her_Own_Line()
    {
        var line = BorneLine(Today.AddDays(-2));
        SignIn(_ines);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand { Id = line.Id, Arrival = "07:00", Departure = "19:00", Reason = Motif }, default);

        Assert.Equal(AttendanceRules.AdminOnly, result.Error);
        Assert.False(line.IsCorrected);
    }

    // ---- « Ajouter un pointage » ---------------------------------------------------------------------------

    [Fact]
    public async Task An_Admin_Adds_A_Day_The_Borne_Never_Saw_With_A_Motif()
    {
        var day = Today.AddDays(-3);
        SignIn(_admin);
        AttendanceEntry? added = null;
        _entries.Setup(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceEntry, CancellationToken>((e, _) => added = e);

        var result = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = day, Arrival = "08:30", Departure = "16:30", Reason = "Borne en panne",
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.NotNull(added);
        Assert.Equal(ClinicId, added!.ClinicId);
        Assert.Equal(day, added.LocalDay);
        Assert.Equal(At(day, "08:30"), added.ArrivalUtc);
        Assert.Equal(At(day, "16:30"), added.DepartureUtc);
        Assert.Null(added.PunchedArrivalUtc);
        Assert.Null(added.TerminalId);
        Assert.Equal(_admin.Id, added.CorrectedByUserId);
        Assert.True(result.Value!.IsCorrected);
        Assert.Equal(480, result.Value.Minutes);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Added_Line_May_Not_Overlap_A_Line_The_Borne_Recorded()
    {
        var day = Today.AddDays(-2);
        BorneLine(day);
        SignIn(_admin);

        var overlapping = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = day, Arrival = "08:00", Departure = "17:00", Reason = "Borne en panne",
        }, default);
        var evening = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = day, Arrival = "18:00", Departure = "19:00", Reason = "Garde du soir",
        }, default);

        Assert.Equal(AttendanceRules.Overlaps, overlapping.Error);
        Assert.True(evening.IsSuccess, evening.Error);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Day_Never_Holds_Two_Open_Lines()
    {
        var today = Today;
        BorneLine(today, closed: false);
        SignIn(_admin);

        var result = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = today, Arrival = "00:01", Departure = null, Reason = "Oubli",
        }, default);

        Assert.Equal(AttendanceRules.Overlaps, result.Error);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Correction_May_Not_Run_Into_The_Afternoon_Line()
    {
        var day = Today.AddDays(-2);
        var morning = BorneLine(day);
        var afternoon = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, day, Guid.NewGuid(), "Accueil", At(day, "17:30"));
        afternoon.Depart(At(day, "19:00"), day);
        _lines.Add(afternoon);
        SignIn(_admin);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand
        {
            Id = morning.Id, Arrival = "08:02", Departure = null, Reason = Motif,
        }, default);

        Assert.Equal(AttendanceRules.Overlaps, result.Error);
        Assert.False(morning.IsOpen);
        VerifyNoSave();
    }

    [Fact]
    public async Task A_Missing_Account_Id_Reads_As_Not_Found()
    {
        SignIn(_admin);

        var result = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = null!, Day = Today.AddDays(-1), Arrival = "08:30", Reason = Motif,
        }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
    }

    [Fact]
    public async Task Changing_Only_The_Arrival_Keeps_The_Bornes_Exact_Departure()
    {
        var day = Today.AddDays(-2);
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, day, Guid.NewGuid(), "Accueil", At(day, "08:02").AddSeconds(41));
        var departedAt = At(day, "16:58").AddSeconds(37);
        line.Depart(departedAt, day);
        _entries.Setup(r => r.GetByIdAsync(line.Id, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        _lines.Add(line);
        SignIn(_admin);

        var result = await Correct().Handle(new CorrectAttendanceEntryCommand
        {
            Id = line.Id, Arrival = "07:45", Departure = "16:58", Reason = Motif,
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(At(day, "07:45"), line.ArrivalUtc);
        Assert.Equal(departedAt, line.DepartureUtc);
        Assert.Equal(AttendanceDepartureSource.Borne, line.DepartureSource);
    }

    [Fact]
    public async Task An_Added_Line_Without_A_Departure_Invents_None()
    {
        var day = Today.AddDays(-3);
        SignIn(_admin);

        var result = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = day, Arrival = "08:30", Departure = null, Reason = "Borne en panne",
        }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Value!.Departure);
        Assert.Null(result.Value.Minutes);
        Assert.True(result.Value.DepartureMissing);
    }

    [Fact]
    public async Task A_Day_To_Come_Cannot_Be_Added()
    {
        SignIn(_admin);

        var tomorrow = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = _ines.Id, Day = Today.AddDays(1), Arrival = "08:30", Reason = Motif,
        }, default);
        var noDay = await Record().Handle(new RecordAttendanceEntryCommand { UserId = _ines.Id, Arrival = "08:30", Reason = Motif }, default);

        Assert.True(tomorrow.IsFailure);
        Assert.True(noDay.IsFailure);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Line_Cannot_Be_Added_For_Another_Cabinets_Account()
    {
        var stranger = Account(User.RoleSecretary, "Amira Autre", OtherClinicId);
        SignIn(_admin);

        var result = await Record().Handle(new RecordAttendanceEntryCommand
        {
            UserId = stranger.Id, Day = Today.AddDays(-1), Arrival = "08:30", Reason = Motif,
        }, default);

        Assert.Equal(StaffLeaveRules.NotFoundCode, result.Code);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- « Schéma de pointage » in Mon profil --------------------------------------------------------------

    [Fact]
    public async Task A_Secretary_Saves_Her_Schema_Hashed_Like_A_Password()
    {
        _ines.RegisterSchemaFailure(DateTime.UtcNow);
        SignIn(_ines);

        var result = await MySchema().Handle(new SetMyAttendanceSchemaCommand { Pattern = new List<int> { 0, 4, 8, 5 } }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("HASH:0-4-8-5", _ines.AttendanceSchemaHash);
        Assert.Equal(0, _ines.SchemaFailedAttempts);
        Assert.True(result.Value!.HasSchema);
        _users.Verify(r => r.Update(_ines), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(new[] { 0, 1, 2 }, "Reliez au moins 4 points.")]
    [InlineData(new[] { 0, 1, 2, 1 }, "Chaque point ne sert qu'une fois.")]
    [InlineData(new[] { 0, 1, 2, 9 }, "Ce schéma n'est pas valide.")]
    public async Task A_Schema_That_Is_Not_4_To_9_Distinct_Dots_Is_Refused(int[] pattern, string expected)
    {
        SignIn(_ines);

        var result = await MySchema().Handle(new SetMyAttendanceSchemaCommand { Pattern = pattern.ToList() }, default);

        Assert.Equal(expected, result.Error);
        Assert.False(_ines.HasAttendanceSchema);
        VerifyNoSave();
    }

    [Fact]
    public async Task An_Account_That_Does_Not_Pointe_Has_No_Schema_To_Save()
    {
        SignIn(_doctor);

        var result = await MySchema().Handle(new SetMyAttendanceSchemaCommand { Pattern = new List<int> { 0, 1, 2, 5 } }, default);

        Assert.Equal(AttendanceRules.NotTrackedSelf, result.Error);
        Assert.False(_doctor.HasAttendanceSchema);
        VerifyNoSave();
    }

    [Fact]
    public async Task No_Schema_Is_Saved_While_The_Pointage_Is_Off()
    {
        _clinic.SetAttendanceEnabled(false);
        SignIn(_ines);

        var result = await MySchema().Handle(new SetMyAttendanceSchemaCommand { Pattern = new List<int> { 0, 1, 2, 5 } }, default);

        Assert.Equal(AttendanceRules.ModuleOffCode, result.Code);
        VerifyNoSave();
    }
}
