using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.TreatmentPlans;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// Wave 3 of <c>devis-fiche-rdv-flexibility</c> — money (G1–G12). See
/// <c>features/devis-fiche-rdv-flexibility/notes.md</c>.
/// </summary>
public class DevisFicheRdvFlexibilityWave3Tests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Today = new(2026, 9, 23, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Oct = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Nov = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Dec = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Aug = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>One 300 DT act, an agreed schedule of 100 · 100 · 100 on Oct · Nov · Dec.</summary>
    private static TreatmentPlan ScheduledDevis()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Bridge", 300m, null, Array.Empty<int>()) });
        plan.SetInstallments(new[] { (Oct, 100m), (Nov, 100m), (Dec, 100m) });
        plan.Accept("2026-0300");
        return plan;
    }

    /// <summary>One 300 DT act accepted with no schedule (one auto row), 300 collected in August.</summary>
    private static TreatmentPlan PaidDevis()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 300m, null, Array.Empty<int>()) });
        plan.Accept("2026-0301");
        plan.RecordInstallmentPayment(plan.Installments.Single().Id, 300m, PaymentMethod.Cash, Aug);
        return plan;
    }

    private static (DateTime, decimal)[] Rows(TreatmentPlan plan) =>
        plan.Installments.OrderBy(i => i.DueDate).Select(i => (i.DueDate, i.Amount)).ToArray();

    private static Mock<ICurrentClinicResolver> Clinic()
    {
        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        return resolver;
    }

    // ── G2: a change of total keeps the agreed dates ─────────────────────────────────────────────────

    [Fact]
    public void G2_A_Remise_Comes_Off_The_Last_Unpaid_Row_And_Keeps_Every_Date()
    {
        var plan = ScheduledDevis();

        plan.SetItemDiscount(plan.Items.Single().Id, 50m, Today);

        Assert.Equal(new[] { (Oct, 100m), (Nov, 100m), (Dec, 50m) }, Rows(plan));
    }

    [Fact]
    public void G2_A_Remise_Larger_Than_The_Last_Row_Drops_It_And_Trims_The_One_Before()
    {
        var plan = ScheduledDevis();

        plan.SetItemDiscount(plan.Items.Single().Id, 150m, Today);

        Assert.Equal(new[] { (Oct, 100m), (Nov, 50m) }, Rows(plan));
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    [Fact]
    public void G2_A_Higher_Total_Lands_On_The_Last_Unpaid_Row()
    {
        var plan = ScheduledDevis();
        plan.SetItemDiscount(plan.Items.Single().Id, 50m, Today);

        plan.SetItemDiscount(plan.Items.Single().Id, 0m, Today);

        Assert.Equal(new[] { (Oct, 100m), (Nov, 100m), (Dec, 100m) }, Rows(plan));
    }

    [Fact]
    public void G2_A_Paid_Row_Is_Never_Touched_By_A_Lower_Total()
    {
        var plan = ScheduledDevis();
        var october = plan.Installments.OrderBy(i => i.DueDate).First();
        plan.RecordInstallmentPayment(october.Id, 100m, PaymentMethod.Cash, Aug);

        plan.SetItemDiscount(plan.Items.Single().Id, 150m, Today);

        Assert.Equal(new[] { (Oct, 100m), (Nov, 50m) }, Rows(plan));
        Assert.Equal(100m, plan.Installments.Single(i => i.DueDate == Oct).AmountPaid);
    }

    [Fact]
    public void G2_The_Auto_Lump_Sum_Stays_Auto_When_It_Grows()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Couronne", 300m, null, Array.Empty<int>()) });
        plan.Accept("2026-0302");
        plan.SetItemDiscount(plan.Items.Single().Id, 100m, Today);

        plan.SetItemDiscount(plan.Items.Single().Id, 0m, Today);

        var row = Assert.Single(plan.Installments);
        Assert.Equal(300m, row.Amount);
        Assert.True(row.IsAutoRaised);
    }

    // ── G10 / G11 ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void G10_A_Price_Change_On_An_Unnumbered_Treatment_Writes_No_Schedule()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Traitement");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Implant", 900m, null, Array.Empty<int>()) });

        plan.RespreadScheduleToTotal(Today);

        Assert.Empty(plan.Installments);
        Assert.Null(plan.Number);
    }

    [Fact]
    public void G11_Editing_The_Steps_Of_A_Written_Off_Devis_Does_Not_Bring_The_Debt_Back()
    {
        var plan = ScheduledDevis();
        plan.WriteOff("Patient parti");

        plan.SetItemSteps(plan.Items.Single().Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 30, null),
            new TreatmentPlanItemStepInput(null, "Scellement", 30, null),
        });

        Assert.Equal(TreatmentPlanStatus.WrittenOff, plan.Status);
        Assert.False(PlanBillingRules.CarriesDebt(plan.Status));
    }

    // ── G3: « Rendre au patient » ─────────────────────────────────────────────────────────────────

    [Fact]
    public void G3_Lowering_Below_What_Was_Collected_Without_A_Method_Is_Refused()
    {
        var plan = PaidDevis();

        var ex = Assert.Throws<InvalidOperationException>(
            () => plan.SetItemDiscount(plan.Items.Single().Id, 100m, Today));

        Assert.Contains("Rendez", ex.Message);
        Assert.DoesNotContain("avoir", ex.Message);
    }

    [Fact]
    public void G3_A_Confirmed_Rendu_Is_Money_Leaving_Today_And_Leaves_The_Past_Day_Alone()
    {
        var plan = PaidDevis();

        plan.SetItemDiscount(plan.Items.Single().Id, 100m, Today, PaymentMethod.Cash);

        var payments = plan.Installments.SelectMany(i => i.Payments).ToList();
        var receipt = Assert.Single(payments, p => !p.IsRefund);
        var rendu = Assert.Single(payments, p => p.IsRefund);
        Assert.Equal((300m, Aug), (receipt.Amount, receipt.PaidOn));
        Assert.Equal((-100m, Today, PaymentMethod.Cash), (rendu.Amount, rendu.PaidOn, rendu.Method));
        Assert.Null(rendu.DentalRecordId);
        Assert.Equal(200m, plan.TotalPlanned);
        Assert.Equal(200m, plan.AmountPaid);
        Assert.Equal(0m, plan.Outstanding);
        Assert.Equal(plan.TotalPlanned, plan.Installments.Sum(i => i.Amount));
    }

    [Fact]
    public void G3_A_Rendu_Is_Neither_Voidable_Nor_A_Reason_To_Cancel()
    {
        var plan = PaidDevis();
        plan.SetItemDiscount(plan.Items.Single().Id, 100m, Today, PaymentMethod.Cash);
        var row = plan.Installments.Single();
        var rendu = row.Payments.Single(p => p.IsRefund);
        var receipt = row.Payments.Single(p => !p.IsRefund);

        Assert.Throws<InvalidOperationException>(() => plan.VoidInstallmentPayment(row.Id, rendu.Id, "x", null, null));
        Assert.Throws<InvalidOperationException>(() => plan.VoidInstallmentPayment(row.Id, receipt.Id, "x", null, null));
        Assert.True(plan.HasReceipts);
        Assert.Throws<InvalidOperationException>(() => plan.Cancel("Erreur"));
    }

    [Fact]
    public void G3_A_Cheque_Is_Never_A_Rendu()
    {
        var plan = PaidDevis();

        Assert.Throws<ArgumentException>(
            () => plan.SetItemDiscount(plan.Items.Single().Id, 100m, Today, PaymentMethod.Cheque));
    }

    [Fact]
    public void G3_Stopping_A_Devis_With_Only_A_Deposit_Gives_It_Back_And_Parks_The_Acts()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Devis");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Implant", 900m, null, Array.Empty<int>()) });
        plan.Accept("2026-0303");
        plan.RecordInstallmentPayment(plan.Installments.Single().Id, 200m, PaymentMethod.Cash, Aug);
        Assert.Equal(200m, plan.RefundOnStop);

        plan.StopTreatment(Today, PaymentMethod.Cash);

        Assert.Equal(TreatmentPlanStatus.Stopped, plan.Status);
        Assert.Equal(0m, plan.AmountPaid);
        Assert.Equal(0m, plan.TotalPlanned);
        // The receipt row keeps its August day; nothing was cascaded away.
        Assert.Contains(plan.Installments.SelectMany(i => i.Payments), p => p.Amount == 200m && p.PaidOn == Aug);
    }

    [Fact]
    public async Task G3_The_Remise_Endpoint_Names_The_Refusal_With_A_Code_Then_Accepts_The_Method()
    {
        var plan = PaidDevis();
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var handler = new SetTreatmentPlanItemDiscountCommandHandler(
            plans.Object, new Mock<IPatientRepository>().Object, Clinic().Object,
            new Mock<IUnitOfWork>().Object, NullLogger<SetTreatmentPlanItemDiscountCommandHandler>.Instance);

        var refused = await handler.Handle(new SetTreatmentPlanItemDiscountCommand
        {
            PlanId = plan.Id, ItemId = plan.Items.Single().Id, DiscountAmount = 100m,
        }, CancellationToken.None);

        Assert.True(refused.IsFailure);
        Assert.Equal(PlanRefund.Code, refused.Code);
        Assert.Contains("100,000 DT sont à rendre", refused.Error);

        // A fresh load, as the retry would get.
        var fresh = PaidDevis();
        plans.Setup(r => r.GetByIdAsync(fresh.Id, It.IsAny<CancellationToken>())).ReturnsAsync(fresh);
        var accepted = await handler.Handle(new SetTreatmentPlanItemDiscountCommand
        {
            PlanId = fresh.Id, ItemId = fresh.Items.Single().Id, DiscountAmount = 100m, RefundMethod = "Cash",
        }, CancellationToken.None);

        Assert.True(accepted.IsSuccess, accepted.Error);
        Assert.Equal(200m, fresh.AmountPaid);
    }

    // ── G5: the fiche's date carries the devis money and the séance ─────────────────────────────────

    [Fact]
    public void G5_Redating_A_Fiche_Moves_Its_Devis_Payment_And_Its_Seance()
    {
        var fiche = Guid.NewGuid();
        var plan = ScheduledDevis();
        var item = plan.Items.Single();
        plan.SetItemSteps(item.Id, new[]
        {
            new TreatmentPlanItemStepInput(null, "Préparation", 30, null),
            new TreatmentPlanItemStepInput(null, "Scellement", 30, null),
        });
        var step = plan.Items.Single().Steps.OrderBy(s => s.SequenceNumber).First();
        plan.MarkItemStepDone(item.Id, step.Id, Aug, fiche);
        plan.CollectChairside(80m, PaymentMethod.Cash, Aug, null, fiche);
        var corrected = Aug.AddDays(-3);

        var moved = plan.FollowDentalRecordDate(fiche, corrected);

        Assert.True(moved);
        Assert.All(plan.Installments.SelectMany(i => i.Payments).Where(p => p.DentalRecordId == fiche),
            p => Assert.Equal(corrected, p.PaidOn));
        Assert.Equal(corrected, plan.Items.Single().Steps.Single(s => s.Id == step.Id).DoneDate);
    }

    // ── G7 ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void G7_A_Written_Off_Devis_Still_Holds_Cash_And_Owes_Nothing()
    {
        Assert.Contains(TreatmentPlanStatus.WrittenOff, PlanBillingRules.CashBearingPlanStatuses);
        Assert.DoesNotContain(TreatmentPlanStatus.WrittenOff, PlanBillingRules.DebtBearingPlanStatuses);
        Assert.All(PlanBillingRules.DebtBearingPlanStatuses,
            s => Assert.Contains(s, PlanBillingRules.CashBearingPlanStatuses));
    }

    // ── G9: no devis number spent on a refused collection ─────────────────────────────────────────

    [Fact]
    public async Task G9_Collecting_More_Than_An_Unnumbered_Treatment_Is_Worth_Mints_No_Number()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Traitement");
        plan.SetItems(new[] { new TreatmentPlanItemInput(null, "Canal", 100m, null, Array.Empty<int>()) });
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, Today, 0m, true);
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var records = new Mock<IDentalRecordRepository>();
        records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        var handler = new CollectOnTreatmentCommandHandler(
            plans.Object, new Mock<IProcedureTypeRepository>().Object, records.Object, Clinic().Object,
            new Mock<IUnitOfWork>().Object, NullLogger<CollectOnTreatmentCommandHandler>.Instance);

        var result = await handler.Handle(new CollectOnTreatmentCommand
        {
            TreatmentPlanId = plan.Id, DentalRecordId = record.Id, Amount = 150m, Method = "Cash",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(TreatmentCollectionRefusals.ExceedsOutstandingCode, result.Code);
        Assert.Null(plan.Number);
        plans.Verify(r => r.GetMaxSequenceForYearAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── G12 ───────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task G12_A_Written_Off_Devis_Cannot_Be_Invoiced()
    {
        var plan = ScheduledDevis();
        plan.WriteOff("Patient parti");
        var plans = new Mock<ITreatmentPlanRepository>();
        plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        var invoices = new Mock<IInvoiceRepository>();
        var handler = new CreateInvoiceFromTreatmentPlanCommandHandler(
            invoices.Object, plans.Object, new Mock<IPatientRepository>().Object, Clinic().Object,
            new Mock<IUnitOfWork>().Object, NullLogger<CreateInvoiceFromTreatmentPlanCommandHandler>.Instance);

        var result = await handler.Handle(
            new CreateInvoiceFromTreatmentPlanCommand { TreatmentPlanId = plan.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("perte", result.Error);
        invoices.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── G1: a typed 0 is a price ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task G1_A_Typed_Zero_Is_Kept_And_Only_A_Blank_Price_Takes_The_Tarif()
    {
        var procedureId = Guid.NewGuid();
        var procedures = new Mock<IProcedureTypeRepository>();
        procedures.Setup(r => r.GetByIdAsync(procedureId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcedureType(procedureId, ClinicId, "Couronne", 30, ColorHex.FromString("#2A9D8F"),
                defaultCost: 90m));
        var pricing = typeof(AmendTreatmentPlanCommand).Assembly
            .GetType("ClinicManagement.Application.Features.TreatmentPlans.Commands.TreatmentPlanItemPricing")!
            .GetMethod("ResolveWithIdsAsync", BindingFlags.Public | BindingFlags.Static)!;

        var task = (Task<List<TreatmentPlanItemInput>>)pricing.Invoke(null, new object[]
        {
            new[]
            {
                new TreatmentPlanItemRequest { DesignationFr = "Couronne", PlannedCost = 0m, ProcedureTypeId = procedureId },
                new TreatmentPlanItemRequest { DesignationFr = "Couronne", PlannedCost = null, ProcedureTypeId = procedureId },
            },
            ClinicId, procedures.Object, CancellationToken.None,
        })!;
        var resolved = await task;

        Assert.Equal(new[] { 0m, 90m }, resolved.Select(r => r.PlannedCost));
    }
}
