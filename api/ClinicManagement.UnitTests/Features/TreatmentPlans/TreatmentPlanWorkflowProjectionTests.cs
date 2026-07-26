using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// [AC-1][AC-2][AC-3][AC-3a][AC-5][AC-6][AC-8] The derivation core: which appointment speaks for a planned
/// act, and which invoice already bills the plan. Nothing here is persisted — an act's état is recomputed on
/// every read, which is why cancelling or deleting the appointment silently un-schedules it.
/// </summary>
public class TreatmentPlanWorkflowProjectionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OtherPatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    /// <summary>Fixed "now" so every date in these tests is unambiguously past or future.</summary>
    private static readonly DateTime AsOf = new(2026, 8, 5, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Future = new(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterFuture = new(2026, 8, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Past = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EarlierPast = new(2026, 7, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();

    private static TreatmentPlan PlanWithOneAct(Guid? patientId = null)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, patientId ?? PatientId, "Plan");
        plan.SetItems(new[] { ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        return plan;
    }

    private static Appointment LinkedAppointment(Guid itemId, DateTime at) =>
        new(Guid.NewGuid(), ClinicId, PatientId, null, at, TimeSpan.FromMinutes(30), treatmentPlanItemId: itemId);

    private void NoInvoiceLinks() =>
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

    private void LinkedAppointments(params Appointment[] appointments) =>
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointments);

    private Task<TreatmentPlanWorkflow> Build(params TreatmentPlan[] plans) =>
        TreatmentPlanWorkflowProjection.BuildAsync(
            plans, ClinicId, _appointments.Object, _invoices.Object, AsOf, CancellationToken.None);

    // [AC-1] An act with no linked appointment reports no scheduling data at all — the frontend renders
    // « À planifier » and offers "Planifier".
    [Fact]
    public async Task Act_With_No_Appointment_Has_No_Scheduling_Data()
    {
        var plan = PlanWithOneAct();
        LinkedAppointments();
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Empty(workflow.ScheduledByItemId);
        Assert.Null(workflow.NextAppointmentAtByPlanId[plan.Id]);
    }

    // [AC-1] A future Scheduled appointment is the act's representative and is also the plan's next séance.
    [Fact]
    public async Task Act_With_A_Future_Live_Appointment_Reports_It()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        LinkedAppointments(LinkedAppointment(itemId, Future));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(Future, workflow.ScheduledByItemId[itemId].AppointmentDateTime);
        Assert.Equal(Future, workflow.NextAppointmentAtByPlanId[plan.Id]);
    }

    // [AC-1] Confirmed and InProgress count as live bookings too, not just Scheduled.
    [Theory]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.InProgress)]
    public async Task Confirmed_And_InProgress_Appointments_Are_Live(AppointmentStatus status)
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        var appointment = LinkedAppointment(itemId, Future);
        appointment.Confirm();
        if (status == AppointmentStatus.InProgress) appointment.Start();
        LinkedAppointments(appointment);
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(status, appointment.Status);
        Assert.True(workflow.ScheduledByItemId.ContainsKey(itemId));
    }

    // [AC-2] THE highest-risk case in the feature: a cancelled appointment must un-schedule the act. If it
    // counted, the act would read « Planifié » forever *and* "Planifier" would stay hidden — permanently
    // unbookable, with no way out from the UI.
    [Fact]
    public async Task Cancelled_Appointment_Un_Schedules_The_Act()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        var appointment = LinkedAppointment(itemId, Future);
        appointment.Cancel("Patient indisponible");
        LinkedAppointments(appointment);
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Empty(workflow.ScheduledByItemId);
        Assert.Null(workflow.NextAppointmentAtByPlanId[plan.Id]);
    }

    // [AC-2] Same for a no-show: the visit did not happen, so the act returns to « À planifier ».
    [Fact]
    public async Task NoShow_Appointment_Un_Schedules_The_Act()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        var appointment = LinkedAppointment(itemId, Past);
        appointment.MarkAsNoShow();
        LinkedAppointments(appointment);
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Empty(workflow.ScheduledByItemId);
    }

    // [AC-2] A rebooked act — the cancelled one is ignored and the live replacement speaks for the act.
    [Fact]
    public async Task A_Live_Replacement_Wins_Over_A_Cancelled_Appointment()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        var cancelled = LinkedAppointment(itemId, Future);
        cancelled.Cancel("Reporté");
        LinkedAppointments(cancelled, LinkedAppointment(itemId, LaterFuture));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(LaterFuture, workflow.ScheduledByItemId[itemId].AppointmentDateTime);
    }

    // [AC-3] Several live appointments on one act: the earliest still-upcoming one is the representative.
    [Fact]
    public async Task Earliest_Future_Appointment_Wins()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        LinkedAppointments(
            LinkedAppointment(itemId, LaterFuture),
            LinkedAppointment(itemId, Future),
            LinkedAppointment(itemId, Past));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(Future, workflow.ScheduledByItemId[itemId].AppointmentDateTime);
    }

    // [AC-3] When every linked appointment is in the past, the most recent one wins — so a réalisé act still
    // shows the visit it actually happened at rather than the first one ever booked.
    [Fact]
    public async Task All_Past_Appointments_Fall_Back_To_The_Latest()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        LinkedAppointments(
            LinkedAppointment(itemId, EarlierPast),
            LinkedAppointment(itemId, Past));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(Past, workflow.ScheduledByItemId[itemId].AppointmentDateTime);
    }

    // [AC-3a] A past live appointment on an act that is not yet Réalisé still reports its appointment (the
    // frontend renders « À enregistrer (RDV JJ/MM) ») but must NOT count as a « prochaine séance » — a header
    // claiming an upcoming visit its own acts report as past is the bug this état exists to prevent.
    [Fact]
    public async Task Past_Appointment_Is_Reported_But_Is_Not_A_Next_Seance()
    {
        var plan = PlanWithOneAct();
        var itemId = plan.Items.First().Id;
        LinkedAppointments(LinkedAppointment(itemId, Past));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(Past, workflow.ScheduledByItemId[itemId].AppointmentDateTime);
        Assert.Null(workflow.NextAppointmentAtByPlanId[plan.Id]);
    }

    // [AC-5] « Prochaine séance » is the earliest future appointment across ALL of the plan's acts, not the
    // first act's.
    [Fact]
    public async Task Next_Seance_Is_The_Earliest_Future_Across_All_Acts()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[]
        {
            ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }),
            ("Détartrage", 60m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 12 }),
        });
        var acts = plan.Items.ToList();
        LinkedAppointments(
            LinkedAppointment(acts[0].Id, LaterFuture),
            LinkedAppointment(acts[1].Id, Future));
        NoInvoiceLinks();

        var workflow = await Build(plan);

        Assert.Equal(Future, workflow.NextAppointmentAtByPlanId[plan.Id]);
    }

    // [AC-8] A linked non-cancelled invoice marks the plan « Facturé » and carries its number, which is what
    // hides "Facturer le devis" so a second bridge invoice cannot be created.
    [Fact]
    public async Task Linked_Issued_Invoice_Marks_The_Plan_Billed()
    {
        var plan = PlanWithOneAct();
        var invoiceId = Guid.NewGuid();
        LinkedAppointments();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (plan.Id, invoiceId, "2026-0031", InvoiceStatus.Issued)
            });

        var workflow = await Build(plan);

        Assert.Equal(invoiceId, workflow.InvoiceByPlanId[plan.Id].InvoiceId);
        Assert.Equal("2026-0031", workflow.InvoiceByPlanId[plan.Id].Number);
    }

    // [AC-5][AC-8] A cancelled bridge no longer represents the plan: the plan stops reading « Facturé » and
    // becomes billable again — the same rule the money reads apply, so the badge and « Solde patient » agree.
    [Fact]
    public async Task Cancelled_Invoice_Does_Not_Mark_The_Plan_Billed()
    {
        var plan = PlanWithOneAct();
        LinkedAppointments();
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (plan.Id, Guid.NewGuid(), "2026-0030", InvoiceStatus.Cancelled)
            });

        var workflow = await Build(plan);

        Assert.Empty(workflow.InvoiceByPlanId);
    }

    // [AC-6] The whole page is served by exactly two reads — one appointments query and one invoice-links
    // query — never one per plan and never one per patient.
    [Fact]
    public async Task Builds_A_Multi_Plan_Page_With_Exactly_Two_Reads()
    {
        var plans = new[] { PlanWithOneAct(), PlanWithOneAct(), PlanWithOneAct(OtherPatientId) };
        LinkedAppointments();
        NoInvoiceLinks();

        await Build(plans);

        _appointments.Verify(r => r.GetByTreatmentPlanItemIdsAsync(
            ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _invoices.Verify(r => r.GetTreatmentPlanLinksAsync(
            ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-6] Every act on the page goes into the single batched appointment lookup.
    [Fact]
    public async Task Batched_Read_Covers_Every_Act_On_The_Page()
    {
        var first = PlanWithOneAct();
        var second = PlanWithOneAct();
        var expected = new[] { first.Items.First().Id, second.Items.First().Id };
        LinkedAppointments();
        NoInvoiceLinks();

        await Build(first, second);

        _appointments.Verify(r => r.GetByTreatmentPlanItemIdsAsync(
            ClinicId,
            It.Is<IReadOnlyCollection<Guid>>(ids => expected.All(ids.Contains) && ids.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
