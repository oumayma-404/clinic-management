using ClinicManagement.Application.Common.Behaviors;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Subscriptions;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Application.Features.Team.Commands;
using ClinicManagement.Application.Features.Team.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// The borne: an anonymous device proved by its secret, a person proved by her schéma. Which cabinet a punch belongs
/// to, what each refusal is named, that a wrong schéma is counted and locks, and that a punch never closes a line from
/// an earlier day or invents an hour.
/// </summary>
public class PointageBorneTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Secret = "borne-secret-accueil";
    private static readonly List<int> RightPattern = new() { 0, 1, 2, 5 };
    private const string StoredHash = "HASH:0-1-2-5";

    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IStaffLeaveRequestRepository> _leaves = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly Mock<ISubscriptionPolicy> _policy = new();
    private readonly Mock<ILocalAuthService> _hasher = new();
    private readonly Mock<IAuditActorProvider> _audit = new();
    private readonly Mock<IAuditEntryRepository> _journal = new();
    private readonly Mock<IRealtimeNotifier> _realtime = new();
    private readonly Mock<INotificationGenerator> _bell = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<ISessionFamilyRepository> _sessions = new();
    private readonly TenantScope _scope = new(NullLogger<TenantScope>.Instance);
    private readonly List<User> _roster = new();
    private readonly List<AttendanceEntry> _todayLines = new();
    private readonly List<AttendanceEntry> _earlierOpen = new();
    private readonly List<StaffLeaveRequest> _acceptedLeaves = new();
    private readonly Clinic _clinic = new(ClinicId, "Cabinet Test", code: "CODE01");
    private readonly AttendanceTerminal _borne;
    private readonly User _ines;
    private readonly User _admin;

    public PointageBorneTests()
    {
        _clinic.SetAttendanceEnabled(true);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(_clinic);
        _users.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => PagedResult<User>.Unpaged(_roster.ToList()));
        _entries.Setup(r => r.ListBetweenAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _todayLines.ToList());
        _entries.Setup(r => r.ListOpenBeforeAsync(ClinicId, It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string userId, DateOnly day, CancellationToken _) => (IReadOnlyList<AttendanceEntry>)_earlierOpen
                .Where(l => l.UserId == userId && l.LocalDay < day && l.IsOpen).OrderBy(l => l.LocalDay).ToList());
        _leaves.Setup(r => r.GetApprovedOverlappingAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _acceptedLeaves.ToList());
        _hasher.Setup(h => h.HashPassword(It.IsAny<string>())).Returns<string>(text => $"HASH:{text}");
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns<string, string>((hash, text) => hash == $"HASH:{text}" ? PasswordVerificationOutcome.Success : PasswordVerificationOutcome.Failed);

        _borne = Borne(ClinicId, "Accueil", Secret);
        _terminals.Setup(r => r.ListAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(() => new List<AttendanceTerminal> { _borne });
        _ines = Account(User.RoleSecretary, "Ines Gharbi", withSchema: true);
        _admin = Account(User.RoleAdmin, "Dr Salma Ben Youssef");
    }

    private AttendanceTerminal Borne(Guid clinicId, string name, string secret)
    {
        var terminal = AttendanceTerminal.Register(
            Guid.NewGuid(), clinicId, name, SessionCredential.Hash(secret), "admin", DateTime.UtcNow.AddDays(-3));
        _terminals.Setup(r => r.FindActiveBySecretHashAcrossClinicsAsync(SessionCredential.Hash(secret), It.IsAny<CancellationToken>()))
            .ReturnsAsync(terminal);
        _terminals.Setup(r => r.GetByIdAsync(terminal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(terminal);
        return terminal;
    }

    private User Account(string role, string name, bool withSchema = false, Guid? clinicId = null)
    {
        var user = User.CreateLocalUser(clinicId ?? ClinicId, role, $"{name.Replace(' ', '.').ToLowerInvariant()}@cabinet.tn", "HASH", name);
        if (withSchema) user.SetAttendanceSchema(StoredHash);
        if (clinicId is null || clinicId == ClinicId) _roster.Add(user);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }

    private static DateOnly Today => AttendanceRules.Today();

    private AttendanceEntry OpenLine(User owner, DateOnly day)
    {
        var line = AttendanceEntry.Arrive(
            Guid.NewGuid(), ClinicId, owner.Id, day, _borne.Id, _borne.Name, DateTime.UtcNow.AddHours(-2));
        _entries.Setup(r => r.FindLatestOpenAsync(ClinicId, owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        if (day == Today) _todayLines.Add(line);
        else if (day < Today) _earlierOpen.Add(line);
        return line;
    }

    /// <summary>An earlier day's line opened at a cabinet-local hour, left open.</summary>
    private AttendanceEntry OpenLineAt(User owner, DateOnly day, string arrival)
    {
        var line = AttendanceEntry.Arrive(
            Guid.NewGuid(), ClinicId, owner.Id, day, _borne.Id, _borne.Name, AttendanceRules.ParseLocalTime(day, arrival)!.Value);
        _entries.Setup(r => r.FindLatestOpenAsync(ClinicId, owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(line);
        _earlierOpen.Add(line);
        return line;
    }

    private void LastSaveOn(DateTime? lastSaveUtc) =>
        _journal.Setup(j => j.GetLastSaveAtAsync(ClinicId, _ines.Id, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(lastSaveUtc);

    private StaffLeaveRequest AcceptedLeave(User owner, DateOnly start, DateOnly end, bool halfDay = false)
    {
        var leave = new StaffLeaveRequest(Guid.NewGuid(), ClinicId, owner.Id, StaffLeaveType.PaidLeave, start, end, halfDay, "Mariage", 1m);
        leave.Approve(_admin.Id, halfDay ? 0.5m : 1m);
        _acceptedLeaves.Add(leave);
        return leave;
    }

    private PunchCommandHandler Punch() => new(
        _terminals.Object, _entries.Object, _leaves.Object, _users.Object, _clinics.Object, _subscriptions.Object,
        _policy.Object, _scope, _hasher.Object, _audit.Object, _journal.Object, _realtime.Object, Notifier(),
        _uow.Object, NullLogger<PunchCommandHandler>.Instance);

    private AttendanceNotifier Notifier() =>
        new(_bell.Object, _users.Object, _terminals.Object, _scope, NullLogger<AttendanceNotifier>.Instance);

    private GetPointageStatusQueryHandler Status() => new(
        _terminals.Object, _entries.Object, _leaves.Object, _users.Object, _clinics.Object, _scope);

    private RegisterAttendanceTerminalCommandHandler Register() =>
        new(_terminals.Object, _users.Object, _clinics.Object, _context.Object, _sessions.Object, Notifier(), _uow.Object);

    private static PunchCommand Tap(User person, List<int>? pattern = null, string? secret = Secret) =>
        new() { Secret = secret, UserId = person.Id, Pattern = pattern ?? RightPattern };

    private void VerifyNoLineWritten()
    {
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _audit.Verify(a => a.AuthenticatedAs(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    private static ClinicSubscription EndingOn(DateTime endsOn)
    {
        var recordedOn = endsOn.AddMonths(-1).Date;
        var recordedAt = DateTime.SpecifyKind(recordedOn, DateTimeKind.Utc);
        var subscription = ClinicSubscription.For(ClinicId, recordedAt);
        subscription.RecomputeFrom(new[]
        {
            SubscriptionPeriod.Create(ClinicId, SubscriptionPeriodKind.Paid, recordedOnClinicDay: recordedOn,
                recordedAtUtc: recordedAt, explicitEndsOn: endsOn),
        }, recordedAt);
        return subscription;
    }

    // ---- the punch ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Right_Schema_Opens_Todays_Line_On_The_Bornes_Cabinet()
    {
        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        var done = result.Value!.Done!;
        Assert.Null(result.Value.Refused);
        Assert.Equal(AttendanceRules.ArrivalAction, done.Action);
        Assert.Matches(@"^\d{2}:\d{2}$", done.At);
        Assert.Equal("Ines Gharbi", done.Name);
        Assert.Equal("Accueil", done.TerminalName);
        _entries.Verify(r => r.AddAsync(It.Is<AttendanceEntry>(e =>
            e.ClinicId == ClinicId && e.UserId == _ines.Id && e.LocalDay == Today && e.IsOpen
            && e.TerminalId == _borne.Id && e.TerminalName == "Accueil" && e.PunchedArrivalUtc == e.ArrivalUtc),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(_borne.LastUsedAt);
        Assert.Equal(TenantScopeKind.Clinic, _scope.Kind);
        Assert.Equal(ClinicId, _scope.ClinicId);
        _audit.Verify(a => a.AuthenticatedAs(_ines.Id, _ines.Email), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Second_Tap_The_Same_Day_Closes_The_Open_Line_At_The_Borne()
    {
        var line = OpenLine(_ines, Today);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AttendanceRules.DepartureAction, result.Value!.Done!.Action);
        Assert.False(line.IsOpen);
        Assert.Equal(AttendanceDepartureSource.Borne, line.DepartureSource);
        Assert.Equal(line.DepartureUtc, line.PunchedDepartureUtc);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Line_Left_Open_Yesterday_Asks_For_Its_Departure_Before_The_Arrival_Lands()
    {
        var yesterday = OpenLineAt(_ines, Today.AddDays(-1), "08:02");

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        var refused = result.Value!.Refused!;
        Assert.Null(result.Value.Done);
        Assert.Equal(AttendanceLockRules.PreviousDepartureMissingCode, refused.Code);
        Assert.Equal(AttendanceLockRules.PreviousDepartureMissing(Today.AddDays(-1), Today), refused.Error);
        Assert.Contains("d'hier", refused.Error);
        Assert.Equal(new PreviousDayDto(AttendanceLockRules.DayKey(Today.AddDays(-1)), "08:02", null), refused.PreviousDay);
        Assert.True(yesterday.IsOpen);
        VerifyNoLineWritten();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Declaration_Step_Names_Her_Last_Save_Of_That_Day_From_The_Journal()
    {
        var day = Today.AddDays(-1);
        OpenLineAt(_ines, day, "08:02");
        var lastSave = AttendanceRules.ParseLocalTime(day, "17:46")!.Value;
        LastSaveOn(lastSave);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal("17:46", result.Value!.Refused!.PreviousDay!.LastSaveAt);
        var (from, to) = ClinicManagement.Application.Common.ClinicClock.LocalDayRangeUtc(day.ToDateTime(TimeOnly.MinValue));
        _journal.Verify(j => j.GetLastSaveAtAsync(ClinicId, _ines.Id, from, to,
            It.Is<IReadOnlyCollection<string>>(t => t.SequenceEqual(AttendanceLockRules.NotWorkInTheJournal)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Wrong_Schema_Is_Counted_Before_Anything_Of_The_Previous_Day_Is_Shown()
    {
        OpenLineAt(_ines, Today.AddDays(-1), "08:02");

        var result = await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        var refused = result.Value!.Refused!;
        Assert.Equal(AttendanceRules.SchemaWrongCode, refused.Code);
        Assert.Null(refused.PreviousDay);
        Assert.Equal(1, _ines.SchemaFailedAttempts);
        _journal.Verify(j => j.GetLastSaveAtAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Declared_Hour_Closes_Yesterday_As_Declared_And_The_Arrival_Then_Opens_Today()
    {
        var day = Today.AddDays(-1);
        var yesterday = OpenLineAt(_ines, day, "08:02");
        var lastSave = AttendanceRules.ParseLocalTime(day, "17:46")!.Value;
        LastSaveOn(lastSave);

        var tap = Tap(_ines);
        tap.DeclaredPreviousDeparture = "17:30";
        var result = await Punch().Handle(tap, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AttendanceRules.ArrivalAction, result.Value!.Done!.Action);
        Assert.Equal(AttendanceRules.ParseLocalTime(day, "17:30"), yesterday.DepartureUtc);
        Assert.Equal(AttendanceDepartureSource.DeclaredBySelf, yesterday.DepartureSource);
        Assert.True(yesterday.IsDeclared);
        Assert.Equal(lastSave, yesterday.DeclaredLastSaveUtc);
        Assert.Null(yesterday.PunchedDepartureUtc);
        _entries.Verify(r => r.AddAsync(It.Is<AttendanceEntry>(e => e.LocalDay == Today && e.IsOpen), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.AuthenticatedAs(_ines.Id, _ines.Email), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("07:30")]
    [InlineData("08:02")]
    public Task A_Declared_Hour_Not_After_The_Arrival_Is_Refused_By_Name_And_Nothing_Is_Written(string declared) =>
        AssertDeclarationRefused(declared);

    [Theory]
    [InlineData("8h30")]
    [InlineData("24:10")]
    public Task An_Unreadable_Declared_Hour_Is_Refused_The_Same_Way_And_Nothing_Is_Written(string declared) =>
        AssertDeclarationRefused(declared);

    private async Task AssertDeclarationRefused(string declared)
    {
        var yesterday = OpenLineAt(_ines, Today.AddDays(-1), "08:02");

        var tap = Tap(_ines);
        tap.DeclaredPreviousDeparture = declared;
        var result = await Punch().Handle(tap, default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceLockRules.DeclaredDepartureInvalidCode, result.Code);
        Assert.Equal(AttendanceLockRules.DeclaredDepartureInvalid("08:02"), result.Error);
        Assert.True(yesterday.IsOpen);
        VerifyNoLineWritten();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Right_Schema_Resets_Her_Counter_Even_When_The_Arrival_Waits_For_A_Declaration()
    {
        OpenLineAt(_ines, Today.AddDays(-1), "08:02");
        for (var i = 0; i < 3; i++) _ines.RegisterSchemaFailure(DateTime.UtcNow);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceLockRules.PreviousDepartureMissingCode, result.Value!.Refused!.Code);
        Assert.Equal(0, _ines.SchemaFailedAttempts);
        _users.Verify(r => r.Update(_ines), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Two_Forgotten_Days_Are_Declared_Oldest_First_Before_The_Arrival_Lands()
    {
        var older = OpenLineAt(_ines, Today.AddDays(-3), "08:00");
        var newer = OpenLineAt(_ines, Today.AddDays(-1), "08:02");

        var asked = await Punch().Handle(Tap(_ines), default);
        Assert.Equal(AttendanceLockRules.DayKey(Today.AddDays(-3)), asked.Value!.Refused!.PreviousDay!.Day);
        Assert.StartsWith("Votre départ du ", asked.Value.Refused.Error);

        var first = Tap(_ines);
        first.DeclaredPreviousDeparture = "17:00";
        var next = await Punch().Handle(first, default);

        Assert.True(older.IsDeclared);
        Assert.True(newer.IsOpen);
        Assert.Equal(AttendanceLockRules.DayKey(Today.AddDays(-1)), next.Value!.Refused!.PreviousDay!.Day);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);

        var second = Tap(_ines);
        second.DeclaredPreviousDeparture = "17:30";
        var arrived = await Punch().Handle(second, default);

        Assert.Equal(AttendanceRules.ArrivalAction, arrived.Value!.Done!.Action);
        Assert.True(newer.IsDeclared);
        _entries.Verify(r => r.AddAsync(It.Is<AttendanceEntry>(e => e.LocalDay == Today), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Line_Open_Today_Still_Departs_Without_Any_Declaration()
    {
        var today = OpenLine(_ines, Today);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceRules.DepartureAction, result.Value!.Done!.Action);
        Assert.Equal(AttendanceDepartureSource.Borne, today.DepartureSource);
        _journal.Verify(j => j.GetLastSaveAtAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime>(),
            It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Lunch_Break_Gives_A_Second_Pair_The_Same_Day()
    {
        var morning = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, Today, _borne.Id, "Accueil", DateTime.UtcNow.AddHours(-5));
        morning.Depart(DateTime.UtcNow.AddHours(-3), Today);
        _entries.Setup(r => r.FindLatestOpenAsync(ClinicId, _ines.Id, It.IsAny<CancellationToken>())).ReturnsAsync((AttendanceEntry?)null);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceRules.ArrivalAction, result.Value!.Done!.Action);
        _entries.Verify(r => r.AddAsync(It.Is<AttendanceEntry>(e => e.LocalDay == Today), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Whole_Day_Congé_Refuses_The_Arrival_And_A_Half_Day_Does_Not()
    {
        var full = AcceptedLeave(_ines, Today, Today);

        var refused = await Punch().Handle(Tap(_ines), default);

        Assert.True(refused.IsFailure);
        Assert.Equal(AttendanceRules.OnLeaveCode, refused.Code);
        VerifyNoLineWritten();

        _acceptedLeaves.Remove(full);
        AcceptedLeave(_ines, Today, Today, halfDay: true);
        var allowed = await Punch().Handle(Tap(_ines), default);

        Assert.True(allowed.IsSuccess, allowed.Error);
        Assert.Equal(AttendanceRules.ArrivalAction, allowed.Value!.Done!.Action);
    }

    [Fact]
    public async Task A_Congé_Does_Not_Stop_Her_Leaving_When_She_Came_In_Anyway()
    {
        OpenLine(_ines, Today);
        AcceptedLeave(_ines, Today, Today);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(AttendanceRules.DepartureAction, result.Value!.Done!.Action);
    }

    [Fact]
    public async Task The_Punch_Reaches_Open_Screens_Under_The_Team_Key()
    {
        await Punch().Handle(Tap(_ines), default);

        _realtime.Verify(r => r.NotifyEntityChangedAsync(
            ClinicId, RealtimeResourceResolver.Resolve(typeof(PunchCommand))!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Broadcast_That_Fails_Does_Not_Undo_The_Punch()
    {
        _realtime.Setup(r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- which device, which cabinet ------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-borne")]
    public async Task A_Device_That_Is_Not_A_Borne_Is_Refused_By_Name(string? secret)
    {
        var result = await Punch().Handle(Tap(_ines, secret: secret), default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceRules.TerminalUnknownCode, result.Code);
        Assert.Equal(AttendanceRules.TerminalUnknown, result.Error);
        _users.Verify(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Request_Already_Scoped_To_Another_Cabinet_Cannot_Use_This_Borne()
    {
        _scope.UseClinic(OtherClinicId);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceRules.TerminalUnknownCode, result.Code);
        Assert.Equal(OtherClinicId, _scope.ClinicId);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task A_Borne_Cannot_Clock_In_Someone_From_Another_Cabinet()
    {
        var stranger = Account(User.RoleSecretary, "Amira Autre", withSchema: true, clinicId: OtherClinicId);

        var result = await Punch().Handle(Tap(stranger), default);

        Assert.Equal(AttendanceRules.PersonNotTrackedCode, result.Code);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task With_The_Pointage_Off_The_Borne_Refuses_And_Deletes_Nothing()
    {
        _clinic.SetAttendanceEnabled(false);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceRules.ModuleOffCode, result.Code);
        VerifyNoLineWritten();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Lapsed_Cloud_Subscription_Refuses_With_The_Usual_Code()
    {
        _policy.SetupGet(p => p.RequiresSubscription).Returns(true);
        _subscriptions.Setup(s => s.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(EndingOn(DateTime.UtcNow.Date.AddDays(-40)));

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsFailure);
        Assert.Equal(SubscriptionRefusals.RequiredCode, result.Code);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task A_Cabinet_With_No_Subscription_Row_Is_Refused_When_One_Is_Required()
    {
        _policy.SetupGet(p => p.RequiresSubscription).Returns(true);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(SubscriptionRefusals.MissingCode, result.Code);
    }

    [Fact]
    public async Task Where_No_Subscription_Is_Required_The_Ledger_Is_Not_Even_Read()
    {
        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        _subscriptions.Verify(s => s.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- who may clock in ----------------------------------------------------------------------------------

    [Fact]
    public async Task An_Untracked_Or_Inactive_Account_Never_Clocks_In()
    {
        var doctor = Account(User.RoleDoctor, "Dr Karim Hamdi", withSchema: true);
        var gone = Account(User.RoleSecretary, "Sana Partie", withSchema: true);
        gone.Deactivate();

        Assert.Equal(AttendanceRules.PersonNotTrackedCode, (await Punch().Handle(Tap(doctor), default)).Code);
        Assert.Equal(AttendanceRules.PersonNotTrackedCode, (await Punch().Handle(Tap(gone), default)).Code);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task An_Admin_Ticked_By_Hand_Clocks_In_Like_A_Secretary()
    {
        _admin.SetAttendanceSchema(StoredHash);
        _admin.SetAttendanceTracked(true);

        var result = await Punch().Handle(Tap(_admin), default);

        Assert.True(result.IsSuccess, result.Error);
    }

    [Fact]
    public async Task A_Tracked_Person_With_No_Schema_Is_Sent_To_Mon_Profil()
    {
        var newcomer = Account(User.RoleSecretary, "Nour Nouvelle");

        var result = await Punch().Handle(Tap(newcomer), default);

        Assert.Equal(AttendanceRules.SchemaMissingCode, result.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNoLineWritten();
    }

    // ---- a wrong schéma ------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Wrong_Schema_Is_Counted_And_Named_With_The_Tries_Left()
    {
        var result = await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        Assert.True(result.IsSuccess, result.Error);
        var refused = result.Value!.Refused!;
        Assert.Null(result.Value.Done);
        Assert.Equal(AttendanceRules.SchemaWrongCode, refused.Code);
        Assert.Equal(4, refused.AttemptsLeft);
        Assert.Equal(AttendanceRules.SchemaWrong(4), refused.Error);
        Assert.Equal(1, _ines.SchemaFailedAttempts);
        _audit.Verify(a => a.RunAs("borne Accueil"), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task A_Stray_Tap_Is_Refused_By_Name_And_Costs_No_Try()
    {
        var result = await Punch().Handle(Tap(_ines, new List<int> { 4 }), default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceRules.SchemaInvalidCode, result.Code);
        Assert.Equal("Reliez au moins 4 points.", result.Error);
        Assert.Equal(0, _ines.SchemaFailedAttempts);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Account_Row_Is_Locked_Inside_One_Transaction_So_Parallel_Guesses_Each_Count()
    {
        var order = new List<string>();
        _uow.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("begin")).Returns(Task.CompletedTask);
        _users.Setup(r => r.LockForUpdateAsync(_ines.Id, It.IsAny<CancellationToken>())).Callback(() => order.Add("lock")).Returns(Task.CompletedTask);
        _hasher.Setup(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Callback(() => order.Add("verify")).Returns(PasswordVerificationOutcome.Failed);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("save")).ReturnsAsync(1);
        _uow.Setup(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("commit")).Returns(Task.CompletedTask);

        await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        Assert.Equal(new[] { "begin", "lock", "verify", "save", "commit" }, order);
    }

    [Fact]
    public async Task A_Failure_Inside_The_Lock_Rolls_The_Transaction_Back()
    {
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task On_A_Congé_Day_The_Refusal_Comes_Before_The_Schema_Is_Checked()
    {
        AcceptedLeave(_ines, Today, Today);

        var result = await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        Assert.Equal(AttendanceRules.OnLeaveCode, result.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        Assert.Equal(0, _ines.SchemaFailedAttempts);
    }

    [Theory]
    [InlineData(false, "departure")]
    [InlineData(true, "arrival")]
    public async Task A_Panel_Announcing_The_Other_Action_Is_Refused_Before_Anything_Is_Written(bool openToday, string expected)
    {
        if (openToday) OpenLine(_ines, Today);

        var result = await Punch().Handle(
            new PunchCommand { Secret = Secret, UserId = _ines.Id, Pattern = RightPattern, Expected = expected }, default);

        Assert.Equal(AttendanceRules.PunchStateChangedCode, result.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task A_Panel_Announcing_The_Right_Action_Punches()
    {
        var result = await Punch().Handle(
            new PunchCommand { Secret = Secret, UserId = _ines.Id, Pattern = RightPattern, Expected = AttendanceRules.ArrivalAction }, default);

        Assert.Equal(AttendanceRules.ArrivalAction, result.Value!.Done!.Action);
    }

    [Fact]
    public async Task The_Borne_Records_Its_Last_Use_Once_A_Day()
    {
        await Punch().Handle(Tap(_ines), default);
        var first = _borne.LastUsedAt;
        var leila = Account(User.RoleSecretary, "Leila Trabelsi", withSchema: true);

        await Punch().Handle(Tap(leila), default);

        Assert.NotNull(first);
        Assert.Equal(first, _borne.LastUsedAt);
    }

    [Fact]
    public async Task The_Fifth_Wrong_Schema_Locks_The_Tile_For_Fifteen_Minutes()
    {
        for (var i = 0; i < User.MaxSchemaFailures - 1; i++)
            await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        var before = DateTime.UtcNow;
        var fifth = await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        var refused = fifth.Value!.Refused!;
        Assert.Equal(AttendanceRules.SchemaLockedCode, refused.Code);
        Assert.NotNull(refused.LockedUntil);
        Assert.InRange(refused.LockedUntil!.Value, before + User.SchemaLockDuration, DateTime.UtcNow + User.SchemaLockDuration);
        Assert.Contains("Réessayez dans 15 min", refused.Error);
        Assert.True(_ines.IsSchemaLocked(DateTime.UtcNow));
    }

    [Fact]
    public async Task A_Wrong_Schema_Reaches_Open_Screens_Too()
    {
        await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        _realtime.Verify(r => r.NotifyEntityChangedAsync(
            ClinicId, RealtimeResourceResolver.Resolve(typeof(PunchCommand))!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Only_The_Fifth_Wrong_Schema_Tells_The_Admins_Until_When_She_Is_Blocked()
    {
        for (var i = 0; i < User.MaxSchemaFailures - 1; i++)
            await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);
        _bell.Verify(b => b.AttendanceNoticeAsync(It.IsAny<Guid>(), NotificationCategory.AttendanceSchemaBlocked, It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        var until = AttendanceRules.LocalTime(_ines.SchemaLockedUntilUtc!.Value);
        _bell.Verify(b => b.AttendanceNoticeAsync(ClinicId, NotificationCategory.AttendanceSchemaBlocked, "Schéma bloqué",
            $"Le schéma de Ines Gharbi est bloqué jusqu'à {until} après 5 essais.", null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Tap_On_An_Already_Locked_Tile_Tells_Nobody_Again()
    {
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            _ines.RegisterSchemaFailure(DateTime.UtcNow);

        await Punch().Handle(Tap(_ines, new List<int> { 8, 7, 6, 3 }), default);

        _bell.Verify(b => b.AttendanceNoticeAsync(It.IsAny<Guid>(), It.IsAny<NotificationCategory>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Locked_Tile_Refuses_Even_The_Right_Schema_Without_Checking_It()
    {
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            _ines.RegisterSchemaFailure(DateTime.UtcNow);

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.Equal(AttendanceRules.SchemaLockedCode, result.Value!.Refused!.Code);
        _hasher.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoLineWritten();
    }

    [Fact]
    public async Task The_Right_Schema_Clears_Earlier_Wrong_Tries()
    {
        _ines.RegisterSchemaFailure(DateTime.UtcNow);
        _ines.RegisterSchemaFailure(DateTime.UtcNow);

        await Punch().Handle(Tap(_ines), default);

        Assert.Equal(0, _ines.SchemaFailedAttempts);
    }

    [Fact]
    public async Task A_Schema_Stored_In_An_Old_Hash_Format_Is_Rehashed_On_Success()
    {
        _hasher.Setup(h => h.VerifyPassword(StoredHash, "0-1-2-5")).Returns(PasswordVerificationOutcome.SuccessNeedsRehash);
        _hasher.Setup(h => h.HashPassword("0-1-2-5")).Returns("NEWHASH");

        var result = await Punch().Handle(Tap(_ines), default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal("NEWHASH", _ines.AttendanceSchemaHash);
    }

    // ---- « Ajouter une borne » : cet appareil ---------------------------------------------------------------------

    [Fact]
    public async Task An_Admin_Registers_A_Borne_And_Only_The_Secrets_Hash_Is_Stored()
    {
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        AttendanceTerminal? added = null;
        _terminals.Setup(r => r.AddAsync(It.IsAny<AttendanceTerminal>(), It.IsAny<CancellationToken>()))
            .Callback<AttendanceTerminal, CancellationToken>((t, _) => added = t);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "  Accueil 2  " }, default);

        Assert.True(result.IsSuccess, result.Error);
        var secret = result.Value!.Secret;
        Assert.Matches("^[A-Za-z0-9_-]{43}$", secret);
        Assert.NotNull(added);
        Assert.Equal(SessionCredential.Hash(secret), added!.SecretHash);
        Assert.DoesNotContain(secret, added.SecretHash);
        Assert.Equal("Accueil 2", added.Name);
        Assert.Equal(ClinicId, added.ClinicId);
        Assert.Equal(_admin.Id, added.CreatedByUserId);
        Assert.Equal("Accueil 2", result.Value.Terminal.Name);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Registering_Again_On_The_Same_Device_Retires_Its_Previous_Borne()
    {
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil", ReplacingTerminalId = _borne.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(_borne.IsActive);
    }

    [Fact]
    public async Task Another_Cabinets_Borne_Is_Never_Retired_By_A_Registration_Here()
    {
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        var foreign = Borne(OtherClinicId, "Ailleurs", "foreign-secret");

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Salle 2", ReplacingTerminalId = foreign.Id }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(foreign.IsActive);
    }

    [Fact]
    public async Task A_Secretary_Cannot_Make_A_Device_A_Borne()
    {
        _context.Setup(c => c.GetUserId()).Returns(_ines.Id);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil" }, default);

        Assert.True(result.IsFailure);
        Assert.Equal(AttendanceRules.AdminOnly, result.Error);
        _terminals.Verify(r => r.AddAsync(It.IsAny<AttendanceTerminal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_Borne_Needs_A_Name(string? name)
    {
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = name }, default);

        Assert.True(result.IsFailure);
        _terminals.Verify(r => r.AddAsync(It.IsAny<AttendanceTerminal>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task No_Borne_Is_Registered_While_The_Pointage_Is_Off()
    {
        _clinic.SetAttendanceEnabled(false);
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);

        var result = await Register().Handle(new RegisterAttendanceTerminalCommand { Name = "Accueil" }, default);

        Assert.Equal(AttendanceRules.ModuleOffCode, result.Code);
    }

    // ---- what a device learns about itself -----------------------------------------------------------------

    [Fact]
    public async Task A_Device_That_Is_Not_A_Borne_Learns_Nothing_About_The_Team()
    {
        var result = await Status().Handle(new GetPointageStatusQuery { Secret = null }, default);

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.IsBorne);
        Assert.Empty(result.Value.People);
        _users.Verify(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Borne_Whose_Pointage_Is_Off_Shows_Its_Name_And_No_Tiles()
    {
        _clinic.SetAttendanceEnabled(false);

        var result = await Status().Handle(new GetPointageStatusQuery { Secret = Secret }, default);

        Assert.True(result.Value!.IsBorne);
        Assert.False(result.Value.Enabled);
        Assert.Equal("Accueil", result.Value.Name);
        Assert.Empty(result.Value.People);
    }

    [Fact]
    public async Task The_Tiles_Are_The_Active_Tracked_People_With_Todays_State()
    {
        var doctor = Account(User.RoleDoctor, "Dr Karim Hamdi", withSchema: true);
        var gone = Account(User.RoleSecretary, "Sana Partie", withSchema: true);
        gone.Deactivate();
        var amira = Account(User.RoleSecretary, "Amira Ben Salah");
        var leila = Account(User.RoleSecretary, "Leila Trabelsi", withSchema: true);
        var nour = Account(User.RoleSecretary, "Nour Zouari", withSchema: true);
        OpenLine(_ines, Today);
        AcceptedLeave(leila, Today, Today);
        AcceptedLeave(nour, Today, Today, halfDay: true);
        for (var i = 0; i < User.MaxSchemaFailures; i++)
            nour.RegisterSchemaFailure(DateTime.UtcNow);

        var result = await Status().Handle(new GetPointageStatusQuery { Secret = Secret }, default);

        var people = result.Value!.People;
        Assert.Equal(_borne.Id, result.Value.TerminalId);
        Assert.True(result.Value.Enabled);
        Assert.Equal(new[] { "Amira Ben Salah", "Ines Gharbi", "Leila Trabelsi", "Nour Zouari" }, people.Select(p => p.Name));
        Assert.DoesNotContain(people, p => p.UserId == doctor.Id || p.UserId == gone.Id);

        var amiraTile = people.Single(p => p.UserId == amira.Id);
        Assert.Equal("out", amiraTile.State);
        Assert.True(amiraTile.NeedsSchema);
        Assert.Equal("AB", amiraTile.Initials);

        var inesTile = people.Single(p => p.UserId == _ines.Id);
        Assert.Equal("in", inesTile.State);
        Assert.Matches(@"^\d{2}:\d{2}$", inesTile.Since);

        Assert.Equal("leave", people.Single(p => p.UserId == leila.Id).State);
        var nourTile = people.Single(p => p.UserId == nour.Id);
        Assert.Equal("out", nourTile.State);
        Assert.NotNull(nourTile.LockedUntil);
    }

    [Fact]
    public async Task After_Her_Departure_Her_Tile_Reads_Left_And_A_Tap_Is_A_New_Arrival()
    {
        var left = DateTime.UtcNow.AddHours(-3);
        var morning = AttendanceEntry.Arrive(Guid.NewGuid(), ClinicId, _ines.Id, Today, _borne.Id, "Accueil", left.AddHours(-2));
        morning.Depart(left, Today);
        _todayLines.Add(morning);

        var tile = (await Status().Handle(new GetPointageStatusQuery { Secret = Secret }, default)).Value!.People.Single(p => p.UserId == _ines.Id);
        var punch = await Punch().Handle(new PunchCommand
        {
            Secret = Secret, UserId = _ines.Id, Pattern = RightPattern, Expected = AttendanceRules.ArrivalAction,
        }, default);

        Assert.Equal(AttendanceRules.StateLeft, tile.State);
        Assert.Equal(AttendanceRules.LocalTime(left), tile.LeftAt);
        Assert.Null(tile.Since);
        Assert.Equal(AttendanceRules.ArrivalAction, punch.Value!.Done!.Action);
    }
}
