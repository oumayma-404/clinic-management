using ClinicManagement.Application.Common;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Dashboard;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Application.Features.Expenses.Commands;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;

namespace ClinicManagement.Application.Features.Demo;

/// <summary>
/// Part B of the scenario (spec FR-6 « Treatments » and « Money »): a devis at every state, its séances recorded on
/// fiches that collect on the treatment, cheques, the month's expenses, one patient owing and one avoir.
/// </summary>
public sealed partial class DemoCabinetBuilder
{
    private static readonly string[] ChequeBanks = { "BIAT", "STB", "Attijari Bank", "BNA" };

    private async Task BuildTreatmentsAsync(CancellationToken ct)
    {
        await CompletedBridgeAsync(ct);
        await ImplantWaitingAsync(ct);
        await CrownInProgressAsync(ct);
        await StoppedTreatmentAsync(ct);
        await FollowedDraftAsync(ct);
    }

    // Leonard Green: a three-unit bridge 14·15·16, 15 the pontique, every séance done and paid — a completed devis.
    private async Task CompletedBridgeAsync(CancellationToken ct)
    {
        var act = Named("Couronne / bridge (par élément)");
        var teeth = new List<int> { 14, 15, 16 };
        var total = (act.DefaultCost ?? 500m) * teeth.Count;
        var plan = Must(await _seed.SendAsAsync(_monica, new CreateTreatmentPlanCommand
        {
            PatientId = PatientNamed("Leonard Green").Id,
            DoctorId = _monicaDoctorId,
            Title = "Bridge 14-16",
            Items = new() { new() { ProcedureTypeId = act.Id, DesignationFr = act.Name, PlannedCost = total, ToothNumbers = teeth } },
            Installments = new() { Due(-60, total / 2), Due(-30, total - total / 2) },
        }, ct), "créer le devis du bridge");

        var item = plan.Items[0];
        var collected = new[] { total / 2, 0m, total - total / 2 };
        var days = new[] { -60, -45, -30 };
        for (var step = 0; step < item.Steps.Count && step < days.Length; step++)
        {
            await RecordPlanSeanceAsync(_monica, _monicaDoctorId, "Leonard Green", act, plan.Id, item.Id, item.Steps[step].Id,
                teeth, ponticTeeth: new List<int> { 15 }, WorkingDayBefore(days[step]), collected[step], ct);
        }
    }

    // Pete Becker: the implant on 36 placed and checked; its crown waits 90 days (osseointegration); one échéance is late.
    private async Task ImplantWaitingAsync(CancellationToken ct)
    {
        var act = Named("Implant dentaire");
        var crown = Named("Couronne sur implant");
        var implantCost = act.DefaultCost ?? 1500m;
        var crownCost = crown.DefaultCost ?? 500m;
        var total = implantCost + crownCost;
        var share = Math.Round(total / 3, 3);
        var plan = Must(await _seed.SendAsAsync(_chandler, new CreateTreatmentPlanCommand
        {
            PatientId = PatientNamed("Pete Becker").Id,
            DoctorId = _chandlerDoctorId,
            Title = "Implant 36",
            Items = new()
            {
                new() { ProcedureTypeId = act.Id, DesignationFr = act.Name, PlannedCost = implantCost, ToothNumbers = new() { 36 } },
                new() { ProcedureTypeId = crown.Id, DesignationFr = crown.Name, PlannedCost = crownCost, ToothNumbers = new() { 36 } },
            },
            Installments = new() { Due(-21, share), Due(-7, share), Due(45, total - 2 * share) },
        }, ct), "créer le devis de l'implant");

        var item = plan.Items[0];
        var days = new[] { -35, -21, -12 };
        var collected = new[] { 0m, share, 0m };
        for (var step = 0; step < item.Steps.Count && step < days.Length; step++)
        {
            await RecordPlanSeanceAsync(_chandler, _chandlerDoctorId, "Pete Becker", act, plan.Id, item.Id, item.Steps[step].Id,
                new List<int> { 36 }, ponticTeeth: null, WorkingDayBefore(days[step]), collected[step], ct);
        }
    }

    // Ross Geller: a crown on 36 after his root canal — the préparation done, part paid, the next séance to come.
    private async Task CrownInProgressAsync(CancellationToken ct)
    {
        var act = Named("Couronne / bridge (par élément)");
        var total = act.DefaultCost ?? 500m;
        var plan = Must(await _seed.SendAsAsync(_monica, new CreateTreatmentPlanCommand
        {
            PatientId = PatientNamed("Ross Geller").Id,
            DoctorId = _monicaDoctorId,
            Title = "Couronne 36",
            Items = new() { new() { ProcedureTypeId = act.Id, DesignationFr = act.Name, PlannedCost = total, ToothNumbers = new() { 36 } } },
            Installments = new() { Due(-6, 200m), Due(20, total - 200m) },
        }, ct), "créer le devis de la couronne");

        var item = plan.Items[0];
        await RecordPlanSeanceAsync(_monica, _monicaDoctorId, "Ross Geller", act, plan.Id, item.Id, item.Steps[0].Id,
            new List<int> { 36 }, ponticTeeth: null, WorkingDayBefore(-6), 200m, ct);
    }

    // Mike Hannigan: détartrage done, whitening declined — the devis is stopped, keeping the work done.
    private async Task StoppedTreatmentAsync(CancellationToken ct)
    {
        var scaling = Named("Détartrage");
        var whitening = Named("Blanchiment dentaire");
        var kept = scaling.DefaultCost ?? 80m;
        var total = kept + (whitening.DefaultCost ?? 500m);
        var plan = Must(await _seed.SendAsAsync(_monica, new CreateTreatmentPlanCommand
        {
            PatientId = PatientNamed("Mike Hannigan").Id,
            DoctorId = _monicaDoctorId,
            Title = "Détartrage et blanchiment",
            Items = new()
            {
                new() { ProcedureTypeId = scaling.Id, DesignationFr = scaling.Name, PlannedCost = kept },
                new() { ProcedureTypeId = whitening.Id, DesignationFr = whitening.Name, PlannedCost = total - kept },
            },
            Installments = new() { Due(-10, total) },
        }, ct), "créer le devis détartrage + blanchiment");

        await RecordPlanSeanceAsync(_monica, _monicaDoctorId, "Mike Hannigan", scaling, plan.Id, plan.Items[0].Id,
            stepId: plan.Items[0].Steps.FirstOrDefault()?.Id, new List<int>(), ponticTeeth: null, WorkingDayBefore(-10), kept, ct);

        Must(await _seed.SendAsAsync(_monica, new StopTreatmentPlanCommand
        {
            Id = plan.Id,
            Reason = "Le patient a renoncé au blanchiment",
        }, ct), "arrêter le devis de Mike Hannigan");
    }

    // Jill Green: facettes followed as a treatment — the un-numbered draft « Suivre ce traitement » creates.
    private async Task FollowedDraftAsync(CancellationToken ct)
    {
        var act = Named("Facette (par élément)");
        Must(await _seed.SendAsAsync(_monica, new StartTreatmentCommand
        {
            PatientId = PatientNamed("Jill Green").Id,
            ProcedureTypeId = act.Id,
            ToothNumbers = new() { 11, 21 },
        }, ct), "suivre les facettes de Jill Green");
    }

    private async Task RecordPlanSeanceAsync(
        DemoMember dentist, Guid doctorId, string patientName, ProcedureTypeDto act, Guid planId, Guid itemId, Guid? stepId,
        List<int> teeth, List<int>? ponticTeeth, DateTime day, decimal collected, CancellationToken ct)
    {
        var patient = PatientNamed(patientName);
        Must(await _seed.SendAsAsync(dentist, new CreateDentalRecordCommand
        {
            PatientId = patient.Id,
            InterventionDate = ClinicClock.ToUtc(day + new TimeSpan(10, 0, 0)),
            DoctorId = doctorId,
            IsAdultTeeth = true,
            TreatmentPlanId = planId,
            TreatmentPlanItemId = itemId,
            TreatmentPlanItemStepId = stepId,
            // Priced on the treatment: the act is 0 on the fiche and the séance's money is collected on the devis.
            Acts = new()
            {
                new()
                {
                    ProcedureTypeId = act.Id,
                    ProcedureName = act.Name,
                    Cost = 0m,
                    ToothNumbers = teeth,
                    PonticToothNumbers = ponticTeeth ?? new List<int>(),
                    ResultingCondition = act.ResultingCondition,
                },
            },
            AmountCollectedOnPlan = collected,
            PaymentMethod = collected > 0 ? "Cash" : null,
        }, ct), $"enregistrer la séance de {patientName} du {day:dd/MM}");
    }

    // ---- money ---------------------------------------------------------------------------------------------------

    private async Task BuildMoneyAsync(CancellationToken ct)
    {
        await BankChequesAsync(ct);
        await ExpensesAsync(ct);
        await AvoirAsync(ct);
        await PatientOwingAsync(ct);
    }

    // Every cheque taken at the chair is banked, but the last one: « Chèques » has exactly one to deposit.
    private async Task BankChequesAsync(CancellationToken ct)
    {
        var cheques = _paidNotes.Where(n => n.Cheque).OrderBy(n => n.Day).ToList();
        foreach (var note in cheques.Take(Math.Max(0, cheques.Count - 1)))
        {
            var invoice = Must(await _seed.SendAsAsync(_rachel, new GetInvoiceQuery { Id = note.InvoiceId }, ct), "lire une note");
            foreach (var payment in invoice.Payments.Where(p => p.Method == "Cheque"))
            {
                Must(await _seed.SendAsAsync(_rachel, new SetPaymentBankedCommand
                {
                    InvoiceId = note.InvoiceId,
                    PaymentId = payment.Id,
                    Banked = true,
                }, ct), "remettre un chèque en banque");
            }
        }
    }

    /// <summary>Where « Net » lands on the dashboard's 30 days: about 3 000 DT, a little different each night.</summary>
    public decimal NetTarget => 3000m + _today.DayOfYear * 37 % 350;

    private async Task ExpensesAsync(CancellationToken ct)
    {
        // Ten days back, so each monthly charge sits once in the last 30 days and never in the last 7.
        var monthly = _today.AddDays(-10);
        while (monthly.Day > 28 || !DemoScenario.IsWorkingDay(monthly)) monthly = monthly.AddDays(-1);
        foreach (var (category, amount, description) in new[]
                 {
                     ("Loyer", 1200m, "Loyer du cabinet"), ("Salaires", 1500m, "Salaire de l'assistante"),
                     ("Salaires", 1100m, "Salaire de la secrétaire"), ("Charges sociales", 650m, "CNSS du personnel"),
                     ("Électricité/Eau", 180m, "STEG et SONEDE"), ("Téléphone/Internet", 90m, "Ligne fixe et fibre"),
                 })
        {
            Must(await _seed.SendAsAsync(_monica, new CreateExpenseCommand
            {
                ExpenseDate = DateTime.SpecifyKind(monthly, DateTimeKind.Utc),
                Category = category,
                Amount = amount,
                Method = "Transfer",
                Description = description,
                RepeatMonthly = true,
            }, ct), $"créer la dépense mensuelle « {description} »");
        }

        foreach (var (daysBack, category, amount, description, method) in new[]
                 {
                     (-3, "Fournitures", 230m, "Gants, masques, composites", "Card"),
                     (-8, "Laboratoire", 350m, "Prothèse — laboratoire Dentalab", "Cheque"),
                     (-12, "Maintenance", 120m, "Révision du compresseur", "Cash"),
                 })
        {
            Must(await _seed.SendAsAsync(_rachel, new CreateExpenseCommand
            {
                ExpenseDate = DateTime.SpecifyKind(WorkingDayBefore(daysBack), DateTimeKind.Utc),
                Category = category,
                Amount = amount,
                Method = method,
                Description = description,
            }, ct), $"créer la dépense « {description} »");
        }
    }

    /// <summary>
    /// The month's lab bill, sized from the dashboard's own « Net » so the 30 days read about <see cref="NetTarget"/>.
    /// Runs last: every fiche and expense above is already counted. Dated 18 days back — in the 30 days, out of the 7.
    /// </summary>
    private async Task BalanceNetAsync(CancellationToken ct)
    {
        var dashboard = Must(await _seed.SendAsAsync(_monica, new GetDashboardQuery { Period = DashboardPeriodKey.Month }, ct),
            "lire le tableau de bord");
        if (dashboard.Money?.Net.Current is not { } net) return;
        var bill = Math.Round(net - NetTarget, 3);
        if (bill <= 0m) return;
        Must(await _seed.SendAsAsync(_rachel, new CreateExpenseCommand
        {
            ExpenseDate = DateTime.SpecifyKind(WorkingDayBefore(-18), DateTimeKind.Utc),
            Category = "Laboratoire",
            Amount = bill,
            Method = "Transfer",
            Description = "Prothèses du mois — laboratoire Dentalab",
        }, ct), "régler la facture du laboratoire");
    }

    // A partial refund on an early note — an avoir, as a real cabinet issues one after a complaint.
    private async Task AvoirAsync(CancellationToken ct)
    {
        var note = _paidNotes.Where(n => !n.Cheque && n.Amount >= 50m).OrderBy(n => n.Day).First();
        Must(await _seed.SendAsAsync(_monica, new CreateCreditNoteCommand
        {
            InvoiceId = note.InvoiceId,
            Amount = 20m,
            Reason = "Geste commercial — attente prolongée",
            Method = "Cash",
            RefundedOn = ClinicClock.ToUtc(note.Day.AddDays(1) + new TimeSpan(10, 0, 0)),
        }, ct), "émettre l'avoir");
    }

    // Eddie Menuek: a soin paid half at the chair — the note stays open, so « Reste à payer » lists him.
    private async Task PatientOwingAsync(CancellationToken ct)
    {
        var act = Named("Soin de carie / obturation");
        var cost = act.DefaultCost ?? 90m;
        Must(await _seed.SendAsAsync(_monica, new CreateDentalRecordCommand
        {
            PatientId = PatientNamed("Eddie Menuek").Id,
            InterventionDate = ClinicClock.ToUtc(WorkingDayBefore(-16) + new TimeSpan(15, 0, 0)),
            DoctorId = _monicaDoctorId,
            IsAdultTeeth = true,
            Acts = new()
            {
                new() { ProcedureTypeId = act.Id, ProcedureName = act.Name, Cost = cost, UnitCost = cost, ToothNumbers = new() { 45 }, ResultingCondition = act.ResultingCondition },
            },
            AmountPaid = Math.Round(cost / 2, 3),
            PaymentMethod = "Cash",
            Notes = new() { "Reste à régler à la prochaine visite" },
        }, ct), "enregistrer la fiche d'Eddie Menuek");
    }

    // ---- helpers -------------------------------------------------------------------------------------------------

    private ProcedureTypeDto Named(string name) =>
        _catalogue.FirstOrDefault(p => p.Name == name)
        ?? throw new InvalidOperationException($"Démo : l'acte « {name} » manque au catalogue.");

    private InstallmentRequest Due(int daysFromToday, decimal amount) => new()
    {
        DueDate = ClinicClock.ToUtc(_today.AddDays(daysFromToday) + new TimeSpan(12, 0, 0)),
        Amount = amount,
    };

    /// <summary>The working day on or before <paramref name="daysFromToday"/>, so a séance never lands on a closed day.</summary>
    private DateTime WorkingDayBefore(int daysFromToday)
    {
        var day = _today.AddDays(daysFromToday);
        while (!DemoScenario.IsWorkingDay(day)) day = day.AddDays(-1);
        return day;
    }
}
