using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Team;

/// <summary>
/// `pointage-obligatoire`: the facts behind the lock, cheapest first. Each fact can only lift it, so the first one that
/// does ends the read — an admin's or a dentist's save costs no query at all, which is what lets the 423 gate sit in
/// front of every write.
/// </summary>
public class AttendanceLockEvaluatorTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly Friday = new(2026, 11, 6);
    private static readonly DateTime FridayTenUtc = AttendanceRules.ParseLocalTime(Friday, "10:00")!.Value;
    private const string OpenAllWeek =
        "[{\"Day\":\"Monday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Tuesday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},"
        + "{\"Day\":\"Wednesday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Thursday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},"
        + "{\"Day\":\"Friday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"17:00\"},{\"Day\":\"Saturday\",\"Enabled\":true,\"From\":\"08:00\",\"To\":\"13:00\"},"
        + "{\"Day\":\"Sunday\",\"Enabled\":false,\"From\":\"08:00\",\"To\":\"17:00\"}]";

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IStaffLeaveRequestRepository> _leaves = new();
    private readonly Mock<IRelayLocalStatus> _relayLocal = new();
    private readonly Mock<IClinicRelayRepository> _relays = new();
    private readonly Mock<ISubscriptionPolicy> _policy = new();
    private readonly Mock<IClinicSubscriptionRepository> _subscriptions = new();
    private readonly User _ines = User.CreateLocalUser(ClinicId, User.RoleSecretary, "ines@cabinet.tn", "HASH", "Ines Gharbi");

    public AttendanceLockEvaluatorTests()
    {
        Switches(attendance: true, locking: true, OpenAllWeek);
        _terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new LiveBornes(true, null));
    }

    private void Switches(bool attendance, bool locking, string? hours) =>
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAttendanceSwitches(attendance, locking, hours));

    private AttendanceLockEvaluator Evaluator() => new(
        _clinics.Object, _entries.Object, _terminals.Object, _leaves.Object, _relayLocal.Object, _relays.Object, _policy.Object,
        _subscriptions.Object);

    private Task<bool> LockedAt(User user, DateTime? nowUtc = null) =>
        Evaluator().IsLockedAsync(user, nowUtc ?? FridayTenUtc, default);

    private void VerifyNothingRead()
    {
        _clinics.Verify(r => r.GetAttendanceSwitchesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _entries.Verify(r => r.HasLineOnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Tracked_Secretary_With_No_Line_During_Opening_Hours_Is_Locked()
    {
        Assert.True(await LockedAt(_ines));
        _entries.Verify(r => r.HasLineOnAsync(ClinicId, _ines.Id, Friday, It.IsAny<CancellationToken>()), Times.Once);
        _leaves.Verify(r => r.HasApprovedCoveringAsync(ClinicId, _ines.Id, Friday, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Admin_Is_Never_Locked_Even_Ticked_To_Pointer_And_Costs_No_Query()
    {
        var admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "salma@cabinet.tn", "HASH", "Dr Salma");
        admin.SetAttendanceTracked(true);

        Assert.False(await LockedAt(admin));
        VerifyNothingRead();
    }

    [Fact]
    public async Task An_Untracked_Or_Inactive_Account_Is_Never_Locked_And_Costs_No_Query()
    {
        var doctor = User.CreateLocalUser(ClinicId, User.RoleDoctor, "nadia@cabinet.tn", "HASH", "Dr Nadia");
        var gone = User.CreateLocalUser(ClinicId, User.RoleSecretary, "old@cabinet.tn", "HASH", "Ancienne");
        gone.Deactivate();

        Assert.False(await LockedAt(doctor));
        Assert.False(await LockedAt(gone));
        VerifyNothingRead();
    }

    [Fact]
    public async Task On_The_PC_De_Secours_Nobody_Is_Locked_And_Nothing_Is_Read()
    {
        _relayLocal.SetupGet(r => r.IsPcDeSecours).Returns(true);

        Assert.False(await LockedAt(_ines));
        VerifyNothingRead();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task The_Pointage_Or_The_Lock_Switched_Off_Lifts_It_Before_Any_Line_Is_Read(bool attendance, bool locking)
    {
        Switches(attendance, locking, OpenAllWeek);

        Assert.False(await LockedAt(_ines));
        _entries.Verify(r => r.HasLineOnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unknown_Cabinet_Is_Not_Locked()
    {
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicAttendanceSwitches?)null);

        Assert.False(await LockedAt(_ines));
    }

    [Theory]
    [InlineData(null, "10:00")]
    [InlineData(OpenAllWeek, "07:30")]
    [InlineData(OpenAllWeek, "17:30")]
    public async Task With_No_Hours_Or_Outside_Them_She_Is_Not_Locked(string? hours, string local)
    {
        Switches(true, true, hours);

        Assert.False(await LockedAt(_ines, AttendanceRules.ParseLocalTime(Friday, local)));
        _entries.Verify(r => r.HasLineOnAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Closed_Day_Is_Not_Locked()
    {
        var sunday = AttendanceRules.ParseLocalTime(new DateOnly(2026, 11, 8), "10:00")!.Value;

        Assert.False(await LockedAt(_ines, sunday));
    }

    [Fact]
    public async Task Any_Line_Today_Lifts_It_And_Spares_The_Conge_Read()
    {
        _entries.Setup(r => r.HasLineOnAsync(ClinicId, _ines.Id, Friday, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.False(await LockedAt(_ines));
        _leaves.Verify(r => r.HasApprovedCoveringAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Accepted_Conge_Today_Lifts_It()
    {
        _leaves.Setup(r => r.HasApprovedCoveringAsync(ClinicId, _ines.Id, Friday, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.False(await LockedAt(_ines));
    }

    [Fact]
    public async Task A_Cabinet_That_May_Not_Record_Work_Is_Not_Locked_The_Borne_Would_Refuse_The_Punch()
    {
        _policy.SetupGet(p => p.RequiresSubscription).Returns(true);
        _subscriptions.Setup(r => r.GetByClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync((ClinicSubscription?)null);

        Assert.False(await LockedAt(_ines));
        _relays.Verify(r => r.GetCurrentForClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Where_No_Subscription_Is_Required_The_Entitlement_Is_Not_Read()
    {
        Assert.True(await LockedAt(_ines));
        _subscriptions.Verify(r => r.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Cloud_Whose_Cabinet_Works_On_Its_PC_De_Secours_Does_Not_Lock()
    {
        var (relay, _) = ClinicRelay.BeginPairing(ClinicId, "PC-ACCUEIL", "local|admin", FridayTenUtc.AddDays(-1));
        relay.Pair("PC-ACCUEIL", "key", null, null, "build-1", FridayTenUtc.AddDays(-1));
        typeof(ClinicRelay).GetProperty(nameof(ClinicRelay.PcHoldingSinceUtc))!.SetValue(relay, FridayTenUtc.AddMinutes(-30));
        _relays.Setup(r => r.GetCurrentForClinicAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(relay);

        Assert.False(await LockedAt(_ines));
    }

    [Fact]
    public async Task A_Cabinet_With_No_Borne_Left_Is_Not_Locked_Nobody_Could_Lift_It()
    {
        _terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(LiveBornes.None);

        Assert.False(await LockedAt(_ines));
        _subscriptions.Verify(r => r.GetByClinicAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Lock_Switched_On_This_Morning_After_Opening_Locks_Nobody_Until_Tomorrow()
    {
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAttendanceSwitches(true, true, OpenAllWeek, LockArmedAtUtc: FridayTenUtc.AddMinutes(-30)));

        Assert.False(await LockedAt(_ines));
        Assert.True(await LockedAt(_ines, AttendanceRules.ParseLocalTime(Friday.AddDays(1), "08:30")));
    }

    [Fact]
    public async Task A_Borne_Gone_Live_Mid_Day_Locks_Nobody_That_Day()
    {
        _terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveBornes(true, FridayTenUtc.AddHours(-1)));

        Assert.False(await LockedAt(_ines));
    }

    [Fact]
    public async Task Armed_And_Live_Before_Todays_Opening_It_Locks()
    {
        _clinics.Setup(r => r.GetAttendanceSwitchesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ClinicAttendanceSwitches(true, true, OpenAllWeek, LockArmedAtUtc: FridayTenUtc.AddDays(-2)));
        _terminals.Setup(r => r.GetLiveBornesAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LiveBornes(true, FridayTenUtc.AddDays(-1)));

        Assert.True(await LockedAt(_ines));
    }

    [Fact]
    public async Task Reads_Already_Made_By_Clock_Status_Are_Not_Made_Again()
    {
        var locked = await Evaluator().IsLockedAsync(
            _ines, FridayTenUtc, new AttendanceLockReads(new ClinicAttendanceSwitches(true, true, OpenAllWeek), HasLineToday: false), default);

        Assert.True(locked);
        VerifyNothingRead();
    }

    [Fact]
    public async Task A_Line_Today_Known_From_Clock_Status_Lifts_It_Without_Another_Read()
    {
        var locked = await Evaluator().IsLockedAsync(
            _ines, FridayTenUtc, new AttendanceLockReads(new ClinicAttendanceSwitches(true, true, OpenAllWeek), HasLineToday: true), default);

        Assert.False(locked);
        _leaves.Verify(r => r.HasApprovedCoveringAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
