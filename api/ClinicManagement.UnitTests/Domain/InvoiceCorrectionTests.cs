using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Correcting a note d'honoraires — replacing a note that was <b>wrong</b>, as opposed to an avoir, which
/// records money handed <i>back to the patient</i>.
///
/// <para>The distinction is the whole point of this area. A mis-keyed amount gave nothing back, so an avoir
/// there states a refund that never happened — which is what every refusal in the fiche used to send the
/// dentist off to do. Correcting marks the payments never-received, cancels the note and raises the right one;
/// the number is spent and marked cancelled, so the sequence stays gapless.</para>
///
/// <para>The mechanics are the aggregate's existing ones (<see cref="Invoice.VoidPayment"/> then
/// <see cref="Invoice.Cancel"/>, in that order — see <see cref="Correction_Order_Is_Void_Then_Cancel"/>);
/// what is new here is <see cref="Invoice.CanBeCorrected"/>, the supersede pair, and
/// <see cref="Invoice.AmendPaymentDate"/>.</para>
/// </summary>
public class InvoiceCorrectionTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime PaidOn = new(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);

    private static Invoice IssuedInvoice(decimal total = 180m, string number = "2026-0073")
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Soin de carie / obturation", 2, total / 2m) });
        invoice.Issue(number);
        return invoice;
    }

    private static Invoice PaidInvoice(decimal total = 180m)
    {
        var invoice = IssuedInvoice(total);
        invoice.RecordPayment(total, PaymentMethod.Cash, PaidOn);
        return invoice;
    }

    // ── CanBeCorrected ────────────────────────────────────────────────────────────────────────────────

    // An ordinary paid note is the case this exists for.
    [Fact]
    public void A_Paid_Note_Can_Be_Corrected()
    {
        Assert.True(PaidInvoice().CanBeCorrected);
    }

    // A draft is already editable in place; routing it through a correction would spend a number for nothing.
    [Fact]
    public void A_Draft_Cannot_Be_Corrected()
    {
        var draft = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        draft.SetLines(new[] { ("Détartrage", 1, 60m) });

        Assert.False(draft.CanBeCorrected);
    }

    // Nothing left to replace.
    [Fact]
    public void A_Cancelled_Note_Cannot_Be_Corrected()
    {
        var invoice = IssuedInvoice();
        invoice.Cancel("Erreur");

        Assert.False(invoice.CanBeCorrected);
    }

    // A note is corrected ONCE; the correction is what gets corrected next. Without this the trail forks and
    // two live notes claim the same séance.
    [Fact]
    public void An_Already_Superseded_Note_Cannot_Be_Corrected_Again()
    {
        var invoice = PaidInvoice();
        invoice.MarkSupersededBy(Guid.NewGuid());

        Assert.False(invoice.CanBeCorrected);
    }

    // ⚠️ Distinct from CanCreateCreditNote, and deliberately: a note with nothing collected has no avoir to
    // establish (there is no money to hand back) but is still perfectly correctable.
    [Fact]
    public void An_Unpaid_Note_Is_Correctable_But_Has_No_Avoir_To_Establish()
    {
        var invoice = IssuedInvoice();

        Assert.True(invoice.CanBeCorrected);
        Assert.False(invoice.CanCreateCreditNote);
    }

    // ── the supersede pair ────────────────────────────────────────────────────────────────────────────

    // Both directions are stored because both are asked: from the old note « what took its place », from the
    // new one « what was this correcting ». A cancelled note whose reader cannot reach its replacement is a
    // dead end, and that is the first question anyone asks of one.
    [Fact]
    public void Supersede_Links_Are_Recorded_In_Both_Directions()
    {
        var original = PaidInvoice();
        var replacement = IssuedInvoice(150m, "2026-0074");

        original.MarkSupersededBy(replacement.Id);
        replacement.MarkSupersedes(original.Id, "Erreur de tarif");

        Assert.Equal(replacement.Id, original.SupersededByInvoiceId);
        Assert.Equal(original.Id, replacement.SupersedesInvoiceId);
        Assert.Equal("Erreur de tarif", replacement.SupersedesReason);
    }

    // A cycle of one would make the trail unreadable in exactly the way the links exist to prevent.
    [Fact]
    public void A_Note_Cannot_Supersede_Itself()
    {
        var invoice = PaidInvoice();

        Assert.Throws<ArgumentException>(() => invoice.MarkSupersededBy(invoice.Id));
        Assert.Throws<ArgumentException>(() => invoice.MarkSupersedes(invoice.Id, "boucle"));
    }

    // The reason is spent when the replacement is issued — that is the moment the predecessor's payments are
    // voided and it is cancelled, and both of those refuse to happen without one.
    [Fact]
    public void A_Correction_Reason_Is_Required()
    {
        var replacement = IssuedInvoice(150m, "2026-0074");

        Assert.Throws<ArgumentException>(() => replacement.MarkSupersedes(Guid.NewGuid(), "   "));
    }

    // ── the correction's own mechanics ────────────────────────────────────────────────────────────────

    // ⚠️ The order is imposed by the aggregate and is not a style choice: `Cancel` refuses while any live
    // payment remains, and `VoidPayment` refuses to touch the payments of a cancelled note. Voiding first
    // satisfies both — and it is what makes the cancellation legitimate rather than a way to erase cash from
    // la caisse, which is the distinction `Cancel`'s own guard draws.
    [Fact]
    public void Correction_Order_Is_Void_Then_Cancel()
    {
        var invoice = PaidInvoice();

        // Cancel first is refused while the money is live.
        Assert.Throws<InvalidOperationException>(() => invoice.Cancel("Erreur de tarif"));

        invoice.VoidPayment(invoice.Payments.Single().Id, "Erreur de tarif", creditedTotal: 0m);
        Assert.Equal(0m, invoice.AmountCollected);
        Assert.True(invoice.CanCancel);

        invoice.Cancel("Erreur de tarif");
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);

        // And now the payments are frozen — the reverse order could never have worked.
        Assert.Throws<InvalidOperationException>(
            () => invoice.VoidPayment(invoice.Payments.Single().Id, "encore", creditedTotal: 0m));
    }

    // ── AmendPaymentDate (L4) ─────────────────────────────────────────────────────────────────────────

    // The reported defect: backdating a séance left its money in the old month, because every money read
    // attributes a payment by PaidOn. No document is touched — the note keeps the day it was written.
    [Fact]
    public void Amending_A_Payment_Date_Moves_Only_The_Payment()
    {
        var invoice = PaidInvoice();
        var issuedOn = invoice.IssueDate;
        var moved = new DateTime(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc);

        invoice.AmendPaymentDate(invoice.Payments.Single().Id, moved);

        Assert.Equal(moved, invoice.Payments.Single().PaidOn);
        Assert.Equal(issuedOn, invoice.IssueDate);
        Assert.Equal(180m, invoice.AmountCollected);
        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
    }

    // That row is reconciled against a bank statement; moving its date would put the two out of agreement with
    // nothing on screen to say so. Refused, not silently skipped.
    [Fact]
    public void A_Banked_Cheque_Refuses_To_Move()
    {
        var invoice = IssuedInvoice();
        invoice.RecordPayment(
            180m, PaymentMethod.Cheque, PaidOn,
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", PaidOn.AddDays(15)),
            banked: ChequeBankedStamp.For(PaymentMethod.Cheque, PaidOn.AddDays(2), "local|abc", "Dr Bel Hadj"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => invoice.AmendPaymentDate(invoice.Payments.Single().Id, PaidOn.AddDays(-3)));

        Assert.Contains("déposé", ex.Message);
        Assert.Equal(PaidOn, invoice.Payments.Single().PaidOn);
    }

    // A voided payment is already out of every total, so there is nothing to move.
    [Fact]
    public void A_Voided_Payment_Refuses_To_Move()
    {
        var invoice = PaidInvoice();
        var payment = invoice.Payments.Single();
        invoice.VoidPayment(payment.Id, "Erreur", creditedTotal: 0m);

        Assert.Throws<InvalidOperationException>(() => invoice.AmendPaymentDate(payment.Id, PaidOn.AddDays(-3)));
    }

    [Fact]
    public void A_Cancelled_Notes_Payments_Refuse_To_Move()
    {
        var invoice = IssuedInvoice();
        invoice.Cancel("Erreur");

        Assert.Throws<InvalidOperationException>(() => invoice.AmendPaymentDate(Guid.NewGuid(), PaidOn));
    }

    // ── the cheque's identity travels with the money ──────────────────────────────────────────────────

    // A correction re-records the payment on the replacement. A cheque left behind would vanish from
    // « chèques à encaisser » entirely — the row that still has to be banked becoming the one row nothing
    // lists — and re-marking a banked one would record today rather than the day it was deposited.
    [Fact]
    public void A_Cheque_Carries_Its_Identity_And_Its_Banked_Mark_Across()
    {
        var original = IssuedInvoice();
        var bankedOn = PaidOn.AddDays(2);
        original.RecordPayment(
            180m, PaymentMethod.Cheque, PaidOn,
            cheque: ChequeDetails.For(PaymentMethod.Cheque, "4512873", "BIAT", PaidOn.AddDays(15)),
            banked: ChequeBankedStamp.For(PaymentMethod.Cheque, bankedOn, "local|abc", "Dr Bel Hadj"));
        var source = original.Payments.Single();

        var replacement = IssuedInvoice(180m, "2026-0074");
        replacement.RecordPayment(
            source.Amount, source.Method, source.PaidOn, source.SourceInstallmentPaymentId,
            source.ToChequeDetails(), source.ToBankedStamp());

        var carried = replacement.Payments.Single();
        Assert.Equal("4512873", carried.ChequeNumber);
        Assert.Equal("BIAT", carried.ChequeBankName);
        Assert.Equal(PaidOn.AddDays(15), carried.ChequeDueDate);
        Assert.Equal(bankedOn, carried.ChequeBankedOn);
        Assert.Equal("Dr Bel Hadj", carried.ChequeBankedByName);
        // ⚠️ And the ORIGINAL date, never today: correcting a mistake now must not move yesterday's takings.
        Assert.Equal(PaidOn, carried.PaidOn);
    }

    // Cash carries no cheque fields — `ChequeDetails.For` is what re-checks that invariant on the way across,
    // rather than the three columns being copied by hand.
    [Fact]
    public void Cash_Carries_No_Cheque_Fields()
    {
        var invoice = PaidInvoice();
        var payment = invoice.Payments.Single();

        Assert.Null(payment.ToChequeDetails());
        Assert.Null(payment.ToBankedStamp());
    }
}
