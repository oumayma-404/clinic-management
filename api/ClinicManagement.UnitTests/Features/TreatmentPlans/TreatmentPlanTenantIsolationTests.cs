using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Application.Features.TreatmentPlans.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// [AC-24] Treatment plans are strictly clinic-scoped: another clinic's devis reads as "not found" for every
/// verb, and no write is staged or committed. The plan area — a numbered financial document — had **no**
/// tenant-isolation guard at all, unlike every other money aggregate; this is it.
/// <para>
/// Covers every verb: get / update / accept / complete / cancel / delete / mark-done / record-payment /
/// amend / revise-installments / reorder, plus list scoping.
/// </para>
/// </summary>
public class TreatmentPlanTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IDentalActCodeRepository> _dentalActs = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    /// <summary>A draft devis belonging to a different clinic.</summary>
    private static TreatmentPlan ForeignDraftPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), OtherClinicId, PatientId, "Plan d'un autre cabinet");
        plan.SetItems(new[] { ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        return plan;
    }

    /// <summary>An accepted devis belonging to a different clinic (numbered, with a lump-sum échéance).</summary>
    private static TreatmentPlan ForeignAcceptedPlan()
    {
        var plan = ForeignDraftPlan();
        plan.Accept("2026-0001");
        return plan;
    }

    private void PlanIsLoadable(TreatmentPlan plan) =>
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);

    /// <summary>No write reached the database, and nothing was staged for one.</summary>
    private void NothingWasWritten()
    {
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _plans.Verify(r => r.UpdateAsync(It.IsAny<TreatmentPlan>(), It.IsAny<CancellationToken>()), Times.Never);
        _plans.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new GetTreatmentPlanQueryHandler(
            _plans.Object, _patients.Object, _appointments.Object, _invoices.Object, _clinicResolver.Object,
            NullLogger<GetTreatmentPlanQueryHandler>.Instance);

        var result = await handler.Handle(new GetTreatmentPlanQuery { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignDraftPlan();
        PlanIsLoadable(foreign);

        var handler = new UpdateTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _dentalActs.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UpdateTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateTreatmentPlanCommand { Id = foreign.Id, Title = "Détourné" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingWasWritten();
    }

    [Fact]
    public async Task Accept_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignDraftPlan();
        PlanIsLoadable(foreign);

        var handler = new AcceptTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<AcceptTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new AcceptTreatmentPlanCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentPlanStatus.Draft, foreign.Status);
        NothingWasWritten();
    }

    [Fact]
    public async Task Complete_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new CompleteTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CompleteTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new CompleteTreatmentPlanCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentPlanStatus.Accepted, foreign.Status);
        NothingWasWritten();
    }

    [Fact]
    public async Task Cancel_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new CancelTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CancelTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new CancelTreatmentPlanCommand { Id = foreign.Id, Reason = "Annulation hostile" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentPlanStatus.Accepted, foreign.Status);
        NothingWasWritten();
    }

    [Fact]
    public async Task Delete_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignDraftPlan();
        PlanIsLoadable(foreign);

        var handler = new DeleteTreatmentPlanCommandHandler(
            _plans.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DeleteTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(new DeleteTreatmentPlanCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingWasWritten();
    }

    // [AC-24] Marking an act done also auto-closes the plan (AC-11), so a cross-tenant call here would both
    // leak and mutate a foreign clinic's clinical record.
    [Fact]
    public async Task MarkItemDone_On_A_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);
        var itemId = foreign.Items.First().Id;

        var handler = new MarkTreatmentPlanItemDoneCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<MarkTreatmentPlanItemDoneCommandHandler>.Instance);

        var result = await handler.Handle(
            new MarkTreatmentPlanItemDoneCommand { PlanId = foreign.Id, ItemId = itemId }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentPlanItemStatus.Planned, foreign.Items.First().Status);
        NothingWasWritten();
    }

    // [AC-24] The money verb: recording a payment against another clinic's échéance must not touch it.
    [Fact]
    public async Task RecordInstallmentPayment_On_A_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);
        var installmentId = foreign.Installments.First().Id;

        var handler = new RecordInstallmentPaymentCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<RecordInstallmentPaymentCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordInstallmentPaymentCommand
            {
                PlanId = foreign.Id,
                InstallmentId = installmentId,
                Amount = 100m,
                Method = "Cash",
                PaidOn = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0m, foreign.AmountPaid);
        NothingWasWritten();
    }

    // [AC-24] Amending another clinic's devis reads as introuvable — and, critically, the billed-plan and
    // live-appointment lookups must not even run for a foreign plan.
    [Fact]
    public async Task Amend_A_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new AmendTreatmentPlanCommandHandler(
            _plans.Object, _patients.Object, _invoices.Object, _appointments.Object, _dentalActs.Object,
            _clinicResolver.Object, _uow.Object, NullLogger<AmendTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(new AmendTreatmentPlanCommand
        {
            Id = foreign.Id,
            AddItems = new List<TreatmentPlanItemRequest> { new() { DesignationFr = "Implant", PlannedCost = 500m } },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(500m, foreign.TotalPlanned);
        Assert.Equal(0, foreign.RevisionNumber);
        _invoices.Verify(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        NothingWasWritten();
    }

    [Fact]
    public async Task ReviseInstallments_On_A_Foreign_Plan_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new ReviseTreatmentPlanInstallmentsCommandHandler(
            _plans.Object, _patients.Object, _invoices.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<ReviseTreatmentPlanInstallmentsCommandHandler>.Instance);

        var result = await handler.Handle(new ReviseTreatmentPlanInstallmentsCommand
        {
            Id = foreign.Id,
            Installments = new List<InstallmentRequest>
            {
                new() { DueDate = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), Amount = 500m },
            },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Single(foreign.Installments);
        NothingWasWritten();
    }

    [Fact]
    public async Task Reorder_A_Foreign_Plans_Acts_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignAcceptedPlan();
        PlanIsLoadable(foreign);

        var handler = new SetTreatmentPlanItemOrderCommandHandler(
            _plans.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<SetTreatmentPlanItemOrderCommandHandler>.Instance);

        var result = await handler.Handle(new SetTreatmentPlanItemOrderCommand
        {
            Id = foreign.Id,
            ItemIds = foreign.Items.Select(i => i.Id).ToList(),
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        NothingWasWritten();
    }

    // [AC-24] The list is scoped to the caller's clinic — the repo is only ever queried with that id.
    [Fact]
    public async Task List_Is_Scoped_To_Caller_Clinic()
    {
        Authenticated();
        _plans.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<TreatmentPlan>()).AsPage());
        _patients.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Patient>()).AsPage());
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        var handler = new GetTreatmentPlansQueryHandler(
            _plans.Object, _patients.Object, _appointments.Object, _invoices.Object, _clinicResolver.Object,
            NullLogger<GetTreatmentPlansQueryHandler>.Instance);

        var result = await handler.Handle(new GetTreatmentPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Items);
        _plans.Verify(r => r.GetFilteredAsync(
            ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
