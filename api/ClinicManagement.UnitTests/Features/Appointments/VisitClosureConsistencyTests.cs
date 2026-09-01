using ClinicManagement.Application.Features.Appointments;
using ClinicManagement.Application.Features.Dashboard.Readers;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// The dashboard's « À clôturer » chip and the worklist it opens must always report the same number.
///
/// <para>This is the rule <c>DashboardAlertsReader</c> already states for every other figure it carries — « each
/// count reuses the <i>same</i> predicate its destination list uses, so a card can never disagree with the page it
/// opens » — and here it is held structurally, by both reading <see cref="VisitClosureReader"/>. These cases exist
/// because that guarantee is invisible: two separately-derived counts agree on almost every fixture, and disagree
/// on exactly the cases that matter (a séance carried by a devis, a contrôle gratuit, a recorded waiver).</para>
///
/// <para>It is <c>MoneyReadConsistencyTests</c>' shape, one subsystem over.</para>
/// </summary>
public class VisitClosureConsistencyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IDentalRecordRepository> _dentalRecords = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IWaitingListRepository> _waitingList = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ILabWorkOrderRepository> _labOrders = new();
    private readonly Mock<IStockItemRepository> _stock = new();
    private readonly Mock<IClinicRepository> _clinics = new();

    /// <summary>A closed visit: <paramref name="hoursAgo"/> back, an hour long, one patient.</summary>
    private static Appointment Visit(AppointmentStatus status, int hoursAgo = 3)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, Guid.NewGuid(), doctorId: null, Now.AddHours(-hoursAgo),
            TimeSpan.FromHours(1));
        Advance(appointment, status);
        return appointment;
    }

    /// <summary>Walks the aggregate to <paramref name="status"/> through its own declared transitions.</summary>
    private static void Advance(Appointment appointment, AppointmentStatus status)
    {
        switch (status)
        {
            case AppointmentStatus.Completed: appointment.MarkVisitCompleted(); break;
            case AppointmentStatus.NoShow: appointment.MarkAsNoShow(); break;
            case AppointmentStatus.Cancelled: appointment.Cancel(); break;
            case AppointmentStatus.InProgress: appointment.Start(); break;
        }
    }

    /// <summary>
    /// A visit whose fiche is on a live note d'honoraires that names <b>no appointment</b> — the state every
    /// séance billed before <c>DentalRecord.AppointmentId</c> was populated is in.
    ///
    /// <para>It was reported as « Encaissement à faire » on money already collected, i.e. the worklist asked the
    /// practice to charge the patient twice. Verified against a real clinic database: five paid fiches, five live
    /// invoices, <c>Invoices.AppointmentId</c> null on every one.</para>
    /// </summary>
    [Fact]
    public async Task A_Fiche_On_A_Live_Invoice_Closes_The_Money_Question_Without_An_Appointment_Link()
    {
        var visit = Visit(AppointmentStatus.Completed);
        var ficheId = Guid.NewGuid();
        Wire(visit);

        _dentalRecords.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (visit.Id, ficheId, 400m) });

        // The note bills the fiche and names no appointment — GetAppointmentLinksAsync stays empty.
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (ficheId, Guid.NewGuid(), (string?)"2026-0001", InvoiceStatus.Paid) });

        Assert.Empty(await ReadWorklist());
        Assert.Equal(0, (await ReadDashboard()).VisitsToClose);
    }

    /// <summary>A <b>cancelled</b> note bills nothing, so the séance stays open — the same rule
    /// <c>AppointmentInvoiceLinks</c> applies to the appointment-linked side.</summary>
    [Fact]
    public async Task A_Cancelled_Note_On_The_Fiche_Leaves_The_Money_Question_Open()
    {
        var visit = Visit(AppointmentStatus.Completed);
        var ficheId = Guid.NewGuid();
        Wire(visit);

        _dentalRecords.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (visit.Id, ficheId, 400m) });

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (ficheId, Guid.NewGuid(), (string?)"2026-0001", InvoiceStatus.Cancelled) });

        var open = await ReadWorklist();

        Assert.Single(open);
        Assert.Equal(VisitClosureStep.Billing, open[0].State.NextStep);
    }

    private void Wire(params Appointment[] candidates)
    {
        _appointments.Setup(r => r.GetClosureCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        _dentalRecords.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, decimal)>());

        _invoices.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        // The fiche→note side. Nothing billed by default; the two cases above override it.
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        _plans.Setup(r => r.GetDebtBearingItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        // Everything the alerts reader needs besides the closure count.
        _clinics.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test", code: "CODE01"));
        _waitingList.Setup(r => r.CountWaitingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _plans.Setup(r => r.CountByStatusAsync(
                It.IsAny<Guid>(), It.IsAny<TreatmentPlanStatus>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _patients.Setup(r => r.GetRecallCandidatesAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IReadOnlyCollection<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<RecallCandidate>());
        _labOrders.Setup(r => r.CountOverdueAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _stock.Setup(r => r.CountLowStockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        _stock.Setup(r => r.CountExpiringSoonAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    // `.Open` — the half « À clôturer » and the dashboard chip both render. The reader also returns the séances
    // somebody has set aside, and these tests are about the two surfaces agreeing on the open ones.
    private async Task<IReadOnlyList<OpenVisit>> ReadWorklist() =>
        (await VisitClosureReader.ReadAsync(
            ClinicId, days: null, doctorId: null, Now,
            _appointments.Object, _dentalRecords.Object, _invoices.Object, _plans.Object)).Open;

    private Task<ClinicManagement.Application.DTOs.DashboardAlertsDto> ReadDashboard() =>
        new DashboardAlertsReader(
            _waitingList.Object, _plans.Object, _patients.Object, _labOrders.Object, _stock.Object,
            _clinics.Object, _appointments.Object, _dentalRecords.Object, _invoices.Object)
            .ReadAsync(ClinicId, Now, CancellationToken.None);

    // Three open visits and two that are not: whatever the rule decides, both surfaces must decide it identically.
    [Fact]
    public async Task The_Dashboard_Count_Equals_The_Worklists_Own_Length()
    {
        Wire(
            Visit(AppointmentStatus.InProgress),
            Visit(AppointmentStatus.Scheduled),
            Visit(AppointmentStatus.Completed),
            Visit(AppointmentStatus.Cancelled),
            Visit(AppointmentStatus.NoShow));

        var worklist = await ReadWorklist();
        var alerts = await ReadDashboard();

        Assert.Equal(worklist.Count, alerts.VisitsToClose);
    }

    // The figure is not merely equal by both being zero — the fixture has to produce real rows, or this test would
    // still pass with the count hard-wired to 0.
    [Fact]
    public async Task The_Fixture_Really_Produces_Open_Visits()
    {
        Wire(
            Visit(AppointmentStatus.InProgress),
            Visit(AppointmentStatus.Scheduled),
            Visit(AppointmentStatus.Cancelled));

        var alerts = await ReadDashboard();

        // Two open (the cancelled one is out of scope entirely, not « closed »).
        Assert.Equal(2, alerts.VisitsToClose);
    }

    [Fact]
    public async Task An_Empty_Clinic_Reports_Zero_On_Both_Sides()
    {
        Wire();

        Assert.Empty(await ReadWorklist());
        Assert.Equal(0, (await ReadDashboard()).VisitsToClose);
    }

    // The order is the client's day grouping — « Aujourd'hui » must be the first heading — and it has to be
    // decided here because the read is paged: reversing a page in the browser only reverses within it.
    [Fact]
    public async Task The_Worklist_Is_Most_Recent_First()
    {
        var oldest = Visit(AppointmentStatus.Scheduled, hoursAgo: 72);
        var newest = Visit(AppointmentStatus.Scheduled, hoursAgo: 3);
        var middle = Visit(AppointmentStatus.Scheduled, hoursAgo: 30);
        Wire(oldest, newest, middle);

        var worklist = await ReadWorklist();

        Assert.Equal(
            new[] { newest.Id, middle.Id, oldest.Id },
            worklist.Select(v => v.Appointment.Id).ToArray());
    }
}
