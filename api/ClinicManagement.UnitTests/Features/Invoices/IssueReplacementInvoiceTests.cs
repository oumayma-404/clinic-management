using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// Issuing a correction — the destructive half, deliberately deferred to this moment.
///
/// <para>Voiding the predecessor's payments takes real money out of la caisse, so it happens only once the
/// replacement actually exists: void, cancel, re-record, in one transaction. Either the whole correction lands
/// or none of it does. The opening half is <see cref="CorrectInvoiceCommandTests"/>.</para>
/// </summary>
public class IssueReplacementInvoiceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime PaidOn = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private IssueInvoiceCommandHandler CreateHandler() => new(
        _invoices.Object, _clinics.Object, _patients.Object, _plans.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<IssueInvoiceCommandHandler>.Instance);

    /// <summary>
    /// A note BILLED at <paramref name="total"/> and COLLECTED at <paramref name="collected"/> (defaulting to
    /// the same). The two are separate parameters because a correction turns entirely on which of them moved:
    /// over-billed carries across, over-collected is a refund and must be refused.
    /// </summary>
    private static Invoice Original(decimal total, Guid clinicId, decimal? collected = null,
        PaymentMethod method = PaymentMethod.Cash,
        ChequeDetails? cheque = null, ChequeBankedStamp? banked = null)
    {
        var invoice = new Invoice(Guid.NewGuid(), clinicId, PatientId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 2, total / 2m) });
        invoice.Issue("2026-0073");
        invoice.RecordPayment(collected ?? total, method, PaidOn, cheque: cheque, banked: banked);
        return invoice;
    }

    private static Invoice Replacement(Guid originalId, decimal total, string reason = "Erreur de tarif")
    {
        var draft = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        draft.SetLines(new[] { ("Soin de carie / obturation", 1, total) });
        draft.MarkSupersedes(originalId, reason);
        return draft;
    }

    private void Arrange(Invoice replacement, Invoice? original)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _invoices.Setup(r => r.GetByIdAsync(replacement.Id, It.IsAny<CancellationToken>())).ReturnsAsync(replacement);
        if (original is not null)
        {
            _invoices.Setup(r => r.GetByIdAsync(original.Id, It.IsAny<CancellationToken>())).ReturnsAsync(original);
        }
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(73);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync((Patient?)null);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private Task<Result<Application.DTOs.InvoiceDto>> Issue(Invoice replacement) =>
        CreateHandler().Handle(new IssueInvoiceCommand { Id = replacement.Id }, CancellationToken.None);

    // The whole swap, in one pass: the predecessor's money is marked never-received, it is cancelled with the
    // correction's reason, and the replacement carries the money at its ORIGINAL date.
    //
    // ⚠️ Collected (150) fits the corrected total (150), which is what makes this a correction rather than a
    // refund. The fiche path produces exactly this shape: the dentist lowers the tariff AND « Payé » together,
    // so the note was over-BILLED, never over-collected. When the patient really did hand over more, the money
    // has to go back and that is an avoir — pinned by
    // `Collecting_More_Than_The_Correction_Is_Refused_And_Names_The_Avoir` below.
    [Fact]
    public async Task Issuing_A_Replacement_Retires_The_Note_It_Corrects()
    {
        var original = Original(180m, ClinicId, collected: 150m);
        var replacement = Replacement(original.Id, 150m);
        Arrange(replacement, original);

        var result = await Issue(replacement);

        Assert.True(result.IsSuccess);
        Assert.Equal(InvoiceStatus.Cancelled, original.Status);
        Assert.Equal("Erreur de tarif", original.CancellationReason);
        Assert.True(original.Payments.Single().IsVoided);
        Assert.Equal("Erreur de tarif", original.Payments.Single().VoidReason);
        Assert.Equal(0m, original.AmountCollected);
        Assert.Equal(180m, original.TotalTtc);   // the wrong note keeps what it said, cancelled

        Assert.Equal(150m, replacement.AmountCollected);
        Assert.Equal(InvoiceStatus.Paid, replacement.Status);
        Assert.Equal("2026-0074", replacement.Number);
    }

    // ⚠️ Correcting a mistake today must not move yesterday's takings — every money read attributes a payment
    // by PaidOn, so a re-recorded payment stamped "now" would silently rewrite two days of la caisse.
    [Fact]
    public async Task The_Carried_Payment_Keeps_Its_Original_Date()
    {
        var original = Original(150m, ClinicId);
        var replacement = Replacement(original.Id, 150m);
        Arrange(replacement, original);

        await Issue(replacement);

        Assert.Equal(PaidOn, replacement.Payments.Single().PaidOn);
    }

    // Both directions, so neither end of a correction is a dead end.
    [Fact]
    public async Task Both_Notes_Point_At_Each_Other_Afterwards()
    {
        var original = Original(180m, ClinicId, collected: 150m);
        var replacement = Replacement(original.Id, 150m);
        Arrange(replacement, original);

        await Issue(replacement);

        Assert.Equal(replacement.Id, original.SupersededByInvoiceId);
        Assert.Equal(original.Id, replacement.SupersedesInvoiceId);
    }

    // ⚠️ Bounded BEFORE anything is voided. Letting `RecordPayment` throw mid-loop would strand a numbered
    // invoice whose predecessor is already cancelled and whose money is nowhere. And this is the case where the
    // patient really IS owed money back, which is an avoir's job — so the message says so rather than clamping.
    [Fact]
    public async Task Collecting_More_Than_The_Correction_Is_Refused_And_Names_The_Avoir()
    {
        var original = Original(180m, ClinicId);
        var replacement = Replacement(original.Id, 120m);
        Arrange(replacement, original);

        var result = await Issue(replacement);

        Assert.True(result.IsFailure);
        Assert.Contains("avoir", result.Error);
        // Nothing moved: the predecessor is intact and still holds its money.
        Assert.Equal(InvoiceStatus.Paid, original.Status);
        Assert.Equal(180m, original.AmountCollected);
        Assert.False(original.Payments.Single().IsVoided);
    }

    // Issuing the replacement while the note it replaces stays live would leave the patient holding two
    // numbered documents for one séance — the exact duplicate this area guards against.
    [Fact]
    public async Task A_Missing_Predecessor_Refuses_The_Issue()
    {
        var replacement = Replacement(Guid.NewGuid(), 150m);
        Arrange(replacement, null);
        _invoices.Setup(r => r.GetByIdAsync(replacement.SupersedesInvoiceId!.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

        var result = await Issue(replacement);

        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error);
    }

    // Tenant isolation on the predecessor too, not only on the draft being issued.
    [Fact]
    public async Task A_Predecessor_In_Another_Clinic_Refuses_The_Issue()
    {
        var foreign = Original(180m, OtherClinicId);
        var replacement = Replacement(foreign.Id, 150m);
        Arrange(replacement, foreign);

        var result = await Issue(replacement);

        Assert.True(result.IsFailure);
        Assert.Equal(InvoiceStatus.Paid, foreign.Status);
    }

    // Somebody cancelled it between opening the correction and issuing it. Refused rather than proceeding,
    // because the money that was going to be carried across is no longer there to carry.
    [Fact]
    public async Task A_Predecessor_Cancelled_In_The_Meantime_Refuses_The_Issue()
    {
        var original = Original(180m, ClinicId);
        original.VoidPayment(original.Payments.Single().Id, "ailleurs", creditedTotal: 0m);
        original.Cancel("Annulée ailleurs");
        var replacement = Replacement(original.Id, 150m);
        Arrange(replacement, original);

        var result = await Issue(replacement);

        Assert.True(result.IsFailure);
        Assert.Contains("déjà été annulée", result.Error);
    }

    // A cheque left behind would vanish from « chèques à encaisser » entirely, and re-marking a banked one
    // would record today rather than the day it was really deposited.
    [Fact]
    public async Task A_Cheque_Carries_Its_Identity_And_Banked_Mark_Onto_The_Replacement()
    {
        var bankedOn = PaidOn.AddDays(2);
        var original = Original(
            150m, ClinicId, method: PaymentMethod.Cheque,
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", PaidOn.AddDays(15)),
            banked: ChequeBankedStamp.For(PaymentMethod.Cheque, bankedOn, "local|abc", "Dr Bel Hadj"));
        var replacement = Replacement(original.Id, 150m);
        Arrange(replacement, original);

        await Issue(replacement);

        var carried = replacement.Payments.Single();
        Assert.Equal(PaymentMethod.Cheque, carried.Method);
        Assert.Equal("4512873", carried.ChequeNumber);
        Assert.Equal("BIAT", carried.ChequeBankName);
        Assert.Equal(bankedOn, carried.ChequeBankedOn);
    }

    // An ordinary draft — no correction — must be untouched by any of this.
    [Fact]
    public async Task An_Ordinary_Draft_Issues_With_No_Supersede_Behaviour()
    {
        var draft = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        draft.SetLines(new[] { ("Détartrage", 1, 60m) });
        Arrange(draft, null);

        var result = await Issue(draft);

        Assert.True(result.IsSuccess);
        Assert.Null(draft.SupersedesInvoiceId);
        Assert.Equal("2026-0074", draft.Number);
    }
}
