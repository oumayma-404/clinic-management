using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// [AC-13][AC-14][AC-15][AC-16] Voiding a recorded payment — "this was never received". The row is kept and
/// marked, <c>AmountCollected</c> is recomputed from the live payments, and the status walks back.
///
/// <para>
/// A void is a <b>correction</b>, not a refund: money actually returned to the patient is an avoir. The two
/// must never both reduce the same dinar, which is what <see cref="AmountCollected_Cannot_Fall_Below_Avoirs_Already_Issued"/>
/// pins.
/// </para>
/// </summary>
public class InvoicePaymentVoidTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime PaidOn = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);

    /// <summary>An issued invoice for <paramref name="total"/> DT, no VAT, no stamp.</summary>
    private static Invoice IssuedInvoice(decimal total = 300m)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, total) });
        invoice.Issue("2026-0031", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        return invoice;
    }

    // [AC-13] The row survives the void, marked with its motif and actor — the whole point of a trail.
    [Fact]
    public void Voiding_Keeps_The_Row_And_Marks_It()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);
        var payment = invoice.Payments.Single();

        invoice.VoidPayment(payment.Id, "Erreur de saisie", creditedTotal: 0m, "local|abc", "Dr Bel Hadj");

        Assert.Single(invoice.Payments);
        Assert.True(payment.IsVoided);
        Assert.Equal("Erreur de saisie", payment.VoidReason);
        Assert.Equal("local|abc", payment.VoidedByUserId);
        Assert.Equal("Dr Bel Hadj", payment.VoidedByName);
        Assert.NotNull(payment.VoidedAt);
    }

    // [AC-13] Collected is RECOMPUTED from the live rows, not decremented — so it can never drift from them.
    [Fact]
    public void Voiding_Recomputes_Collected_From_The_Live_Payments()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);
        invoice.RecordPayment(50m, PaymentMethod.Card, PaidOn);
        var first = invoice.Payments.First();

        invoice.VoidPayment(first.Id, "Erreur de saisie", creditedTotal: 0m);

        Assert.Equal(50m, invoice.AmountCollected);
        Assert.Equal(50m, invoice.Payments.Where(p => !p.IsVoided).Sum(p => p.Amount));
    }

    // [AC-14] Paid → PartiallyPaid when some money remains.
    [Fact]
    public void Voiding_Walks_A_Paid_Invoice_Back_To_PartiallyPaid()
    {
        var invoice = IssuedInvoice(300m);
        invoice.RecordPayment(200m, PaymentMethod.Cash, PaidOn);
        invoice.RecordPayment(100m, PaymentMethod.Card, PaidOn);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        invoice.VoidPayment(invoice.Payments.First().Id, "Erreur", creditedTotal: 0m);

        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
        Assert.Equal(100m, invoice.AmountCollected);
    }

    // [AC-14] …and all the way back to Issued when nothing is left.
    [Fact]
    public void Voiding_The_Only_Payment_Walks_Back_To_Issued()
    {
        var invoice = IssuedInvoice(300m);
        invoice.RecordPayment(300m, PaymentMethod.Cash, PaidOn);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);

        invoice.VoidPayment(invoice.Payments.Single().Id, "Erreur", creditedTotal: 0m);

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal(0m, invoice.AmountCollected);
        Assert.Equal(300m, invoice.Outstanding);
    }

    // [AC-15] The interaction that would otherwise take the same dinar out of the caisse twice: an avoir has
    // already returned 100 DT on paper, so collected may not drop below that.
    [Fact]
    public void AmountCollected_Cannot_Fall_Below_Avoirs_Already_Issued()
    {
        var invoice = IssuedInvoice(300m);
        invoice.RecordPayment(150m, PaymentMethod.Cash, PaidOn);
        var payment = invoice.Payments.Single();

        var ex = Assert.Throws<InvalidOperationException>(
            () => invoice.VoidPayment(payment.Id, "Erreur", creditedTotal: 100m));

        Assert.Contains("avoirs", ex.Message);
        Assert.False(payment.IsVoided);
        Assert.Equal(150m, invoice.AmountCollected);
    }

    // [AC-15] …but a void that leaves enough collected to cover the avoirs is allowed.
    [Fact]
    public void A_Void_That_Still_Covers_The_Avoirs_Is_Allowed()
    {
        var invoice = IssuedInvoice(300m);
        invoice.RecordPayment(150m, PaymentMethod.Cash, PaidOn);
        invoice.RecordPayment(100m, PaymentMethod.Card, PaidOn);

        invoice.VoidPayment(invoice.Payments.First().Id, "Erreur", creditedTotal: 100m);

        Assert.Equal(100m, invoice.AmountCollected);
    }

    // [AC-16] A second void is refused rather than decrementing twice — the double-click case.
    [Fact]
    public void Voiding_An_Already_Voided_Payment_Is_Refused()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);
        var payment = invoice.Payments.Single();
        invoice.VoidPayment(payment.Id, "Erreur", creditedTotal: 0m);

        var ex = Assert.Throws<InvalidOperationException>(
            () => invoice.VoidPayment(payment.Id, "Encore", creditedTotal: 0m));

        Assert.Contains("déjà annulé", ex.Message);
        Assert.Equal("Erreur", payment.VoidReason);   // the original reason is not rewritten
        Assert.Equal(0m, invoice.AmountCollected);    // and collected did not move twice
    }

    // [AC-13] A motif is mandatory — a reversal without a stated reason is not a correction.
    [Fact]
    public void A_Void_Without_A_Reason_Is_Refused()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);

        Assert.Throws<ArgumentException>(
            () => invoice.VoidPayment(invoice.Payments.Single().Id, "  ", creditedTotal: 0m));
    }

    // [AC-13] An unknown payment id is refused.
    [Fact]
    public void Voiding_An_Unknown_Payment_Is_Refused()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);

        Assert.Throws<InvalidOperationException>(
            () => invoice.VoidPayment(Guid.NewGuid(), "Erreur", creditedTotal: 0m));
    }

    // [Edge case] A fully-voided invoice becomes cancellable again: it was never really paid, so cancelling it
    // is legitimate. Before this, keeping voided rows would have made it PERMANENTLY un-cancellable, because
    // the old guard counted any payment row at all.
    [Fact]
    public void A_Fully_Voided_Invoice_Becomes_Cancellable_Again()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);
        Assert.False(invoice.CanCancel);

        invoice.VoidPayment(invoice.Payments.Single().Id, "Facture émise au mauvais patient", creditedTotal: 0m);

        Assert.True(invoice.CanCancel);
        invoice.Cancel("Émise au mauvais patient");
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    // [Edge case] A live payment still blocks cancellation — that rule is unchanged.
    [Fact]
    public void A_Live_Payment_Still_Blocks_Cancellation()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(100m, PaymentMethod.Cash, PaidOn);
        invoice.RecordPayment(50m, PaymentMethod.Card, PaidOn);
        invoice.VoidPayment(invoice.Payments.First().Id, "Erreur", creditedTotal: 0m);

        Assert.False(invoice.CanCancel);
        var ex = Assert.Throws<InvalidOperationException>(() => invoice.Cancel("Test"));
        Assert.Contains("avoir", ex.Message);
    }

    // [AC-29] A sub-millime amount is refused rather than silently stored as 0,000 — a zero-amount row would
    // count for nothing yet block cancellation forever.
    [Fact]
    public void A_Sub_Millime_Payment_Is_Refused()
    {
        var invoice = IssuedInvoice();

        var ex = Assert.Throws<ArgumentException>(
            () => invoice.RecordPayment(0.0004m, PaymentMethod.Cash, PaidOn));

        Assert.Contains("millime", ex.Message);
        Assert.Empty(invoice.Payments);
    }

    // [AC-29] Amounts round to the millime on the way in.
    [Fact]
    public void A_Payment_Amount_Is_Rounded_To_The_Millime()
    {
        var invoice = IssuedInvoice();

        invoice.RecordPayment(10.0006m, PaymentMethod.Cash, PaidOn);

        Assert.Equal(10.001m, invoice.Payments.Single().Amount);
        Assert.Equal(10.001m, invoice.AmountCollected);
    }

    // [AC-13] A cancelled invoice's payments are frozen.
    [Fact]
    public void Payments_On_A_Cancelled_Invoice_Cannot_Be_Voided()
    {
        var invoice = IssuedInvoice();
        invoice.Cancel("Erreur");

        Assert.Throws<InvalidOperationException>(
            () => invoice.VoidPayment(Guid.NewGuid(), "Erreur", creditedTotal: 0m));
    }
}
