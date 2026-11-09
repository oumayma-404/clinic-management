using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// `pointage-obligatoire`: what every signed-in screen asks — locked or not, which bornes to name, whether she is present
/// (« Pointer votre départ ? »), and whether THIS device is one of her cabinet's bornes. It never refuses: a cabinet
/// without the pointage, or an account that does not pointer, simply reads « not locked ».
/// </summary>
public class ClockStatusQueryTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Now = AttendanceRules.ParseLocalTime(new DateOnly(2026, 11, 6), "10:00")!.Value;
    // Open every day for the whole day; every query is judged at the pinned « Now », never at the suite's own clock.
    private static readonly string AllDay = "[" + string.Join(",",
        new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(d => $"{{\"Day\":\"{d}\",\"Enabled\":true,\"From\":\"00:00\",\"To\":\"23:59\"}}")) + "]";

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IStaffLeaveRequestRepository> _leaves = new();
    private readonly Mock<IRelayLocalStatus> _relayLocal = new();
    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<ISubscriptionPolicy> _policy = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly TenantScope _scope = new(NullLogger<TenantScope>.Instance);
    private readonly List<AttendanceEntry> _lines = new();
    private readonly List<AttendanceTerminal> _bornes = new();
    private readonly User _ines;

    public ClockStatusQueryTests()
    {
        _ines = Caller(User.CreateLocalUser(ClinicId, User.RoleSecretary, "ines@cabinet.tn", "HASH", "Ines Gharbi"));
        _ines.SetAttendanceSchema("HASH:0-1-2-5");
        _scope.UseClinic(ClinicId);
        Switches(attendance: true, locking: true);
        _entries.Setup(r => r.ListBetweenAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _lines.ToList());
        _terminals.Setup(r => r.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _bornes.ToList());
        _terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => LiveBornes.Of(_bornes));
    }

    private User Caller(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private void Switches(bool attendance, bool locking) =>
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAttendanceSwitches(attendance, locking, AllDay));

    private AttendanceTerminal Borne(Guid clinicId, string name, string secret)
    {
        var terminal = AttendanceTerminal.Register(Guid.NewGuid(), clinicId, name, SessionCredential.Hash(secret), "admin", Now.AddDays(-3));
        _terminals.Setup(r => r.FindActiveBySecretHashAcrossClinicsAsync(SessionCredential.Hash(secret), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminal);
        if (clinicId == ClinicId) _bornes.Add(terminal);
        return terminal;
    }

    private GetMyClockStatusQueryHandler Handler() => new(
        _context.Object, _users.Object, _clinics.Object, _entries.Object, _terminals.Object, _scope,
        new AttendanceLockEvaluator(_clinics.Object, _entries.Object, _terminals.Object, _leaves.Object, _relayLocal.Object,
            _relays.Object, _policy.Object, _subscriptions.Object));

    [Fact]
    public async Task A_Tracked_Secretary_With_No_Line_Is_Locked_And_Told_The_Active_Bornes()
    {
        Borne(ClinicId, "Accueil", "s-accueil");
        Borne(ClinicId, "Salle 2", "s-salle2").Revoke(DateTime.UtcNow);

        var result = await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default);

        Assert.True(result.IsSuccess, result.Error);
        var status = result.Value!;
        Assert.True(status.Locked);
        Assert.Equal(new[] { "Accueil" }, status.Bornes);
        Assert.Null(status.PresentSince);
        Assert.False(status.CanClockOutHere);
        Assert.True(status.HasSchema);
        Assert.Equal(_ines.Id, status.UserId);
        Assert.Equal("Ines Gharbi", status.Name);
    }

    [Fact]
    public async Task Present_Today_She_Is_Not_Locked_And_The_Sign_Out_Prompt_Reads_Her_Arrival()
    {
        var arrival = Now.AddMinutes(-90);
        _lines.Add(AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, AttendanceRules.Today(Now), Guid.NewGuid(), "Accueil", arrival));
        _entries.Setup(r => r.HasLineOnAsync(ClinicId, _ines.Id, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.False(status.Locked);
        Assert.Equal(AttendanceRules.LocalTime(arrival), status.PresentSince);
    }

    [Fact]
    public async Task A_Tracked_Account_Says_So_And_An_Untracked_One_Does_Not()
    {
        Borne(ClinicId, "Accueil", "s-accueil");
        Assert.True((await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!.Tracked);

        Caller(User.CreateLocalUser(ClinicId, User.RoleDoctor, "nadia@cabinet.tn", "HASH", "Dr Nadia"));
        Assert.False((await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!.Tracked);
    }

    [Fact]
    public async Task After_Her_Departure_She_Reads_Left_At_And_Is_Not_Present()
    {
        var today = AttendanceRules.Today(Now);
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, today, Guid.NewGuid(), "Accueil", Now.AddHours(-3));
        line.Depart(Now.AddMinutes(-20), today);
        _lines.Add(line);

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.Null(status.PresentSince);
        Assert.Equal(AttendanceRules.LocalTime(Now.AddMinutes(-20)), status.LeftAt);
    }

    [Fact]
    public async Task Present_Again_After_Lunch_She_Is_Not_Left()
    {
        var today = AttendanceRules.Today(Now);
        var morning = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, today, Guid.NewGuid(), "Accueil", Now.AddHours(-4));
        morning.Depart(Now.AddHours(-2), today);
        _lines.Add(morning);
        _lines.Add(AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, today, Guid.NewGuid(), "Accueil", Now.AddHours(-1)));

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.Equal(AttendanceRules.LocalTime(Now.AddHours(-1)), status.PresentSince);
        Assert.Null(status.LeftAt);
    }

    [Fact]
    public async Task Five_Wrong_Schemas_Are_Told_With_When_They_Lift()
    {
        Borne(ClinicId, "Accueil", "s-accueil");
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            _ines.RegisterSchemaFailure(Now.AddMinutes(-1));

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.Equal(_ines.SchemaLockedUntilUtc, status.SchemaLockedUntil);
        Assert.NotNull(status.SchemaLockedUntil);
    }

    [Fact]
    public async Task A_Borne_Installed_During_The_Day_Does_Not_Lock_Her_That_Day()
    {
        var terminal = AttendanceTerminal.Register(
            Guid.NewGuid(), ClinicId, "Accueil", SessionCredential.Hash("s-new"), "admin", Now.AddMinutes(-30));
        _bornes.Add(terminal);

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.False(status.Locked);
        Assert.Equal(new[] { "Accueil" }, status.Bornes);
    }

    [Fact]
    public async Task On_One_Of_Her_Cabinets_Bornes_The_Pad_Can_Open_Here()
    {
        Borne(ClinicId, "Accueil", "s-accueil");

        var status = (await Handler().Handle(new GetMyClockStatusQuery { Secret = "s-accueil", NowUtc = Now }, default)).Value!;

        Assert.True(status.CanClockOutHere);
    }

    [Fact]
    public async Task Another_Cabinets_Borne_Is_Not_Hers_To_Punch_On()
    {
        Borne(OtherClinicId, "Ailleurs", "s-ailleurs");

        var status = (await Handler().Handle(new GetMyClockStatusQuery { Secret = "s-ailleurs", NowUtc = Now }, default)).Value!;

        Assert.False(status.CanClockOutHere);
        Assert.Equal(ClinicId, _scope.ClinicId);
    }

    [Fact]
    public async Task With_The_Pointage_Off_Nobody_Is_Locked_And_Nothing_Else_Is_Read()
    {
        Switches(attendance: false, locking: true);

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.False(status.Locked);
        Assert.Empty(status.Bornes);
        Assert.True(status.HasSchema);
        _terminals.Verify(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Account_That_Does_Not_Pointer_Reads_Not_Locked_Rather_Than_A_Refusal()
    {
        var doctor = Caller(User.CreateLocalUser(ClinicId, User.RoleDoctor, "nadia@cabinet.tn", "HASH", "Dr Nadia"));

        var result = await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.Locked);
        Assert.Equal(doctor.Id, result.Value.UserId);
        Assert.False(result.Value.HasSchema);
    }

    [Fact]
    public async Task A_Session_With_No_Account_Is_Refused()
    {
        _context.Setup(c => c.GetUserId()).Returns((string?)null);

        var result = await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default);

        Assert.True(result.IsFailure);
    }

    // ---- a device an admin chose in « Ajouter une borne » --------------------------------------------------

    private AttendanceTerminal WaitingFor(Guid session, string name)
    {
        _context.Setup(c => c.GetSessionFamilyId()).Returns(session);
        var waiting = AttendanceTerminal.AssignToDevice(
            Guid.NewGuid(), ClinicId, name, session, "Ines Gharbi · Chrome sur Windows", "admin", Now.AddMinutes(-1));
        _bornes.Add(waiting);
        _terminals.Setup(r => r.FindPendingForSessionAsync(ClinicId, session, It.IsAny<CancellationToken>())).ReturnsAsync(waiting);
        return waiting;
    }

    [Fact]
    public async Task A_Device_Chosen_As_A_Borne_Is_Told_So_And_The_Waiting_Borne_Is_Not_Offered_As_A_Place_To_Pointer()
    {
        Borne(ClinicId, "Bureau", "s-bureau");
        WaitingFor(Guid.NewGuid(), "Accueil");

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.Equal("Accueil", status.BorneWaiting);
        Assert.Equal(new[] { "Bureau" }, status.Bornes);
    }

    [Fact]
    public async Task An_Untracked_Doctors_Device_Still_Learns_It_Was_Chosen_As_A_Borne()
    {
        Caller(User.CreateLocalUser(ClinicId, User.RoleDoctor, "karim@cabinet.tn", "HASH", "Dr Karim Hamdi"));
        WaitingFor(Guid.NewGuid(), "Accueil");

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.False(status.Locked);
        Assert.Equal("Accueil", status.BorneWaiting);
    }

    [Fact]
    public async Task Another_Sessions_Waiting_Borne_Is_Not_This_Devices()
    {
        Borne(ClinicId, "Bureau", "s-bureau");
        WaitingFor(Guid.NewGuid(), "Accueil");
        _context.Setup(c => c.GetSessionFamilyId()).Returns(Guid.NewGuid());

        var status = (await Handler().Handle(new GetMyClockStatusQuery { NowUtc = Now }, default)).Value!;

        Assert.Null(status.BorneWaiting);
    }
}
