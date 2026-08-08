using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// Billing a fiche de soins — the session's payment finally reaching the till.
///
/// <para><b>The defect this closes.</b> <c>DentalRecord.AmountPaid</c> was read by nothing but the fiche's own
/// display: a dentist could type an amount there, see it on screen, and it would never appear in la caisse, on the
/// dashboard, or in the patient's balance. Cash exists in exactly two ledgers — invoice <c>Payment</c> and devis
/// <c>InstallmentPayment</c> — so the fix is to let a fiche produce a real payment on a real numbered document,
/// not to teach a fourth read about a fourth source.</para>
///
/// <para><b>What the tests are watching for.</b> Issuing consumes a gapless number, so every refusal must happen
/// <i>before</i> that — a typo in an amount or a date must not leave a numbered, unpaid note behind. And the
/// pricing rule must be the server's only copy: it used to live in the patient page's browser code.</para>
/// </summary>
public class InvoiceFromDentalRecordTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime InterventionDate = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<ICreditNoteRepository> _creditNotes = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private static Patient NewPatient(Guid clinicId) =>
        new(PatientId, clinicId, "Amal", "Ben Salah", new DateTime(1990, 4, 3), "Femme");

    /// <summary>
    /// The session's TTC: 60 (flat exam) + 90 × 3 (per-tooth composite) = 330 HT, plus <b>TVA</b> and the
    /// <b>timbre fiscal</b>. A new <c>Clinic</c> now applies VAT at 7 % (J11 — dental acts are taxable at the
    /// reduced rate, Tableau « B » nouveau § II n° 1) and enables stamp duty at 1,000 DT, so the note settles at
    /// 330,000 + 23,100 + 1,000 = <b>354,100</b> — worth spelling out, because a fixture quietly assuming 330
    /// would have read as a pricing bug.
    /// </summary>
    private const decimal SessionTtc = 354.100m;

    /// <summary>A two-act session: a flat 60 DT exam plus a per-tooth composite at 90 DT × 3 teeth.</summary>
    private static DentalRecord RecordFixture()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Consultation", 60m, null, false, Array.Empty<int>(), null, null, null),
            new DentalRecordActInput(
                null, "Composite", 270m, 90m, true, new[] { 16, 26, 36 }, null, null, null),
        });
        return record;
    }

    private BillDentalRecordCommandHandler CreateHandler() => new(
        _invoices.Object, _records.Object, _patients.Object, _clinics.Object, _creditNotes.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<BillDentalRecordCommandHandler>.Instance);

    private Invoice? _saved;

    private void Arrange(DentalRecord record, Guid? patientClinicId = null)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NewPatient(patientClinicId ?? ClinicId));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _invoices.Setup(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()))
            .Callback((Invoice i, CancellationToken _) => _saved = i)
            .ReturnsAsync((Invoice i, CancellationToken _) => i);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    // ---- The whole point --------------------------------------------------------

    [Fact]
    public async Task Billing_With_Payment_Issues_The_Note_And_Records_The_Cash()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = SessionTtc, Method = "Cash" },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Issued and numbered — a payment cannot exist on a draft, so this is not optional.
        Assert.NotNull(_saved!.Number);
        Assert.Equal(SessionTtc, _saved.AmountCollected);
        Assert.Equal(record.Id, _saved.DentalRecordId);

        // 330 HT plus the clinic's TVA and timbre fiscal, and a full settlement lands on Paid, not PartiallyPaid.
        // HT is asserted separately because it is the figure the *acts* determine — it must not move when the
        // clinic's tax posture does.
        Assert.Equal(SessionTtc, _saved.TotalTtc);
        Assert.Equal(330m, _saved.TotalHt);
        Assert.Equal(InvoiceStatus.Paid, _saved.Status);

        // And it is a real Payment row — the thing la caisse and the dashboard actually read.
        var payment = Assert.Single(_saved.Payments);
        Assert.Equal(PaymentMethod.Cash, payment.Method);
        Assert.False(payment.IsVoided);
    }

    [Fact]
    public async Task The_Payment_Defaults_To_The_Session_Date_Not_Today()
    {
        var record = RecordFixture();
        Arrange(record);

        await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = 100m, Method = "Card" },
            },
            CancellationToken.None);

        // A fiche recorded two days late was paid on the day it happened. Booking that cash to "now" would put it
        // in the wrong day's caisse — and on the 1st, the wrong month's revenue.
        Assert.Equal(InterventionDate, Assert.Single(_saved!.Payments).PaidOn);
    }

    [Fact]
    public async Task Billing_Without_Payment_Issues_The_Note_And_Collects_Nothing()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(_saved!.Number);
        Assert.Empty(_saved.Payments);
        Assert.Equal(InvoiceStatus.Issued, _saved.Status);
        // « Le patient paiera plus tard » — the balance is real debt and « Créances » must see it.
        Assert.Equal(SessionTtc, _saved.Outstanding);
    }

    [Fact]
    public async Task The_Whole_Chain_Is_One_Transaction()
    {
        var record = RecordFixture();
        Arrange(record);

        await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = SessionTtc, Method = "Cash" },
            },
            CancellationToken.None);

        // Create → issue → pay committed once. Composing the three existing commands would leave a half-issued,
        // numbered invoice with no payment reachable on any failure between them.
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failed_Save_Rolls_Back_And_Commits_Nothing()
    {
        var record = RecordFixture();
        Arrange(record);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connexion perdue"));

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = SessionTtc, Method = "Cash" },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Every refusal happens before a number is consumed ----------------------

    [Theory]
    [InlineData(0, "Cash")]
    [InlineData(-50, "Cash")]
    public async Task A_Non_Positive_Amount_Is_Refused_Before_Any_Invoice_Exists(int amount, string method)
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = amount, Method = method },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task An_Unknown_Payment_Method_Is_Refused_Before_Any_Invoice_Exists()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = 100m, Method = "Bitcoin" },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Mode de paiement invalide.", result.Error);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task A_Future_Payment_Date_Is_Refused_Before_Any_Invoice_Exists()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest
                {
                    Amount = 100m,
                    Method = "Cash",
                    PaidOn = DateTime.UtcNow.AddDays(3),
                },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task An_Over_Payment_Is_Refused_Before_Any_Invoice_Exists()
    {
        var record = RecordFixture();
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                // The session settles at 354,100 DT (330 HT + 7 % TVA + timbre), so 500 over-pays it.
                PaidNow = new DentalRecordPaymentRequest { Amount = 500m, Method = "Cash" },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("dépasse le total", result.Error);
        // Checked against the TTC the invoice *will* freeze, not after Issue() — otherwise a fat-fingered amount
        // burns a number and leaves an unpaid note behind forever.
        AssertNothingWasIssued();
    }

    /// <summary>No transaction, no number, nothing persisted — the state a pre-issue refusal must leave behind.</summary>
    private void AssertNothingWasIssued()
    {
        Assert.Null(_saved);
        _invoices.Verify(r => r.AddAsync(It.IsAny<Invoice>(), It.IsAny<CancellationToken>()), Times.Never);
        _invoices.Verify(
            r => r.GetMaxSequenceForYearAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Guards ----------------------------------------------------------------

    [Fact]
    public async Task A_Fiche_From_Another_Clinic_Reads_As_Not_Found()
    {
        var record = RecordFixture();
        Arrange(record, patientClinicId: OtherClinicId);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id }, CancellationToken.None);

        // A DentalRecord has no ClinicId — the check goes through its patient — and a cross-clinic id must not
        // disclose that the fiche exists somewhere else.
        Assert.True(result.IsFailure);
        Assert.Equal("Fiche de soins introuvable.", result.Error);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task An_Already_Billed_Fiche_With_Nothing_To_Add_Names_Its_Note_And_Raises_No_Second_One()
    {
        var record = RecordFixture();
        Arrange(record);
        ArrangeExistingNote(record, "2026-0042", collected: 0m);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id }, CancellationToken.None);

        // ⚠️ A *success* now, not a refusal. « Déjà facturée » is the expected outcome of re-saving a fiche, and
        // modelling it as an error is what made the whole thing recoverable only by matching a French substring.
        Assert.True(result.IsSuccess);
        Assert.Equal(DentalRecordBillingOutcome.AlreadyBilled, result.Value!.Outcome);
        // Naming the number is the difference between a usable message and sending the user hunting /factures.
        Assert.Contains("2026-0042", result.Value.Message);
        Assert.Equal(0m, result.Value.AmountCollected);
        AssertNothingWasIssued();
    }

    /// <summary>
    /// A live note billing <paramref name="record"/>, priced exactly as the fiche is — which is what the acts-changed
    /// guard compares against — and holding <paramref name="collected"/>.
    /// </summary>
    private Invoice ArrangeExistingNote(DentalRecord record, string number, decimal collected)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId: record.Id);
        invoice.SetLines(DentalRecordInvoiceLines.For(record)
            .Select(l => (l.Designation, l.Quantity, l.UnitPriceHt, (Guid?)record.Id, (Guid?)null, (string?)null)));
        invoice.Issue(number, vatApplicable: true, vatRate: 7m, stampDutyEnabled: true, stampDutyAmount: 1m);
        if (collected > 0m)
        {
            invoice.RecordPayment(collected, PaymentMethod.Cash, InterventionDate);
        }

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (record.Id, invoice.Id, number, invoice.Status),
            });
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _creditNotes.Setup(r => r.GetTotalForInvoiceAsync(invoice.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);

        return invoice;
    }

    [Fact]
    public async Task Raising_The_Amount_Tops_The_SAME_Note_Up_Rather_Than_Raising_A_Second() // AC-1
    {
        var record = RecordFixture();
        Arrange(record);
        var invoice = ArrangeExistingNote(record, "2026-0042", collected: 200m);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                // The fiche's « Montant payé » is CUMULATIVE — the séance is now fully settled — so the increment
                // is arithmetic the command does, not something the user is asked to work out.
                PaidNow = new DentalRecordPaymentRequest { Amount = SessionTtc, Method = "Cash" },
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DentalRecordBillingOutcome.ToppedUp, result.Value!.Outcome);
        Assert.Equal(SessionTtc - 200m, result.Value.AmountCollected);
        Assert.Equal(SessionTtc, invoice.AmountCollected);
        Assert.Equal(2, invoice.Payments.Count);
        // One document for one séance: no number was consumed and no transaction opened.
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task Lowering_The_Amount_Is_Refused_And_Names_The_Avoir() // AC-2
    {
        var record = RecordFixture();
        Arrange(record);
        ArrangeExistingNote(record, "2026-0042", collected: 200m);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                PaidNow = new DentalRecordPaymentRequest { Amount = 50m, Method = "Cash" },
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.PaymentLoweredCode, result.Code);
        Assert.Contains("avoir", result.Error);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task Changing_The_Acts_Of_A_Billed_Fiche_Is_Refused() // AC-3b
    {
        var record = RecordFixture();
        Arrange(record);
        ArrangeExistingNote(record, "2026-0042", collected: 0m);

        // The note's lines were frozen at issue, so a fiche whose acts moved would stop describing what was
        // billed. Compared on the money — `SetActs` regenerates every act id, so there is no identity to diff.
        record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Consultation", 60m, null, false, Array.Empty<int>(), null, null, null),
        });

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.ActsChangedCode, result.Code);
        AssertNothingWasIssued();
    }

    [Fact]
    public async Task A_Fiche_Whose_Only_Invoice_Was_Cancelled_Can_Be_Billed_Again_MANUALLY()
    {
        var record = RecordFixture();
        Arrange(record);
        var cancelled = ArrangeExistingNote(record, "2026-0009", collected: 0m);
        cancelled.Cancel("erreur de saisie");
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (record.Id, cancelled.Id, "2026-0009", InvoiceStatus.Cancelled),
            });

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id }, CancellationToken.None);

        // A cancelled note no longer represents the work — the same rule the money reads de-duplicate by — and
        // pressing « Facturer cette intervention » is the deliberate act of re-billing it.
        Assert.True(result.IsSuccess);
        Assert.Equal(DentalRecordBillingOutcome.Billed, result.Value!.Outcome);
    }

    [Fact]
    public async Task But_A_Re_Save_Will_Not_Silently_Raise_A_Second_Note_Over_A_Cancelled_One() // A-1
    {
        var record = RecordFixture();
        Arrange(record);
        var cancelled = ArrangeExistingNote(record, "2026-0009", collected: 0m);
        cancelled.Cancel("erreur de saisie");
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid, Guid, string?, InvoiceStatus)>
            {
                (record.Id, cancelled.Id, "2026-0009", InvoiceStatus.Cancelled),
            });

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand
            {
                DentalRecordId = record.Id,
                IsAutomatic = true,
                PaidNow = new DentalRecordPaymentRequest { Amount = SessionTtc, Method = "Cash" },
            },
            CancellationToken.None);

        // The acceptance criterion is « never *silently* create a second document », and saving the fiche is the
        // silent path. It names the note so the user can decide, and consumes no number.
        Assert.True(result.IsFailure);
        Assert.Equal(DentalRecordBillingRefusals.InvoiceNotLiveCode, result.Code);
        Assert.Contains("2026-0009", result.Error);
        AssertNothingWasIssued();
    }

    // ---- The pricing rule, now the server's only copy --------------------------

    [Fact]
    public void A_Per_Tooth_Act_Bills_As_Quantity_Times_Unit_Price()
    {
        var lines = DentalRecordInvoiceLines.For(RecordFixture());

        Assert.Equal(2, lines.Count);

        var flat = lines.Single(l => l.Designation == "Consultation");
        Assert.Equal(1, flat.Quantity);
        Assert.Equal(60m, flat.UnitPriceHt);

        // 3 × 90, not 1 × 270: a patient reading « Composite (dents 16, 26, 36) … 270,000 DT » cannot check the
        // arithmetic, and the teeth belong on the line so they can see what was treated.
        var perTooth = lines.Single(l => l.Designation.StartsWith("Composite"));
        Assert.Equal("Composite (dents 16, 26, 36)", perTooth.Designation);
        Assert.Equal(3, perTooth.Quantity);
        Assert.Equal(90m, perTooth.UnitPriceHt);
    }

    [Fact]
    public void A_Per_Tooth_Act_With_No_Captured_Unit_Price_Stays_One_Line()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);
        record.SetActs(new[]
        {
            // `UnitCost` is nullable precisely because acts recorded before per-tooth pricing never captured one.
            new DentalRecordActInput(null, "Composite", 270m, null, true, new[] { 16, 26, 36 }, null, null, null),
        });

        var line = Assert.Single(DentalRecordInvoiceLines.For(record));

        Assert.Equal(1, line.Quantity);
        Assert.Equal(270m, line.UnitPriceHt);
    }

    [Fact]
    public void A_Legacy_Fiche_With_No_Acts_Bills_Its_Own_Derived_Cost()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);

        var line = Assert.Single(DentalRecordInvoiceLines.For(record));

        // Returning nothing would produce an empty invoice with a number attached to it.
        Assert.Equal(1, line.Quantity);
        Assert.Equal(record.Cost, line.UnitPriceHt);
    }

    [Fact]
    public async Task A_Fiche_With_Nothing_Billable_Is_Refused()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);
        Arrange(record);

        var result = await CreateHandler().Handle(
            new BillDentalRecordCommand { DentalRecordId = record.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("aucun acte facturable", result.Error);
        AssertNothingWasIssued();
    }
}
