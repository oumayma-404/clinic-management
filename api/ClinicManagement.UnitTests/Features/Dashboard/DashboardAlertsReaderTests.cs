using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

/// <summary>
/// [AC-9] The « À traiter » section — standing state across the salle d'attente, devis awaiting an answer, relances,
/// prostheses overdue at the lab, and stock. Every figure is a count with a matching filtered destination.
/// </summary>
public class DashboardAlertsReaderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime FixedNow = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IWaitingListRepository> _waitingList = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ILabWorkOrderRepository> _labOrders = new();
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    // The three « À clôturer » reads. They go through VisitClosureReader — the same helper the worklist itself
    // calls — so the chip and the page it opens cannot report different numbers.
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _dentalRecords = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();

    private DashboardAlertsReader Reader() => new(
        _waitingList.Object, _plans.Object, _patients.Object, _labOrders.Object, _stock.Object, _clinics.Object,
        _appointments.Object, _dentalRecords.Object, _invoices.Object);

    private static Clinic ClinicFixture() => new(ClinicId, "Cabinet Test", code: "CODE01");

    private void WireDefaults(Clinic? clinic = null)
    {
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(clinic ?? ClinicFixture());
        _waitingList.Setup(r => r.CountWaitingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _plans.Setup(r => r.CountByStatusAsync(
                It.IsAny<Guid>(), It.IsAny<TreatmentPlanStatus>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _patients.Setup(r => r.GetRecallCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecallCandidate>());
        _labOrders.Setup(r => r.CountOverdueAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _stock.Setup(r => r.CountLowStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _stock.Setup(r => r.CountExpiringSoonAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        // No candidate visits ⇒ VisitClosureReader short-circuits before its three link reads, so only this one
        // needs wiring for the existing cases.
        _appointments.Setup(r => r.GetClosureCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
    }

    // [AC-9] Every count lands on its own DTO slot.
    [Fact]
    public async Task Maps_Every_Count_To_Its_Own_Field()
    {
        WireDefaults();
        _waitingList.Setup(r => r.CountWaitingAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _plans.Setup(r => r.CountByStatusAsync(
                ClinicId, TreatmentPlanStatus.Draft, null, null, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _labOrders.Setup(r => r.CountOverdueAsync(ClinicId, FixedNow, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _stock.Setup(r => r.CountLowStockAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _stock.Setup(r => r.CountExpiringSoonAsync(
                ClinicId, Clinic.DefaultStockExpiryLeadDays, FixedNow, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var alerts = await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        Assert.Equal(2, alerts.WaitingList);
        Assert.Equal(5, alerts.DraftPlans);
        Assert.Equal(1, alerts.OverdueLabOrders);
        Assert.Equal(3, alerts.LowStock);
        Assert.Equal(4, alerts.ExpiringStock);
        Assert.True(alerts.ExpiryAlertEnabled);
    }

    // [AC-9] « Devis en attente de réponse » means Draft — a devis presented with no answer yet — and carries no date
    // bound: a quote from three months ago with no reply is exactly the one worth chasing.
    [Fact]
    public async Task Devis_Awaiting_An_Answer_Counts_Drafts_With_No_Date_Bound()
    {
        WireDefaults();

        await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        _plans.Verify(r => r.CountByStatusAsync(
            ClinicId, TreatmentPlanStatus.Draft, null, null, false, It.IsAny<CancellationToken>()), Times.Once);
        _plans.Verify(r => r.CountByStatusAsync(
            ClinicId, TreatmentPlanStatus.Accepted, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-9] A clinic with the approaching-expiry alert switched off reports the alert as DISABLED and is never
    // queried. Reporting 0 would claim nothing is expiring, when the truth is that nothing was looked at.
    [Fact]
    public async Task An_Expiry_Window_Switched_Off_Disables_The_Alert_Rather_Than_Reporting_Zero()
    {
        var clinic = ClinicFixture();
        clinic.SetStockExpiryLeadDays(1);
        // Simulate the legacy/backfilled state the job also guards against: the setter enforces 1–365, so only a row
        // predating the feature can hold 0. Reflection reproduces that state without weakening the domain guard.
        typeof(Clinic).GetProperty(nameof(Clinic.StockExpiryLeadDays))!.SetValue(clinic, 0);
        WireDefaults(clinic);

        var alerts = await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        Assert.False(alerts.ExpiryAlertEnabled);
        Assert.Equal(0, alerts.ExpiringStock);
        _stock.Verify(r => r.CountExpiringSoonAsync(
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-9] The clinic's own lead window is used, not a hardcoded default.
    [Fact]
    public async Task Uses_The_Clinics_Own_Expiry_Lead_Window()
    {
        var clinic = ClinicFixture();
        clinic.SetStockExpiryLeadDays(90);
        WireDefaults(clinic);

        await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        _stock.Verify(r => r.CountExpiringSoonAsync(ClinicId, 90, FixedNow, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-9] The relance count applies the SAME exact rule the relance page applies (RecallDueRule), so the card and
    // the list it opens cannot show different numbers. The repository's bound is a deliberate superset, so a
    // candidate that is inside the widened window but not actually due must be excluded here too.
    [Fact]
    public async Task Recall_Count_Applies_The_Exact_Due_Rule_Not_The_Widened_Bound()
    {
        var clinic = ClinicFixture();
        clinic.SetRecallIntervalMonths(6);
        WireDefaults(clinic);

        // Due: anchor + 6 months is in the past.
        var due = Candidate(new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc));
        // Inside the three-day widened bound but NOT due: 16 December 2025 + 6 months = 16 June 2026 > 15 June 2026.
        var notYetDue = Candidate(new DateTime(2025, 12, 16, 0, 0, 0, DateTimeKind.Utc));

        _patients.Setup(r => r.GetRecallCandidatesAsync(
                ClinicId, It.IsAny<DateTime>(), FixedNow,
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { due, notYetDue });

        var alerts = await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        Assert.Equal(1, alerts.PatientsToRecall);
    }

    // [AC-9] An empty clinic reports zeros, not nulls or an error.
    [Fact]
    public async Task An_Empty_Clinic_Reports_Zeros()
    {
        WireDefaults();

        var alerts = await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        Assert.Equal(0, alerts.WaitingList);
        Assert.Equal(0, alerts.DraftPlans);
        Assert.Equal(0, alerts.PatientsToRecall);
        Assert.Equal(0, alerts.OverdueLabOrders);
        Assert.Equal(0, alerts.LowStock);
        Assert.Equal(0, alerts.ExpiringStock);
    }

    // [AC-1] Nothing is ever read for another clinic.
    [Fact]
    public async Task Every_Read_Is_Scoped_To_The_Callers_Clinic()
    {
        WireDefaults();

        await Reader().ReadAsync(ClinicId, FixedNow, CancellationToken.None);

        _waitingList.Verify(r => r.CountWaitingAsync(OtherClinicId, It.IsAny<CancellationToken>()), Times.Never);
        _stock.Verify(r => r.CountLowStockAsync(OtherClinicId, It.IsAny<CancellationToken>()), Times.Never);
        _labOrders.Verify(r => r.CountOverdueAsync(
            OtherClinicId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _patients.Verify(r => r.GetRecallCandidatesAsync(
            OtherClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static RecallCandidate Candidate(DateTime anchorUtc) => new(
        Guid.NewGuid(), "Jean", "Dupont", "+21620123456", anchorUtc, anchorUtc, null, null);
}
