using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// Wave 2 of <c>devis-fiche-rdv-flexibility</c>: edits that were lost and treatments created by accident. See
/// <c>features/devis-fiche-rdv-flexibility/notes.md</c>.
/// </summary>
public class DevisFicheRdvFlexibilityWave2Tests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Fiche = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime Day1 = new(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Crown = Guid.NewGuid();
    private static readonly Guid Scaling = Guid.NewGuid();

    private static TreatmentPlan TwoActDevis(Guid first, Guid second)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Couronne", 300m, first, Array.Empty<int>()),
            new TreatmentPlanItemInput(null, "Détartrage", 80m, second, Array.Empty<int>()),
        });
        plan.Accept("2026-0200");
        return plan;
    }

    private static DentalRecordActInput Act(Guid procedure, decimal cost) =>
        new(procedure, "Acte", cost, null, false, Array.Empty<int>(), ToothCondition.Couronne, null, null);

    private static Mock<ITreatmentPlanRepository> Plans(TreatmentPlan plan)
    {
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plans;
    }

    private static Mock<IAppointmentRepository> NoVisits()
    {
        var repository = new Mock<IAppointmentRepository>();
        repository
            .Setup(r => r.GetByTreatmentPlanItemIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment>());
        return repository;
    }

    private static Mock<ICurrentClinicResolver> Clinic()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        return resolver;
    }

    // ── C4: one fiche closes several devis acts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Seance_With_Two_Devis_Acts_Prices_Both_At_Zero_And_Closes_Both()
    {
        var plan = TwoActDevis(Crown, Scaling);
        var crown = plan.Items.Single(i => i.ProcedureTypeId == Crown);
        var scaling = plan.Items.Single(i => i.ProcedureTypeId == Scaling);
        var plans = Plans(plan).Object;
        var acts = new List<DentalRecordActInput> { Act(Crown, 0m), Act(Scaling, 80m) };

        var resolved = await FicheExtraPlanActs.ResolveAsync(
            plans, acts, plan.Id, crown.Id, new[] { new FichePlanItemLink(scaling.Id) }, null, ClinicId,
            CancellationToken.None);

        Assert.Equal(0m, resolved.Acts[1].Cost);
        var extra = Assert.Single(resolved.Extras);
        Assert.Equal(1, extra.ActIndex);

        foreach (var itemId in new[] { crown.Id, scaling.Id })
        {
            var link = await DentalRecordLinker.LinkPlanItemAsync(
                plans, NoVisits().Object, plan.Id, itemId, PatientId, ClinicId, Fiche, Day1,
                CancellationToken.None);
            Assert.True(link.IsSuccess);
        }
        Assert.All(plan.Items, i => Assert.Equal(TreatmentPlanItemStatus.Done, i.Status));
    }

    [Fact]
    public async Task Two_Crowns_On_One_Devis_Take_Two_Different_Acts_Of_The_Fiche()
    {
        var plan = TwoActDevis(Crown, Crown);
        var first = plan.Items.OrderBy(i => i.SequenceNumber).First();
        var second = plan.Items.OrderBy(i => i.SequenceNumber).Last();
        var acts = new List<DentalRecordActInput> { Act(Crown, 0m), Act(Crown, 300m) };

        var resolved = await FicheExtraPlanActs.ResolveAsync(
            Plans(plan).Object, acts, plan.Id, first.Id, new[] { new FichePlanItemLink(second.Id) }, null,
            ClinicId, CancellationToken.None);

        Assert.Equal(1, Assert.Single(resolved.Extras).ActIndex);
        Assert.Equal(0m, resolved.Acts[1].Cost);
    }

    [Fact]
    public async Task A_Re_Save_Keeps_The_Second_Act_Even_When_The_Client_Names_Only_The_First()
    {
        var plan = TwoActDevis(Crown, Scaling);
        var crown = plan.Items.Single(i => i.ProcedureTypeId == Crown);
        var scaling = plan.Items.Single(i => i.ProcedureTypeId == Scaling);
        plan.MarkItemDone(scaling.Id, Day1, Fiche);
        var acts = new List<DentalRecordActInput> { Act(Crown, 0m), Act(Scaling, 80m) };

        var resolved = await FicheExtraPlanActs.ResolveAsync(
            Plans(plan).Object, acts, plan.Id, crown.Id, Array.Empty<FichePlanItemLink>(), Fiche, ClinicId,
            CancellationToken.None);

        Assert.Equal(scaling.Id, Assert.Single(resolved.Extras).Item.Id);
        Assert.Equal(0m, resolved.Acts[1].Cost);
    }

    [Fact]
    public void An_Unfinished_Extra_Act_Does_Not_Chart_Its_End_State()
    {
        var acts = new List<DentalRecordActInput> { Act(Crown, 0m), Act(Scaling, 0m) };

        var chartable = FicheExtraPlanActs.Chartable(acts, new[] { (1, false) });

        Assert.Equal(ToothCondition.Couronne, chartable[0].ResultingCondition);
        Assert.Null(chartable[1].ResultingCondition);
    }

    // ── F6 / F7: a protocol is decided once, when the act enters the plan ───────────────────────────────

    [Fact]
    public async Task Accepting_Keeps_An_Act_Set_To_One_Seance_And_Never_Reads_The_Catalogue()
    {
        var plan = TwoActDevis(Crown, Scaling);
        var crown = plan.Items.Single(i => i.ProcedureTypeId == Crown);
        plan.SetItemSteps(crown.Id, new[] { new TreatmentPlanItemStepInput(null, "Préparation", 30) });

        var confirmed = TreatmentPlanStepProtocol.AsConfirmed(plan);
        var catalogue = new Mock<IProcedureTypeRepository>(MockBehavior.Strict);

        await TreatmentPlanStepProtocol.ApplyAsync(plan, ClinicId, catalogue.Object, CancellationToken.None, confirmed);

        Assert.Empty(plan.Items.Single(i => i.ProcedureTypeId == Scaling).Steps);
        Assert.Single(crown.Steps);
    }

    [Fact]
    public async Task An_Amendment_Protocols_Only_The_Acts_It_Adds()
    {
        var plan = TwoActDevis(Crown, Scaling);
        var existing = plan.Items.Single(i => i.ProcedureTypeId == Crown);
        var added = plan.Items.Single(i => i.ProcedureTypeId == Scaling);
        var twoSeances = new List<IReadOnlyList<TreatmentPlanItemStepInput>?>
        {
            new[] { new TreatmentPlanItemStepInput(null, "A", 30), new TreatmentPlanItemStepInput(null, "B", 30) },
            new[] { new TreatmentPlanItemStepInput(null, "A", 30), new TreatmentPlanItemStepInput(null, "B", 30) },
        };

        await TreatmentPlanStepProtocol.ApplyAsync(
            plan, ClinicId, new Mock<IProcedureTypeRepository>().Object, CancellationToken.None, twoSeances,
            onlyItemIds: new[] { added.Id });

        Assert.Empty(existing.Steps);
        Assert.Equal(2, added.Steps.Count);
    }

    // ── F5: the draft editor refuses what it would drop ─────────────────────────────────────────────────

    [Fact]
    public async Task The_Draft_Editor_Refuses_A_Seance_It_Would_Drop()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Brouillon");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 300m, Crown, Array.Empty<int>()) });
        var handler = new UpdateTreatmentPlanCommandHandler(
            Plans(plan).Object, new Mock<IPatientRepository>().Object, new Mock<IProcedureTypeRepository>().Object,
            Clinic().Object, new Mock<IUnitOfWork>().Object, NullLogger<UpdateTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateTreatmentPlanCommand
        {
            Id = plan.Id,
            Title = "Brouillon",
            Items = new List<TreatmentPlanItemRequest>
            {
                new()
                {
                    Id = plan.Items.Single().Id, ProcedureTypeId = Crown, DesignationFr = "Couronne", PlannedCost = 300m,
                    Steps = new List<ClinicManagement.Application.DTOs.TreatmentPlanItemStepRequest> { new() { Label = "Préparation" } },
                },
            },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("Modifier les actes et les prix", result.Error);
    }

    // ── E4: an abandoned booking takes its treatment with it ────────────────────────────────────────────

    private static DiscardBookingPlanCommandHandler Discard(TreatmentPlan plan, Mock<ISender> sender,
        IAppointmentRepository? visits = null) =>
        new(Plans(plan).Object, visits ?? NoVisits().Object, Clinic().Object, sender.Object,
            NullLogger<DiscardBookingPlanCommandHandler>.Instance);

    [Fact]
    public async Task An_Abandoned_Draft_Is_Deleted()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Suivi");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Implant", 900m, Crown, Array.Empty<int>()) });
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<DeleteTreatmentPlanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await Discard(plan, sender).Handle(new DiscardBookingPlanCommand { Id = plan.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        sender.Verify(s => s.Send(It.Is<DeleteTreatmentPlanCommand>(c => c.Id == plan.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task An_Abandoned_Numbered_Devis_Is_Cancelled_Not_Left_As_A_Debt()
    {
        var plan = TwoActDevis(Crown, Scaling);
        var sender = new Mock<ISender>();
        sender.Setup(s => s.Send(It.IsAny<CancelTreatmentPlanCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TreatmentPlanDto>.Success(new TreatmentPlanDto()));

        var result = await Discard(plan, sender).Handle(new DiscardBookingPlanCommand { Id = plan.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        sender.Verify(s => s.Send(It.Is<CancelTreatmentPlanCommand>(c => c.Id == plan.Id), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task A_Plan_A_Visit_Books_Or_An_Old_One_Is_Never_Discarded()
    {
        var booked = TwoActDevis(Crown, Scaling);
        var visit = new Appointment(Guid.NewGuid(), ClinicId, PatientId, null, Day1, TimeSpan.FromMinutes(30));
        visit.SetProcedures(new[]
        {
            new AppointmentProcedureInput(Crown, "Couronne", 30, null, null, booked.Items.First().Id, null),
        });
        var visits = new Mock<IAppointmentRepository>();
        visits.Setup(r => r.GetByTreatmentPlanItemIdsAsync(ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Appointment> { visit });
        var sender = new Mock<ISender>(MockBehavior.Strict);

        var refusedBooked = await Discard(booked, sender, visits.Object)
            .Handle(new DiscardBookingPlanCommand { Id = booked.Id }, CancellationToken.None);

        var old = TwoActDevis(Crown, Scaling);
        typeof(TreatmentPlan).GetProperty(nameof(TreatmentPlan.CreatedAt))!
            .SetValue(old, DateTime.UtcNow.AddDays(-1));
        var refusedOld = await Discard(old, sender).Handle(new DiscardBookingPlanCommand { Id = old.Id }, CancellationToken.None);

        Assert.True(refusedBooked.IsFailure);
        Assert.True(refusedOld.IsFailure);
    }
}
