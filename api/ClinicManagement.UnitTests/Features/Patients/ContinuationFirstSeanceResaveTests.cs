using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Re-saving the FIRST séance of a continuation whose « reste » was priced — the note keeps that séance's act and
/// the devis holds it at 0 « sur la note », unattached.
///
/// <para><b>Production, 06/11/2026.</b> Traitement de canal on 46, 300 DT on note 2026-0141, 100 paid. Continued
/// with a priced reste (devis 2026-0016). Reopening the 30/10 fiche: the server zeroed the act (« imposing 0 in
/// place of the 300 sent »), « Corriger la note » with nothing paid cancelled the note, voided the 100 and raised
/// nothing — the 300 DT séance was billed nowhere. The dentist only meant « le patient n'a pas payé ces 100 ».</para>
/// </summary>
public class ContinuationFirstSeanceResaveTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProcedureTypeId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTime Seance1 = new(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
    private const decimal ActCost = 300m;
    private const decimal Paid = 100m;

    private readonly Patient _patient = new(Guid.NewGuid(), ClinicId, "Amine", "Trabelsi", new DateTime(1985, 4, 12), "Male");
    private readonly DentalRecord _fiche;
    private readonly Invoice _note;
    private readonly TreatmentPlan _plan;

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IToothStateRepository> _toothStates = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ISender> _sender = new();
    private readonly List<BillDentalRecordCommand> _billed = new();

    public ContinuationFirstSeanceResaveTests()
    {
        _fiche = new DentalRecord(Guid.NewGuid(), _patient.Id, ClinicId, Seance1, Paid, isAdultTeeth: true);
        _fiche.SetActs(new[] { Act(ActCost) });

        _note = new Invoice(Guid.NewGuid(), ClinicId, _patient.Id, dentalRecordId: _fiche.Id);
        _note.SetLines(new[] { ("Traitement de canal (dévitalisation)", 1, ActCost, (Guid?)_fiche.Id) });
        _note.Issue("2026-0141");
        _note.RecordPayment(Paid, PaymentMethod.Cash, Seance1);

        // What `ContinueRecordedActCommand`'s billed path with a priced reste leaves behind.
        _plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, _patient.Id, "Traitement de canal (dévitalisation)");
        _plan.SetItems(new[]
        {
            new TreatmentPlanItemInput(null, "Traitement de canal (dévitalisation)", 0m, ProcedureTypeId, new List<int> { 46 }),
            new TreatmentPlanItemInput(null, "Séance suivante", 200m, ProcedureTypeId, new List<int> { 46 }),
        });
        var carried = FirstItem;
        _plan.MarkItemBilledOnInvoice(carried.Id, _note.Id, ActCost);
        _plan.Accept("2026-0016");
        _plan.SetItemSteps(carried.Id, new[] { new TreatmentPlanItemStepInput(null, "1re séance", null) });
        _plan.SetItemSteps(_plan.Items.OrderBy(i => i.SequenceNumber).Last().Id,
            new[] { new TreatmentPlanItemStepInput(null, "Séance suivante", null) });
        _plan.MarkItemStepDone(carried.Id, FirstItem.Steps.First().Id, Seance1, _fiche.Id);

        _patients.Setup(r => r.GetByIdAsync(_patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_patient);
        _records.Setup(r => r.GetByIdAsync(_fiche.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_fiche);
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _toothStates.Setup(r => r.GetByDentalRecordIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ToothState>());
        _toothStates.Setup(r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ToothState>());
        _toothStates.Setup(r => r.AddAsync(It.IsAny<ToothState>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToothState s, CancellationToken _) => s);
        _creditNotes.Setup(r => r.GetTotalForInvoiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        _invoices.Setup(r => r.GetByIdAsync(_note.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_note);
        _invoices.Setup(r => r.GetByDentalRecordAsync(ClinicId, _fiche.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _note });
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[] { (_fiche.Id, _note.Id, _note.Number, _note.Status) });

        _plans.Setup(r => r.GetByIdAsync(_plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_plan);
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, _fiche.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { _plan });
        _plans.Setup(r => r.GetPlansBilledOnInvoiceAsync(ClinicId, _note.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new[] { (_plan.Id, _plan.Number, _plan.Status) });

        _sender.Setup(s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<DentalRecordBillingResult>> c, CancellationToken _) =>
                _billed.Add((BillDentalRecordCommand)c))
            .ReturnsAsync(Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
            {
                Outcome = DentalRecordBillingOutcome.Billed,
                Invoice = new InvoiceDto { Number = "2026-0150" },
                AmountCollected = 50m,
            }));
    }

    private TreatmentPlanItem FirstItem => _plan.Items.OrderBy(i => i.SequenceNumber).First();

    private static DentalRecordActInput Act(decimal cost) => new(
        ProcedureTypeId, "Traitement de canal (dévitalisation)", cost, cost, false, new[] { 46 },
        null, null, null);

    private UpdateDentalRecordCommandHandler Handler() => new(
        _records.Object, _patients.Object, _toothStates.Object, _plans.Object,
        new Mock<IProcedureTypeRepository>().Object, new Mock<IAppointmentRepository>().Object,
        _invoices.Object, _creditNotes.Object, new Mock<IMedicalDocumentRepository>().Object,
        new Mock<IClinicRepository>().Object, new Mock<IDoctorRepository>().Object,
        new Mock<IClinicContext>().Object, _resolver.Object, _uow.Object,
        new Mock<IStockConsumptionService>().Object, _sender.Object,
        NullLogger<UpdateDentalRecordCommandHandler>.Instance);

    /// <summary>The fiche as the modal sends it on reopen: still linked to the act the devis holds at 0.</summary>
    private UpdateDentalRecordCommand Resave(decimal paid, string? correctionReason = null) => new()
    {
        Id = _fiche.Id,
        PatientId = _patient.Id,
        InterventionDate = Seance1,
        AmountPaid = paid,
        IsAdultTeeth = true,
        CorrectionReason = correctionReason,
        TreatmentPlanId = _plan.Id,
        TreatmentPlanItemId = FirstItem.Id,
        Acts = new List<DentalActInput>
        {
            new()
            {
                ProcedureTypeId = ProcedureTypeId,
                ProcedureName = "Traitement de canal (dévitalisation)",
                Cost = ActCost,
                UnitCost = ActCost,
                IsPerTooth = false,
                ToothNumbers = new List<int> { 46 },
            },
        },
    };

    // ── fix 1: the note prices this act, so the devis' 0 is not imposed on its own fiche ─────────────────

    [Fact]
    public async Task The_Continued_Act_Keeps_Its_Fee_On_The_Fiche_Its_Note_Bills()
    {
        var imposed = await PlanCarriedActPricing.ImposeAsync(
            _plans.Object, new List<DentalRecordActInput> { Act(ActCost) }, _plan.Id, FirstItem.Id, ClinicId,
            NullLogger.Instance, CancellationToken.None, invoiceRepository: _invoices.Object, ficheId: _fiche.Id);

        Assert.Null(imposed.Refusal);
        Assert.Equal(ActCost, imposed.Acts.Single().Cost);
    }

    // Another séance naming the same act is a second claim on work the note already bills — still 0.
    [Fact]
    public async Task Another_Fiche_Naming_The_Carried_Act_Is_Still_Priced_Zero()
    {
        var imposed = await PlanCarriedActPricing.ImposeAsync(
            _plans.Object, new List<DentalRecordActInput> { Act(ActCost) }, _plan.Id, FirstItem.Id, ClinicId,
            NullLogger.Instance, CancellationToken.None, invoiceRepository: _invoices.Object, ficheId: Guid.NewGuid());

        Assert.Equal(0m, imposed.Acts.Single().Cost);
    }

    // The create path passes no fiche (a new fiche has no note) and keeps the old rule.
    [Fact]
    public async Task Without_A_Fiche_The_Carried_Act_Is_Priced_Zero_As_Before()
    {
        var imposed = await PlanCarriedActPricing.ImposeAsync(
            _plans.Object, new List<DentalRecordActInput> { Act(ActCost) }, _plan.Id, FirstItem.Id, ClinicId,
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(0m, imposed.Acts.Single().Cost);
    }

    // The reported case, end to end: reopen the 30/10 fiche, press Enregistrer, nothing changed.
    [Fact]
    public async Task Re_Saving_The_First_Seance_Unchanged_Keeps_Its_Fee_And_Its_Note()
    {
        var result = await Handler().Handle(Resave(paid: Paid), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(ActCost, _fiche.Cost);
        Assert.Equal(InvoiceStatus.PartiallyPaid, _note.Status);
        Assert.False(_note.Payments.Single().IsVoided);
    }

    // ── fix 2: « le patient n'a pas payé » — Payé 100 → 0 keeps a note billing the séance ────────────────

    [Fact]
    public async Task Correcting_To_Nothing_Paid_Raises_An_Unpaid_Note_And_Leaves_The_Devis_Alone()
    {
        var totalBefore = _plan.TotalPlanned;

        var result = await Handler().Handle(
            Resave(paid: 0m, correctionReason: "Correction — note remplacée depuis l'application."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvoiceStatus.Cancelled, _note.Status);
        Assert.True(_note.Payments.Single().IsVoided);
        var replacement = Assert.Single(_billed);
        Assert.Equal(_note.Id, replacement.SupersedesInvoiceId);
        Assert.Null(replacement.PaidNow);
        Assert.False(replacement.IsAutomatic);
        Assert.Equal(ActCost, _fiche.Cost);
        Assert.Equal(0m, _fiche.AmountPaid);
        Assert.Equal(totalBefore, _plan.TotalPlanned);
    }

    // With money paid the note is replaced, and reads find the replacement through the fiche — allowed.
    [Fact]
    public async Task Correcting_With_Money_Paid_Still_Replaces_The_Note()
    {
        var result = await Handler().Handle(
            Resave(paid: 50m, correctionReason: "Correction — note remplacée depuis l'application."),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvoiceStatus.Cancelled, _note.Status);
        Assert.Equal(_note.Id, Assert.Single(_billed).SupersedesInvoiceId);
    }

    // An ordinary fiche corrected to nothing paid keeps a note too — it used to be left billed nowhere.
    [Fact]
    public async Task An_Ordinary_Fiche_Corrected_To_Nothing_Paid_Keeps_An_Unpaid_Note()
    {
        _plans.Setup(r => r.GetPlansBilledOnInvoiceAsync(ClinicId, _note.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, string?, TreatmentPlanStatus)>());
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, _fiche.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());

        var command = Resave(paid: 0m, correctionReason: "Correction — note remplacée depuis l'application.");
        command.TreatmentPlanId = null;
        command.TreatmentPlanItemId = null;
        command.Acts.Single().Cost = 250m;
        command.Acts.Single().UnitCost = 250m;

        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(InvoiceStatus.Cancelled, _note.Status);
        Assert.Null(Assert.Single(_billed).PaidNow);
    }

    // An ordinary save with nothing paid still raises nothing — the replacement is a correction's alone.
    [Fact]
    public async Task An_Ordinary_Save_With_Nothing_Paid_Still_Bills_Nothing()
    {
        var billing = await DentalRecordAutoBilling.BillIfPaidAsync(
            _sender.Object, _fiche, 0m, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(nameof(DentalRecordBillingOutcome.NotCollected), billing.Outcome);
        Assert.Empty(_billed);
    }
}
