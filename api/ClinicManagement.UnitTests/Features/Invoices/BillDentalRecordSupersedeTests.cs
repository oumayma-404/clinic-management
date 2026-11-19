using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// The fiche's correction re-bills through <c>BillDentalRecordCommand</c>, and this is the half that writes the
/// trail: the replacement names the note it corrects, and that note is pointed forward at it.
///
/// <para><b>Why this class exists at all.</b> <c>DentalRecordCorrectionTests</c> mocks <c>ISender</c> — it can
/// prove the command was sent with the right shape, and nothing about what the handler behind it does. A probe
/// deleting the linking outright left that class <b>green</b>, so the whole of this behaviour was uncovered by
/// construction. A test that mocks the seam cannot cover the far side of it.</para>
/// </summary>
public class BillDentalRecordSupersedeTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime InterventionDate = new(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private Invoice? _saved;
    private readonly List<Invoice> _updated = new();

    private readonly Mock<ITreatmentPlanRepository> _plans = new();

    private BillDentalRecordCommandHandler CreateHandler() => new(
        _invoices.Object, _records.Object, _patients.Object, _clinics.Object, _creditNotes.Object, _plans.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<BillDentalRecordCommandHandler>.Instance);

    private static DentalRecord RecordFixture()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Soin de carie / obturation", 150m, 75m, true, new[] { 26, 27 }, null, null, null),
        });
        return record;
    }

    /// <summary>The note the correction retires — already voided and cancelled by the update command.</summary>
    private Invoice RetiredNote(Guid recordId, Guid clinicId)
    {
        var invoice = new Invoice(Guid.NewGuid(), clinicId, PatientId, dentalRecordId: recordId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 2, 90m) });
        invoice.Issue("2026-0073");
        invoice.RecordPayment(180m, PaymentMethod.Cash, InterventionDate);
        invoice.VoidPayment(invoice.Payments.Single().Id, "Erreur de tarif", creditedTotal: 0m);
        invoice.Cancel("Erreur de tarif");
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return invoice;
    }

    /// <summary>
    /// A retired note that held the chair's cash and the desk's cheque — both voided by the correction
    /// (`seance-au-fauteuil`).
    /// </summary>
    private (Invoice Note, Payment Cash, Payment Cheque) RetiredNoteWithDeskCheque(Guid recordId)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: recordId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 2, 90m) });
        invoice.Issue("2026-0073");
        invoice.RecordPayment(100m, PaymentMethod.Cash, InterventionDate);
        invoice.RecordPayment(
            50m, PaymentMethod.Cheque, InterventionDate.AddDays(1),
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", InterventionDate.AddDays(20)));
        var cash = invoice.Payments.Single(p => p.Method == PaymentMethod.Cash);
        var cheque = invoice.Payments.Single(p => p.Method == PaymentMethod.Cheque);
        foreach (var payment in invoice.Payments.ToList())
        {
            invoice.VoidPayment(payment.Id, "Erreur de tarif", creditedTotal: 0m);
        }
        invoice.Cancel("Erreur de tarif");
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        return (invoice, cash, cheque);
    }

    private Task<Result<DentalRecordBillingResult>> BillCorrection(
        DentalRecord record, Guid supersedes, decimal paid, params CarriedPayment[] carried) =>
        CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                IsAutomatic = false,
                SupersedesInvoiceId = supersedes,
                PaidNow = new DentalRecordPaymentRequest { Amount = paid, Method = "Cash" },
                CarriedPayments = carried,
            },
            CancellationToken.None);

    // The desk's cheque lands on the replacement as the same cheque — number, bank, the day it was handed in — and
    // the chair's cash keeps its own day. La caisse nets to zero: nothing new reached the till.
    [Fact]
    public async Task The_Replacement_Re_Records_The_Carried_Payments_As_They_Were()
    {
        var record = RecordFixture();
        Arrange(record);
        var (retired, cash, cheque) = RetiredNoteWithDeskCheque(record.Id);

        var result = await BillCorrection(
            record, retired.Id, 150m, new CarriedPayment(cash.Id, 100m), new CarriedPayment(cheque.Id, 50m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _saved!.Payments.Count);
        var carriedCheque = Assert.Single(_saved.Payments, p => p.Method == PaymentMethod.Cheque);
        Assert.Equal(50m, carriedCheque.Amount);
        Assert.Equal(InterventionDate.AddDays(1), carriedCheque.PaidOn);
        Assert.Equal("4512873", carriedCheque.ChequeNumber);
        Assert.Equal("BIAT", carriedCheque.ChequeBankName);
        var carriedCash = Assert.Single(_saved.Payments, p => p.Method == PaymentMethod.Cash);
        Assert.Equal(100m, carriedCash.Amount);
        Assert.Equal(InterventionDate, carriedCash.PaidOn);
        Assert.Equal(150m, _saved.AmountCollected);
        Assert.Equal(0m, result.Value!.AmountCollected);
        Assert.Contains("reportés", result.Value.Message);
    }

    // What the corrected « Payé » holds beyond the carried rows is new money taken at the chair, recorded once.
    [Fact]
    public async Task Money_Beyond_The_Carry_Is_Recorded_Once_As_New()
    {
        var record = RecordFixture();
        Arrange(record);
        var (retired, cash, _) = RetiredNoteWithDeskCheque(record.Id);

        var result = await BillCorrection(record, retired.Id, 150m, new CarriedPayment(cash.Id, 100m));

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(2, _saved!.Payments.Count);
        Assert.Equal(150m, _saved.AmountCollected);
        Assert.Equal(50m, result.Value!.AmountCollected);
    }

    // An id the retired note does not hold is ignored, never trusted: the carry is read off that note.
    [Fact]
    public async Task A_Carried_Id_The_Retired_Note_Does_Not_Hold_Is_Ignored()
    {
        var record = RecordFixture();
        Arrange(record);
        var (retired, _, _) = RetiredNoteWithDeskCheque(record.Id);

        var result = await BillCorrection(record, retired.Id, 150m, new CarriedPayment(Guid.NewGuid(), 100m));

        Assert.True(result.IsSuccess, result.Error);
        var payment = Assert.Single(_saved!.Payments);
        Assert.Equal(150m, payment.Amount);
    }

    private void Arrange(DentalRecord record)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, ClinicId, "Leila", "Gharbi", new DateTime(1985, 3, 2), "Femme"));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        // ⚠️ The retired note is deliberately ABSENT from the links: it is cancelled, so the already-billed
        // branch must not find it. That is the A-1 shape the correction depends on.
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(73);
        _invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => _saved = i)
            .ReturnsAsync((Invoice i, CancellationToken _) => i);
        _invoices.Setup(r => r.UpdateAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => _updated.Add(i))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Task<Result<DentalRecordBillingResult>> Bill(DentalRecord record, Guid? supersedes) =>
        CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                IsAutomatic = supersedes is null,
                SupersedesInvoiceId = supersedes,
                PaidNow = new DentalRecordPaymentRequest { Amount = 150m, Method = "Cash" },
            },
            CancellationToken.None);

    // The trail, written on both sides. « Qu'est-ce qui a remplacé celle-ci ? » is the first question anyone
    // asks of a cancelled note, and without the forward link it has no answer.
    [Fact]
    public async Task Re_Billing_A_Correction_Links_Both_Notes()
    {
        var record = RecordFixture();
        Arrange(record);
        var retired = RetiredNote(record.Id, ClinicId);

        var result = await Bill(record, retired.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(retired.Id, _saved!.SupersedesInvoiceId);
        Assert.Equal(_saved.Id, retired.SupersededByInvoiceId);
        Assert.Contains(retired, _updated);
    }

    // An ordinary automatic billing carries none of it.
    [Fact]
    public async Task An_Ordinary_Billing_Links_Nothing()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await Bill(record, supersedes: null);

        Assert.True(result.IsSuccess);
        Assert.Null(_saved!.SupersedesInvoiceId);
        // Not `Assert.Empty`: the ordinary path legitimately updates the note it is issuing. What must not
        // happen is a write to any OTHER note.
        Assert.DoesNotContain(_updated, i => i.Id != _saved.Id);
    }

    // ⚠️ Passed EXPLICITLY rather than inferred from « the record has a cancelled note »: an old, unrelated
    // cancellation would match that guess, and the two notes would be wired together as a correction that never
    // happened. This is the case that guess gets wrong.
    [Fact]
    public async Task An_Unrelated_Old_Cancellation_Is_Not_Wired_Up_As_A_Correction()
    {
        var record = RecordFixture();
        Arrange(record);
        var unrelated = RetiredNote(record.Id, ClinicId);

        await Bill(record, supersedes: null);

        Assert.Null(_saved!.SupersedesInvoiceId);
        Assert.Null(unrelated.SupersededByInvoiceId);
    }

    // Tenant isolation on the link too — a note in another clinic must not be reachable through it.
    [Fact]
    public async Task A_Predecessor_In_Another_Clinic_Is_Not_Linked()
    {
        var record = RecordFixture();
        Arrange(record);
        var foreign = RetiredNote(record.Id, OtherClinicId);

        var result = await Bill(record, foreign.Id);

        // The replacement still names what it was TOLD it replaces — the request said so — but nothing was
        // written to a note this clinic does not own.
        Assert.True(result.IsSuccess);
        Assert.Null(foreign.SupersededByInvoiceId);
        Assert.DoesNotContain(foreign, _updated);
    }

    // Already pointed at a replacement: leave it alone rather than repointing it at a second one, which would
    // make the trail branch and lose the first correction.
    [Fact]
    public async Task An_Already_Superseded_Predecessor_Is_Not_Repointed()
    {
        var record = RecordFixture();
        Arrange(record);
        var retired = RetiredNote(record.Id, ClinicId);
        var firstReplacement = Guid.NewGuid();
        retired.MarkSupersededBy(firstReplacement);

        await Bill(record, retired.Id);

        Assert.Equal(firstReplacement, retired.SupersededByInvoiceId);
    }
}
