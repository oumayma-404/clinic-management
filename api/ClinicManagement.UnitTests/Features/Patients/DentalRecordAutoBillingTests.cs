using MediatR;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Saving a fiche de soins with a « Montant payé » puts that money in la caisse.
///
/// <para><b>The reported bug.</b> A dentist recorded four sessions worth 1 280 DT, each showing
/// « Montant payé 400,000 · Reste à payer 0 », and la caisse showed nothing: zero invoices, zero payment rows.
/// <c>DentalRecord.AmountPaid</c> was read by nothing but the fiche's own display — and the form *pre-fills it
/// with the running total*, so the field filled itself in with the full amount and meant nothing.</para>
///
/// <para><b>What these tests protect.</b> Two things, in order of how much damage they prevent:
/// <list type="number">
///   <item>A re-saved fiche must not raise a <b>second</b> note d'honoraires. A fiche is re-saved routinely —
///   a corrected note, one more tooth — and invoice numbers are gapless and irreversible.</item>
///   <item>A billing failure must be <b>reported</b>, never swallowed. The record is already committed, so
///   swallowing would put the dentist right back where they started: believing money landed when it did not.</item>
/// </list></para>
/// </summary>
public class DentalRecordAutoBillingTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime InterventionDate = new(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ISender> _sender = new();

    private static DentalRecord RecordFixture()
    {
        var record = new DentalRecord(Guid.NewGuid(), PatientId, ClinicId, InterventionDate, 0m, true);
        record.SetActs(new[]
        {
            new DentalRecordActInput(null, "Détartrage", 400m, null, false, Array.Empty<int>(), null, null, null),
        });
        return record;
    }

    private Task<DentalRecordBillingDto> BillAsync(DentalRecord record, decimal amountPaid) =>
        DentalRecordAutoBilling.BillIfPaidAsync(
            _sender.Object, record, amountPaid, NullLogger.Instance, CancellationToken.None);

    /// <summary>
    /// A successful billing outcome. The command returns a <b>typed</b> result now — outcome, note and what this
    /// call actually collected — which is what let the helper stop recovering « déjà facturée » by matching a
    /// French substring against the error message.
    /// </summary>
    private static Result<DentalRecordBillingResult> Billed(
        DentalRecordBillingOutcome outcome, string number, decimal collected = 0m, string? message = null) =>
        Result<DentalRecordBillingResult>.Success(new DentalRecordBillingResult
        {
            Outcome = outcome,
            Invoice = new InvoiceDto { Id = Guid.NewGuid(), Number = number, AmountCollected = collected },
            AmountCollected = collected,
            Message = message,
        });

    private void SenderReturns(Result<DentalRecordBillingResult> result) =>
        _sender.Setup(s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    [Fact]
    public async Task A_Paid_Fiche_Is_Billed_And_The_Note_Is_Reported()
    {
        var record = RecordFixture();
        SenderReturns(Billed(DentalRecordBillingOutcome.Billed, "2026-0007", 400m));

        var billing = await BillAsync(record, 400m);

        Assert.Equal(nameof(DentalRecordBillingOutcome.Billed), billing.Outcome);
        Assert.Equal("2026-0007", billing.InvoiceNumber);
        Assert.Equal(400m, billing.AmountCollected);
    }

    [Fact]
    public async Task The_Payment_Is_Dated_To_The_Session_Not_Today()
    {
        var record = RecordFixture();
        BillDentalRecordCommand? sent = null;
        _sender.Setup(s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<Result<DentalRecordBillingResult>> c, CancellationToken _) =>
                sent = (BillDentalRecordCommand)c)
            .ReturnsAsync(Billed(DentalRecordBillingOutcome.Billed, "2026-0008"));

        await BillAsync(record, 400m);

        // The user's own case: a fiche entered for a past appointment. Booking that cash to "today" would put it
        // in the wrong day's caisse — and on the 1st, the wrong month's revenue.
        Assert.Equal(InterventionDate, sent!.PaidNow!.PaidOn);
        Assert.Equal(record.Id, sent.DentalRecordId);
    }

    [Fact]
    public async Task A_Fiche_With_No_Payment_Bills_Nothing_At_All()
    {
        var record = RecordFixture();

        var billing = await BillAsync(record, 0m);

        // Not an error: « le patient paiera plus tard » is a legitimate outcome, and this is the behaviour that
        // existed before auto-billing. Billing on a zero would consume a number for no money.
        Assert.Equal(nameof(DentalRecordBillingOutcome.NotCollected), billing.Outcome);
        _sender.Verify(
            s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Re_Saving_A_Billed_Fiche_Does_Not_Raise_A_Second_Note() // the load-bearing case
    {
        var record = RecordFixture();
        SenderReturns(Billed(
            DentalRecordBillingOutcome.AlreadyBilled, "2026-0007",
            message: "Cette fiche de soins est déjà facturée sur la note n° 2026-0007."));

        var billing = await BillAsync(record, 400m);

        // Reported as its own outcome, distinct from Failed: the money IS in the till, so the UI says so quietly
        // rather than raising an alarm on every edit of an already-billed session. ⚠️ It is a *success* now — the
        // command's already-billed branch decides between topping the note up and having nothing to add, and only
        // the second is this outcome. A flat refusal was how the extra cash used to be lost.
        Assert.Equal(nameof(DentalRecordBillingOutcome.AlreadyBilled), billing.Outcome);
        Assert.Contains("2026-0007", billing.Message);
    }

    [Fact]
    public async Task Raising_The_Amount_On_A_Billed_Fiche_Tops_The_Same_Note_Up()
    {
        var record = RecordFixture();
        // 200,000 already collected on note 2026-0007; the fiche is re-saved at 400,000, so 200,000 more reaches
        // the till on the SAME document (AC-1). What is reported is the increment, not the note's new total.
        SenderReturns(Billed(DentalRecordBillingOutcome.ToppedUp, "2026-0007", 200m));

        var billing = await BillAsync(record, 400m);

        Assert.Equal(nameof(DentalRecordBillingOutcome.ToppedUp), billing.Outcome);
        Assert.Equal("2026-0007", billing.InvoiceNumber);
        Assert.Equal(200m, billing.AmountCollected);
    }

    [Fact]
    public async Task A_Refusal_Is_Told_Apart_From_A_Failure_By_Its_Code()
    {
        var record = RecordFixture();
        SenderReturns(Result<DentalRecordBillingResult>.Failure(
            "Cette fiche est facturée sur la note n° 2026-0007, qui a déjà encaissé 400,000 DT. …",
            ClinicManagement.Application.Features.Invoices.DentalRecordBillingRefusals.PaymentLoweredCode));

        var billing = await BillAsync(record, 100m);

        // A rule said no and the user has a defined next step (an avoir), which is not the same event as the
        // billing having gone wrong. Told apart by the code — never by the sentence, which is exactly the match
        // this part deleted.
        Assert.Equal(nameof(DentalRecordBillingOutcome.Refused), billing.Outcome);
        Assert.Contains("2026-0007", billing.Message);
    }

    [Fact]
    public async Task A_Refused_Billing_Is_Reported_As_Failed_With_Its_Reason()
    {
        var record = RecordFixture();
        SenderReturns(Result<DentalRecordBillingResult>.Failure(
            "Le montant encaissé dépasse le total de la note d'honoraires."));

        var billing = await BillAsync(record, 5000m);

        Assert.Equal(nameof(DentalRecordBillingOutcome.Failed), billing.Outcome);
        Assert.Contains("dépasse le total", billing.Message);
    }

    [Fact]
    public async Task A_Thrown_Billing_Is_Reported_And_Never_Propagates()
    {
        var record = RecordFixture();
        _sender.Setup(s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("connexion perdue"));

        // The clinical record is already committed when this runs, so a throw must not surface as a failed save —
        // that would lose the dentist's work over a money problem. But it must not vanish either.
        var billing = await BillAsync(record, 400m);

        Assert.Equal(nameof(DentalRecordBillingOutcome.Failed), billing.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(billing.Message));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-400)]
    public async Task A_Negative_Amount_Bills_Nothing(decimal amountPaid)
    {
        var billing = await BillAsync(RecordFixture(), amountPaid);

        Assert.Equal(nameof(DentalRecordBillingOutcome.NotCollected), billing.Outcome);
        _sender.Verify(
            s => s.Send(It.IsAny<BillDentalRecordCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
