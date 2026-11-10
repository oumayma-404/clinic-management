using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Team;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// `pointage-obligatoire` AC-6: « Départ non pointé » 15 minutes after today's closing, once per line, for her alone.
/// « Now » is a parameter, so the threshold and the cabinet's day are pinned to fixed instants. The job reads lines and
/// never writes one — an `xmin` bump would 409 an admin's open correction.
/// </summary>
public class AttendanceDepartureReminderJobTests
{
    private static readonly DateOnly Friday = new(2026, 11, 6);
    private static readonly Clinic Cabinet = Open(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "08:00", "17:00");

    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IAttendanceEntryRepository> _entries = new();
    private readonly Mock<IAttendanceTerminalRepository> _terminals = new();
    private readonly Mock<IStaffNotificationRepository> _notifications = new();
    private readonly Mock<INotificationGenerator> _generator = new();
    private readonly Mock<IAuditActorProvider> _audit = new();
    private readonly TenantScope _scope = new(NullLogger<TenantScope>.Instance);
    private readonly List<Clinic> _cabinets = new() { Cabinet };

    public AttendanceDepartureReminderJobTests()
    {
        // The repository's own predicate: only cabinets with the pointage on reach the pass.
        _clinics.Setup(r => r.ListAttendanceHoursAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _cabinets
            .Where(c => c.AttendanceEnabled).Select(c => new ClinicAttendanceHours(c.Id, c.WorkingHoursJson)).ToList());
        _entries.Setup(r => r.ListOpenOnAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AttendanceEntry>());
        _terminals.Setup(r => r.ListAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Guid clinicId, CancellationToken _) =>
            (IReadOnlyList<AttendanceTerminal>)new List<AttendanceTerminal> { AttendanceTerminal.Register(Guid.NewGuid(), clinicId, "Accueil", "hash", "admin", new DateTime(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc)) });
    }

    private static Clinic Open(Guid id, string from, string to, bool fridayOpen = true, bool attendance = true)
    {
        var clinic = new Clinic(id, "Cabinet", code: "CODE01");
        clinic.SetAttendanceEnabled(attendance);
        clinic.SetWorkingHours("[" + string.Join(",", new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" }
            .Select(d => $"{{\"Day\":\"{d}\",\"Enabled\":{(d != "Friday" || fridayOpen ? "true" : "false")},\"From\":\"{from}\",\"To\":\"{to}\"}}")) + "]");
        return clinic;
    }

    private AttendanceEntry Present(Clinic clinic, string userId, string arrival)
    {
        var line = AttendanceEntry.Arrive(Guid.NewGuid(), clinic.Id, userId, Friday, Guid.NewGuid(), "Accueil",
            AttendanceRules.ParseLocalTime(Friday, arrival)!.Value);
        _entries.Setup(r => r.ListOpenOnAsync(clinic.Id, Friday, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { line });
        return line;
    }

    private Task RunAt(string local) =>
        new AttendanceDepartureReminderJob(_clinics.Object, _entries.Object, _terminals.Object, _notifications.Object,
                _generator.Object, _audit.Object, _scope, NullLogger<AttendanceDepartureReminderJob>.Instance)
            .RemindMissingDepartures(AttendanceRules.ParseLocalTime(Friday, local)!.Value);

    private void VerifyNobodyReminded() =>
        _generator.Verify(g => g.AttendanceDepartureMissingAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Never);

    [Fact]
    public async Task Fifteen_Minutes_After_Closing_She_Is_Reminded_Of_Her_Arrival_Hour()
    {
        Present(Cabinet, "local|ines", "08:02");

        await RunAt("17:15");

        _generator.Verify(g => g.AttendanceDepartureMissingAsync(Cabinet.Id, "local|ines", "08:02", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        _audit.Verify(a => a.RunAs(nameof(AttendanceDepartureReminderJob)), Times.Once);
        Assert.Equal(TenantScopeKind.SystemWide, _scope.Kind);
    }

    [Fact]
    public async Task Before_Closing_Plus_Fifteen_Nobody_Is_Reminded()
    {
        Present(Cabinet, "local|ines", "08:02");

        await RunAt("17:14");

        VerifyNobodyReminded();
    }

    [Fact]
    public async Task A_Line_Already_Reminded_Today_Is_Not_Reminded_Twice()
    {
        Present(Cabinet, "local|ines", "08:02");
        var (dayStart, _) = ClinicManagement.Application.Common.ClinicClock.LocalDayRangeUtc(Friday.ToDateTime(TimeOnly.MinValue));
        _notifications.Setup(n => n.ExistsForUserSinceAsync(Cabinet.Id, NotificationCategory.AttendanceDepartureMissing,
            "local|ines", dayStart, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await RunAt("18:00");

        VerifyNobodyReminded();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task A_Closed_Day_Or_A_Cabinet_Without_The_Pointage_Reminds_Nobody(bool fridayOpen, bool attendance)
    {
        var clinic = Open(Guid.NewGuid(), "08:00", "17:00", fridayOpen, attendance);
        _cabinets.Clear();
        _cabinets.Add(clinic);
        Present(clinic, "local|ines", "08:02");

        await RunAt("19:00");

        VerifyNobodyReminded();
    }

    [Fact]
    public async Task With_No_Opening_Hours_Closing_Cannot_Be_Known_So_Nobody_Is_Reminded()
    {
        var clinic = new Clinic(Guid.NewGuid(), "Sans horaires", code: "CODE02");
        clinic.SetAttendanceEnabled(true);
        _cabinets.Clear();
        _cabinets.Add(clinic);
        Present(clinic, "local|ines", "08:02");

        await RunAt("23:00");

        VerifyNobodyReminded();
        _entries.Verify(r => r.ListOpenOnAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task One_Cabinets_Failure_Does_Not_Cost_Another_Its_Reminder()
    {
        var broken = Open(Guid.NewGuid(), "08:00", "17:00");
        _cabinets.Insert(0, broken);
        _entries.Setup(r => r.ListOpenOnAsync(broken.Id, Friday, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));
        Present(Cabinet, "local|ines", "08:02");

        await RunAt("17:30");

        _generator.Verify(g => g.AttendanceDepartureMissingAsync(Cabinet.Id, "local|ines", "08:02", It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_Reminder_Names_The_Live_Bornes_Only()
    {
        Present(Cabinet, "local|ines", "08:02");
        var retired = AttendanceTerminal.Register(Guid.NewGuid(), Cabinet.Id, "Ancienne", "h1", "admin", new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc));
        retired.Revoke(new DateTime(2026, 9, 2, 7, 0, 0, DateTimeKind.Utc));
        var waiting = AttendanceTerminal.AssignToDevice(Guid.NewGuid(), Cabinet.Id, "Salle 2", Guid.NewGuid(), null, "admin", DateTime.UtcNow);
        var live = AttendanceTerminal.Register(Guid.NewGuid(), Cabinet.Id, "Accueil", "h2", "admin", new DateTime(2026, 10, 1, 7, 0, 0, DateTimeKind.Utc));
        _terminals.Setup(r => r.ListAsync(Cabinet.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AttendanceTerminal> { retired, waiting, live });

        await RunAt("17:30");

        _generator.Verify(g => g.AttendanceDepartureMissingAsync(Cabinet.Id, "local|ines", "08:02",
            It.Is<IReadOnlyList<string>>(b => b.SequenceEqual(new[] { "Accueil" })), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Cabinet_With_No_Live_Borne_Is_Not_Asked_For_A_Departure_Nobody_Could_Pointer()
    {
        Present(Cabinet, "local|ines", "08:02");
        _terminals.Setup(r => r.ListAsync(Cabinet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<AttendanceTerminal>());

        await RunAt("17:30");

        VerifyNobodyReminded();
    }

    [Fact]
    public async Task The_Reminder_Writes_No_Pointage_Row()
    {
        var line = Present(Cabinet, "local|ines", "08:02");

        await RunAt("17:30");

        Assert.True(line.IsOpen);
        Assert.Null(line.UpdatedAt);
        _entries.Verify(r => r.AddAsync(It.IsAny<AttendanceEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
