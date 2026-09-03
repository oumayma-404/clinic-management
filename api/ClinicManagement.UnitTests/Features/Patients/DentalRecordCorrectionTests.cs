using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Correcting a fiche de soins whose note d'honoraires disagrees with it, and carrying a re-dated séance's money
/// with it (L4).
///
/// <para>The refusals themselves are <c>DentalRecordBillingGuard</c>'s and predate this; what is new is the
/// <b>way out</b> of them. « Les actes ne peuvent plus être modifiés. Établissez un avoir » used to be the end
/// of the road — the action it named lives in another page's row menu, and an avoir is the wrong document
/// anyway, since it records money handed back and a mis-keyed amount handed nothing back.</para>
///
/// <para>⚠️ Everything destructive here runs <b>pre-commit</b>, for the reason the guard's own docstring gives:
/// the auto-billing is post-commit, so a refusal raised from there arrives after the edit is saved and leaves
/// the fiche permanently disagreeing with its note. « Refusé » has to mean the save did not happen.</para>
/// </summary>
public class DentalRecordCorrectionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Intervention = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Backdated = new(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>The séance's one act. Shared by the fixture and the command so the two cannot drift.</summary>
    private static DentalRecordActInput ActInput(decimal cost) => new(
        ProcedureTypeId: null,
        ProcedureName: "Soin de carie / obturation",
        Cost: cost,
        UnitCost: cost / 2m,
        IsPerTooth: true,
        ToothNumbers: new[] { 26, 27 },
        ResultingCondition: ToothCondition.Obturation,
        Surfaces: null,
        Note: null);

    private sealed class Harness
    {
        public Mock<IDentalRecordRepository> Records { get; } = new();
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IToothStateRepository> ToothStates { get; } = new();
        public Mock<ITreatmentPlanRepository> Plans { get; } = new();
        public Mock<IInvoiceRepository> Invoices { get; } = new();
        public Mock<ICreditNoteRepository> CreditNotes { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IStockConsumptionService> Stock { get; } = new();
        public Mock<ISender> Sender { get; } = new();

        public Patient Patient { get; }
        public DentalRecord Record { get; }
        public Invoice? Note { get; private set; }

        /// <summary>Every <c>BillDentalRecordCommand</c> the post-commit billing sent — the re-bill is one.</summary>
        public List<BillDentalRecordCommand> Billed { get; } = new();

        public Harness()
        {
            Patient = new Patient(
                Guid.NewGuid(), ClinicId, "Leila", "Gharbi",
                new DateTime(1985, 3, 2, 0, 0, 0, DateTimeKind.Utc), "F",
                new Email("leila.gharbi@example.com"), new PhoneNumber("+21620123456"));
            Record = new DentalRecord(Guid.NewGuid(), Patient.Id, ClinicId, Intervention, 180m, true);
            Record.SetActs(new[] { ActInput(180m) });

            Patients.Setup(r => r.GetByIdAsync(Patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Patient);
            Records.Setup(r => r.GetByIdAsync(Record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Record);
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
            ToothStates.Setup(r => r.GetByDentalRecordIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ToothState>());
            ToothStates.Setup(r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<ToothState>());
            ToothStates.Setup(r => r.AddAsync(It.IsAny<ToothState>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ToothState s, CancellationToken _) => s);
            Invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
            CreditNotes.Setup(r => r.GetTotalForInvoiceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0m);

            // The post-commit billing goes through MediatR; capture the command rather than mocking the outcome,
            // because WHICH command is sent is the assertion (IsAutomatic, SupersedesInvoiceId).
            Sender.Setup(s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()))
                .Callback((IRequest<Result<DentalRecordBillingResult>> c, CancellationToken _) =>
                    Billed.Add((BillDentalRecordCommand)c))
                .ReturnsAsync(Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
                {
                    Outcome = DentalRecordBillingOutcome.Billed,
                    Invoice = new InvoiceDto { Number = "2026-0074" },
                    AmountCollected = 150m,
                }));
        }

        /// <summary>Put a live, fully-paid note d'honoraires behind the fiche.</summary>
        public Invoice GiveItANote(decimal total = 180m, PaymentMethod method = PaymentMethod.Cash,
            ChequeDetails? cheque = null, ChequeBankedStamp? banked = null)
        {
            var invoice = new Invoice(Guid.NewGuid(), ClinicId, Patient.Id, dentalRecordId: Record.Id);
            invoice.SetLines(new[] { ("Soin de carie / obturation", 2, total / 2m, (Guid?)Record.Id) });
            invoice.Issue("2026-0073");
            invoice.RecordPayment(total, method, Intervention, cheque: cheque, banked: banked);

            Invoices.Setup(r => r.GetDentalRecordLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { (Record.Id, invoice.Id, (string?)"2026-0073", invoice.Status) });
            Invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

            Note = invoice;
            return invoice;
        }

        /// <summary>
        /// Read only so the linker can answer « which steps did this séance carry out? ». Unstubbed, so
        /// `GetByIdAsync` returns null and the linker falls through to the named step — which is this
        /// fixture's case: no record here is linked to an appointment.
        /// </summary>
        public Mock<IAppointmentRepository> Appointments { get; } = new();

        public UpdateDentalRecordCommandHandler Handler() => new(
            Records.Object, Patients.Object, ToothStates.Object, Plans.Object, Appointments.Object,
            Invoices.Object,
            CreditNotes.Object, Resolver.Object, Uow.Object, Stock.Object, Sender.Object,
            NullLogger<UpdateDentalRecordCommandHandler>.Instance);

        public UpdateDentalRecordCommand Command(decimal cost, decimal paid, DateTime? on = null,
            string? correctionReason = null) => new()
        {
            Id = Record.Id,
            PatientId = Patient.Id,
            InterventionDate = on ?? Intervention,
            AmountPaid = paid,
            IsAdultTeeth = true,
            CorrectionReason = correctionReason,
            Acts = new List<DentalActInput>
            {
                new()
                {
                    ProcedureName = "Soin de carie / obturation",
                    Cost = cost,
                    UnitCost = cost / 2m,
                    IsPerTooth = true,
                    ToothNumbers = new List<int> { 26, 27 },
                    ResultingCondition = "Obturation",
                },
            },
        };
    }

    // ── the refusal still refuses ─────────────────────────────────────────────────────────────────────

    // Retiring a numbered document is never something a routine re-save should do by itself, so the correction
    // is opt-in and the ordinary save is refused exactly as before.
    [Fact]
    public async Task Lowering_The_Acts_Without_Asking_To_Correct_Is_Still_Refused()
    {
        var h = new Harness();
        var note = h.GiveItANote();

        var result = await h.Handler().Handle(h.Command(cost: 150m, paid: 150m), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.ActsChangedCode, result.Code);
        Assert.Equal(InvoiceStatus.Paid, note.Status);
        Assert.False(note.Payments.Single().IsVoided);
        Assert.Empty(h.Billed);
    }

    // ⚠️ « Refusé » means the save did not happen — asserted on the aggregate, not only on the Result: the whole
    // reason this check is pre-commit is that a refusal arriving afterwards leaves the fiche disagreeing with
    // its own note for ever.
    [Fact]
    public async Task A_Refusal_Writes_Nothing()
    {
        var h = new Harness();
        h.GiveItANote();

        await h.Handler().Handle(h.Command(cost: 150m, paid: 150m), CancellationToken.None);

        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── the correction ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Correcting_Voids_The_Payment_And_Cancels_The_Note()
    {
        var h = new Harness();
        var note = h.GiveItANote();

        var result = await h.Handler().Handle(
            h.Command(cost: 150m, paid: 150m, correctionReason: "Erreur de tarif"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(note.Payments.Single().IsVoided);
        Assert.Equal("Erreur de tarif", note.Payments.Single().VoidReason);
        Assert.Equal(InvoiceStatus.Cancelled, note.Status);
        Assert.Equal("Erreur de tarif", note.CancellationReason);
    }

    // ⚠️ THE case this whole branch turns on. The automatic path deliberately refuses to raise a note for a
    // fiche whose note is spent (A-1) — that refusal is what stops a routine re-save producing a second
    // document — but here the cancellation was asked for, so the replacement must be raised explicitly.
    // Left as `IsAutomatic = true` the correction cancels the note and silently raises nothing, which is the
    // worst outcome available: a séance with work recorded and no bill at all.
    [Fact]
    public async Task The_Rebill_Is_Explicit_And_Names_The_Note_It_Replaces()
    {
        var h = new Harness();
        var note = h.GiveItANote();

        await h.Handler().Handle(
            h.Command(cost: 150m, paid: 150m, correctionReason: "Erreur de tarif"), CancellationToken.None);

        var rebill = Assert.Single(h.Billed);
        Assert.False(rebill.IsAutomatic);
        Assert.Equal(note.Id, rebill.SupersedesInvoiceId);
        Assert.Equal(h.Record.Id, rebill.DentalRecordId);
    }

    // An ordinary save must not acquire any of this: still automatic, still nothing superseded.
    [Fact]
    public async Task An_Ordinary_Save_Bills_Automatically_And_Supersedes_Nothing()
    {
        var h = new Harness();

        var result = await h.Handler().Handle(h.Command(cost: 180m, paid: 180m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var billed = Assert.Single(h.Billed);
        Assert.True(billed.IsAutomatic);
        Assert.Null(billed.SupersedesInvoiceId);
    }

    // A fiche nothing bills has no note to retire; the reason is simply inert rather than an error.
    [Fact]
    public async Task A_Correction_Reason_On_An_Unbilled_Fiche_Changes_Nothing()
    {
        var h = new Harness();

        var result = await h.Handler().Handle(
            h.Command(cost: 150m, paid: 150m, correctionReason: "Erreur"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(h.Billed).IsAutomatic);
    }

    // ── L4: the séance's date carries its money ───────────────────────────────────────────────────────

    // The reported defect: the séance moved to the 31st and its money stayed in the new month, so
    // « encaissé ce mois » reported a figure nobody could explain. No document is touched — the note keeps the
    // day it was written, and only the record of when cash changed hands is corrected.
    [Fact]
    public async Task Backdating_The_Seance_Moves_Its_Payments_And_Not_Its_Note()
    {
        var h = new Harness();
        var note = h.GiveItANote();
        var issuedOn = note.IssueDate;

        var result = await h.Handler().Handle(
            h.Command(cost: 180m, paid: 180m, on: Backdated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Backdated, note.Payments.Single().PaidOn);
        Assert.Equal(issuedOn, note.IssueDate);
        Assert.Equal(InvoiceStatus.Paid, note.Status);
        Assert.Equal(180m, note.AmountCollected);
    }

    // Saving a fiche without touching its date must not restamp the money — a re-save is routine (a corrected
    // note, one more tooth), and rewriting PaidOn on each one would drag a payment forward day by day.
    [Fact]
    public async Task Re_Saving_Without_Changing_The_Date_Leaves_The_Payment_Alone()
    {
        var h = new Harness();
        var note = h.GiveItANote();

        await h.Handler().Handle(h.Command(cost: 180m, paid: 180m), CancellationToken.None);

        Assert.Equal(Intervention, note.Payments.Single().PaidOn);
    }

    // That row is reconciled against a bank statement. Refused with its own code — silently leaving it behind
    // is the very shape of failure this fixes.
    [Fact]
    public async Task A_Banked_Cheque_Refuses_The_Re_Dating_With_Its_Own_Code()
    {
        var h = new Harness();
        var note = h.GiveItANote(
            method: PaymentMethod.Cheque,
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", Intervention.AddDays(15)),
            banked: ChequeBankedStamp.For(PaymentMethod.Cheque, Intervention.AddDays(2), "local|abc", "Dr B"));

        var result = await h.Handler().Handle(
            h.Command(cost: 180m, paid: 180m, on: Backdated), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.PaymentBankedCode, result.Code);
        Assert.Equal(Intervention, note.Payments.Single().PaidOn);
        h.Uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // A voided payment is out of every total already, so re-dating a séance must simply skip it rather than
    // failing on a row that no longer counts.
    [Fact]
    public async Task A_Voided_Payment_Does_Not_Block_The_Re_Dating()
    {
        var h = new Harness();
        var note = h.GiveItANote();
        note.VoidPayment(note.Payments.Single().Id, "Erreur", creditedTotal: 0m);

        var result = await h.Handler().Handle(
            h.Command(cost: 180m, paid: 0m, on: Backdated), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(Intervention, note.Payments.Single().PaidOn);
    }
}

/// <summary>
/// Which refusals a correction can get past — read by the API so the client offers « Corriger » on exactly
/// these and nothing else.
/// </summary>
public class CorrectableRefusalTests
{
    // The two that mean « the fiche and its note disagree », which replacing the note resolves.
    [Theory]
    [InlineData(DentalRecordBillingRefusals.ActsChangedCode)]
    [InlineData(DentalRecordBillingRefusals.PaymentLoweredCode)]
    public void A_Disagreement_With_The_Note_Is_Correctable(string code)
    {
        Assert.True(DentalRecordBillingRefusals.IsCorrectable(code));
    }

    // ⚠️ The exclusions carry the reasoning, and each is a different kind of "no".
    //  - InvoiceNotLive: there is no live note left to replace — the séance is re-billed from « Facturer ».
    //  - PaymentExceedsCost: the séance's own arithmetic, not a disagreement with any document.
    //  - PaymentBanked: a fact about a bank, which no amount of re-issuing changes.
    [Theory]
    [InlineData(DentalRecordBillingRefusals.InvoiceNotLiveCode)]
    [InlineData(DentalRecordBillingRefusals.PaymentExceedsCostCode)]
    [InlineData(DentalRecordBillingRefusals.PaymentBankedCode)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something_else")]
    public void Everything_Else_Is_Not(string? code)
    {
        Assert.False(DentalRecordBillingRefusals.IsCorrectable(code));
    }
}
