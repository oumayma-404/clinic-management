using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-3][AC-4][AC-7] The « Activité » section: appointments honoured, patients registered, the taux d'absence, and
/// devis accepted — each against the previous window.
/// </summary>
public class DashboardActivityReaderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime FixedNow = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();

    private static readonly DashboardPeriod Period =
        DashboardPeriod.Resolve(DashboardPeriodKey.Month, FixedNow);

    private DashboardActivityReader Reader() =>
        new(_appointments.Object, _patients.Object, _plans.Object);

    /// <summary>Empty defaults so each test states only what it is about.</summary>
    private void WireDefaults()
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>());
        _patients.Setup(r => r.CountCreatedBetweenAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _plans.Setup(r => r.CountByStatusAsync(
                It.IsAny<Guid>(), It.IsAny<TreatmentPlanStatus>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private void WireBreakdown(
        Dictionary<AppointmentStatus, int> current,
        Dictionary<AppointmentStatus, int> previous)
    {
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                ClinicId, Period.From, Period.ToInclusive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);
        _appointments.Setup(r => r.CountByStatusBetweenAsync(
                ClinicId, Period.PreviousFrom, Period.PreviousToInclusive, It.IsAny<CancellationToken>()))
            .ReturnsAsync(previous);
    }

    // [AC-3] Honoured appointments come from the Completed bucket of the single breakdown read.
    [Fact]
    public async Task Reads_Completed_Appointments_From_The_Status_Breakdown()
    {
        WireDefaults();
        WireBreakdown(
            new() { [AppointmentStatus.Completed] = 84, [AppointmentStatus.NoShow] = 4 },
            new() { [AppointmentStatus.Completed] = 71, [AppointmentStatus.NoShow] = 6 });

        var activity = await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        Assert.Equal(84m, activity.CompletedAppointments.Current);
        Assert.Equal(71m, activity.CompletedAppointments.Previous);
    }

    // [AC-3] A status with no rows is absent from the dictionary and must read as zero, not blow up or go null.
    [Fact]
    public async Task A_Status_Absent_From_The_Breakdown_Reads_As_Zero()
    {
        WireDefaults();
        WireBreakdown(
            new() { [AppointmentStatus.Scheduled] = 5 },
            new() { [AppointmentStatus.Scheduled] = 3 });

        var activity = await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        Assert.Equal(0m, activity.CompletedAppointments.Current);
        // Five scheduled, none missed => a real 0 % absence rate, not an undefined one.
        Assert.Equal(0m, activity.AbsenceRate.Current);
    }

    // [AC-4] The rate counts BOTH no-shows and cancellations over the period total — the pair the drill-through
    // link filters on.
    [Fact]
    public async Task Absence_Rate_Counts_NoShow_And_Cancelled_Over_The_Total()
    {
        WireDefaults();
        WireBreakdown(
            new()
            {
                [AppointmentStatus.Completed] = 80,
                [AppointmentStatus.NoShow] = 10,
                [AppointmentStatus.Cancelled] = 10
            },
            new Dictionary<AppointmentStatus, int>());

        var activity = await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        // 20 of 100.
        Assert.Equal(20m, activity.AbsenceRate.Current);
    }

    // [AC-4] The load-bearing case: an empty period has NO absence rate. Reporting 0 % would claim perfect
    // attendance for a clinic that was closed.
    [Fact]
    public async Task Absence_Rate_Is_Null_When_The_Period_Held_No_Appointments()
    {
        WireDefaults();
        WireBreakdown(new Dictionary<AppointmentStatus, int>(), new Dictionary<AppointmentStatus, int>());

        var activity = await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        Assert.Null(activity.AbsenceRate.Current);
        Assert.Null(activity.AbsenceRate.Previous);
        Assert.Null(activity.AbsenceRate.DeltaPercent);
    }

    // [AC-7] « Devis acceptés » must be counted by AcceptedDate, not CreatedAt — otherwise the card counts one set
    // and its drill-through link (which filters on acceptance) opens another.
    [Fact]
    public async Task Accepted_Plans_Are_Counted_By_Their_Acceptance_Date()
    {
        WireDefaults();
        WireBreakdown(new Dictionary<AppointmentStatus, int>(), new Dictionary<AppointmentStatus, int>());

        await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        _plans.Verify(r => r.CountByStatusAsync(
                ClinicId, TreatmentPlanStatus.Accepted, Period.From, Period.ToInclusive,
                true, It.IsAny<CancellationToken>()),
            Times.Once);
        _plans.Verify(r => r.CountByStatusAsync(
                ClinicId, TreatmentPlanStatus.Accepted, Period.PreviousFrom, Period.PreviousToInclusive,
                true, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-3] Both windows are read with their OWN bounds. Without this every activity delta compares a figure with
    // itself — inert while looking present.
    [Fact]
    public async Task Reads_Each_Window_With_Its_Own_Bounds()
    {
        WireDefaults();
        WireBreakdown(new Dictionary<AppointmentStatus, int>(), new Dictionary<AppointmentStatus, int>());
        _patients.Setup(r => r.CountCreatedBetweenAsync(
                ClinicId, Period.From, Period.ToInclusive, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(12);
        _patients.Setup(r => r.CountCreatedBetweenAsync(
                ClinicId, Period.PreviousFrom, Period.PreviousToInclusive, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(15);

        var activity = await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        Assert.Equal(12m, activity.NewPatients.Current);
        Assert.Equal(15m, activity.NewPatients.Previous);
        Assert.Equal(-20.0m, activity.NewPatients.DeltaPercent);
    }

    // [AC-1] Every read is scoped to the caller's clinic — no repository is ever asked about another one.
    [Fact]
    public async Task Every_Read_Is_Scoped_To_The_Callers_Clinic()
    {
        WireDefaults();

        await Reader().ReadAsync(ClinicId, Period, CancellationToken.None);

        _appointments.Verify(r => r.CountByStatusBetweenAsync(
            OtherClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _patients.Verify(r => r.CountCreatedBetweenAsync(
            OtherClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _plans.Verify(r => r.CountByStatusAsync(
            OtherClinicId, It.IsAny<TreatmentPlanStatus>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
