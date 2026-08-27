using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Invoice aggregate invariants: draft lifecycle (AC-1), optional source links (AC-4), issuance +
/// frozen totals (AC-3), payments + overpayment guard (AC-5), and cancellation rules (AC-6).
/// </summary>
public class InvoiceEntityTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Invoice DraftWithLine(decimal unitPrice = 100m)
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, unitPrice) });
        return invoice;
    }

    // [AC-1] A new invoice is a draft with no number, and is deletable.
    [Fact]
    public void New_Invoice_Is_Draft_Without_Number()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);

        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
        Assert.True(invoice.CanBeDeleted);
    }

    // [AC-4] Optional dental-record / appointment links are stored on the invoice.
    [Fact]
    public void Ctor_Stores_Optional_Source_Links()
    {
        var dentalRecordId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId, dentalRecordId, appointmentId);

        Assert.Equal(dentalRecordId, invoice.DentalRecordId);
        Assert.Equal(appointmentId, invoice.AppointmentId);
    }

    // [AC-1] Issuing assigns the number and computes the totals — which are the acts and nothing else, since
    // the product applies no TVA and no timbre fiscal. This used to expect 100 HT → 7 TVA → 108 TTC.
    [Fact]
    public void Issue_Assigns_Number_And_Freezes_Totals()
    {
        var invoice = DraftWithLine(100m);

        invoice.Issue("2026-0001");

        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("2026-0001", invoice.Number);
        Assert.NotNull(invoice.IssueDate);
        Assert.Equal(100.000m, invoice.TotalHt);
        Assert.Equal(0m, invoice.TotalVat);
        Assert.Equal(100.000m, invoice.TotalTtc);
    }

    // [AC-1] A draft with no lines cannot be issued.
    [Fact]
    public void Issue_Requires_At_Least_One_Line()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.Issue("2026-0001"));
    }

    // [AC-1] An issued invoice can no longer be edited.
    [Fact]
    public void SetLines_On_Issued_Throws()
    {
        var invoice = DraftWithLine();
        invoice.Issue("2026-0001");

        Assert.Throws<InvalidOperationException>(() =>
            invoice.SetLines(new[] { ("Autre", 1, 50m) }));
    }

    // [AC-5] A partial payment updates the collected amount and moves to PartiallyPaid.
    [Fact]
    public void RecordPayment_Partial_Sets_PartiallyPaid()
    {
        var invoice = DraftWithLine(100m);
        invoice.Issue("2026-0001"); // TTC = 100

        invoice.RecordPayment(40m, PaymentMethod.Cash, DateTime.UtcNow);

        Assert.Equal(40m, invoice.AmountCollected);
        Assert.Equal(60m, invoice.Outstanding);
        Assert.Equal(InvoiceStatus.PartiallyPaid, invoice.Status);
    }

    // [AC-5] Paying exactly the TTC moves the invoice to Paid.
    [Fact]
    public void RecordPayment_Exact_Sets_Paid()
    {
        var invoice = DraftWithLine(100m);
        invoice.Issue("2026-0001"); // TTC = 100

        invoice.RecordPayment(100m, PaymentMethod.Card, DateTime.UtcNow);

        Assert.Equal(InvoiceStatus.Paid, invoice.Status);
        Assert.Equal(0m, invoice.Outstanding);
    }

    // [AC-5] An overpayment (beyond the TTC) is refused.
    [Fact]
    public void RecordPayment_Overpayment_Throws()
    {
        var invoice = DraftWithLine(100m);
        invoice.Issue("2026-0001"); // TTC = 100

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordPayment(101m, PaymentMethod.Cash, DateTime.UtcNow));
    }

    // [AC-5] A draft cannot receive a payment (must be issued first).
    [Fact]
    public void RecordPayment_On_Draft_Throws()
    {
        var invoice = DraftWithLine(100m);

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordPayment(10m, PaymentMethod.Cash, DateTime.UtcNow));
    }

    // [AC-6] A draft is deleted, not cancelled.
    [Fact]
    public void Cancel_On_Draft_Throws()
    {
        var invoice = DraftWithLine(100m);

        Assert.Throws<InvalidOperationException>(() => invoice.Cancel("erreur"));
    }

    // [AC-6] Cancelling an issued invoice keeps its number and requires a reason.
    [Fact]
    public void Cancel_Keeps_Number_And_Requires_Reason()
    {
        var invoice = DraftWithLine(100m);
        invoice.Issue("2026-0007");

        Assert.Throws<ArgumentException>(() => invoice.Cancel("  "));

        invoice.Cancel("Erreur de saisie");

        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
        Assert.Equal("2026-0007", invoice.Number);
        Assert.Equal("Erreur de saisie", invoice.CancellationReason);
        Assert.False(invoice.CanBeDeleted);
    }

    // [AC-6] A cancelled invoice accepts no further payment.
    [Fact]
    public void RecordPayment_On_Cancelled_Throws()
    {
        var invoice = DraftWithLine(100m);
        invoice.Issue("2026-0007");
        invoice.Cancel("annulée");

        Assert.Throws<InvalidOperationException>(() =>
            invoice.RecordPayment(10m, PaymentMethod.Cash, DateTime.UtcNow));
    }
}
