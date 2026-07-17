using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Invoice TTN « El Fatoora » e-invoicing state machine (feature facturation-einvoicing-ttn): queue/outbox
/// entry (FR-4), the status lifecycle + persisted fields (FR-5), idempotency (edge: duplicate submission),
/// and bounded retry with backoff (FR-4). Independent of the fiscal <see cref="InvoiceStatus"/> lifecycle.
/// </summary>
public class InvoiceEInvoiceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Invoice IssuedInvoice()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        invoice.Issue("2026-0001", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        return invoice;
    }

    // [FR-5] A fresh invoice has no e-invoicing state.
    [Fact]
    public void New_Invoice_Is_NotSubmitted()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);

        Assert.Equal(EInvoiceStatus.NotSubmitted, invoice.EInvoiceStatus);
        Assert.Null(invoice.TtnIdentifier);
        Assert.Null(invoice.QrPayload);
        Assert.Equal(0, invoice.EInvoiceAttemptCount);
    }

    // [FR-4] A draft cannot be sent to El Fatoora — it must be fiscally issued first.
    [Fact]
    public void QueueForElFatoora_On_Draft_Throws()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });

        Assert.False(invoice.CanSubmitToElFatoora);
        Assert.Throws<InvalidOperationException>(() => invoice.QueueForElFatoora());
    }

    // [FR-4] Queuing an issued invoice marks it Queued and due immediately, resetting the retry budget.
    [Fact]
    public void QueueForElFatoora_On_Issued_Sets_Queued()
    {
        var invoice = IssuedInvoice();

        invoice.QueueForElFatoora();

        Assert.Equal(EInvoiceStatus.Queued, invoice.EInvoiceStatus);
        Assert.NotNull(invoice.EInvoiceNextAttemptAt);
        Assert.Equal(0, invoice.EInvoiceAttemptCount);
        Assert.Null(invoice.EInvoiceLastError);
    }

    // [Edge: cancelled invoice] A cancelled invoice is never submitted.
    [Fact]
    public void QueueForElFatoora_On_Cancelled_Throws()
    {
        var invoice = IssuedInvoice();
        invoice.Cancel("Erreur de saisie");

        Assert.False(invoice.CanSubmitToElFatoora);
        Assert.Throws<InvalidOperationException>(() => invoice.QueueForElFatoora());
    }

    // [FR-5] Signing records the signed-XML key and moves to Signed.
    [Fact]
    public void MarkEInvoiceSigned_Sets_Signed_And_Key()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();

        invoice.MarkEInvoiceSigned("clinic/e-invoices/x-signed.xml");

        Assert.Equal(EInvoiceStatus.Signed, invoice.EInvoiceStatus);
        Assert.Equal("clinic/e-invoices/x-signed.xml", invoice.SignedXmlStorageKey);
    }

    // [FR-5] Validation captures the TTN id, QR cachet and receipt, and stamps the timestamps.
    [Fact]
    public void MarkEInvoiceValidated_Sets_Valid_Fields()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();
        invoice.MarkEInvoiceSigned("k");

        invoice.MarkEInvoiceValidated("TTN-123", "ttn=TTN-123;ttc=100.000", "clinic/e-invoices/x-receipt.xml");

        Assert.Equal(EInvoiceStatus.Valid, invoice.EInvoiceStatus);
        Assert.Equal("TTN-123", invoice.TtnIdentifier);
        Assert.Equal("ttn=TTN-123;ttc=100.000", invoice.QrPayload);
        Assert.Equal("clinic/e-invoices/x-receipt.xml", invoice.TtnReceiptStorageKey);
        Assert.NotNull(invoice.EInvoiceValidatedAt);
        Assert.NotNull(invoice.EInvoiceSubmittedAt);
        Assert.Null(invoice.EInvoiceLastError);
        Assert.Null(invoice.EInvoiceNextAttemptAt);
    }

    // [Edge: duplicate submission] A validated invoice is not re-submittable (idempotent per invoice).
    [Fact]
    public void Validated_Invoice_Cannot_Be_Requeued()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();
        invoice.MarkEInvoiceSigned("k");
        invoice.MarkEInvoiceValidated("TTN-123", "qr", null);

        Assert.False(invoice.CanSubmitToElFatoora);
        Assert.Throws<InvalidOperationException>(() => invoice.QueueForElFatoora());
    }

    // [Edge: TTN rejection] A permanent rejection records the reason and stops retrying.
    [Fact]
    public void MarkEInvoiceRejected_Sets_Rejected_And_Error()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();

        invoice.MarkEInvoiceRejected("Schéma TEIF invalide");

        Assert.Equal(EInvoiceStatus.Rejected, invoice.EInvoiceStatus);
        Assert.Equal("Schéma TEIF invalide", invoice.EInvoiceLastError);
        Assert.Null(invoice.EInvoiceNextAttemptAt);
        // A rejected invoice may be corrected and re-sent (US-5).
        Assert.True(invoice.CanSubmitToElFatoora);
    }

    // [FR-4] A transient failure keeps the invoice Queued until the attempt cap, then crosses to Failed.
    [Fact]
    public void RecordEInvoiceFailure_Stays_Queued_Until_Max_Then_Failed()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();
        var next = DateTime.UtcNow.AddMinutes(1);

        invoice.RecordEInvoiceFailure("Réseau indisponible", maxAttempts: 2, nextAttemptAt: next);
        Assert.Equal(EInvoiceStatus.Queued, invoice.EInvoiceStatus);
        Assert.Equal(1, invoice.EInvoiceAttemptCount);
        Assert.NotNull(invoice.EInvoiceNextAttemptAt);

        invoice.RecordEInvoiceFailure("Réseau indisponible", maxAttempts: 2, nextAttemptAt: next);
        Assert.Equal(EInvoiceStatus.Failed, invoice.EInvoiceStatus);
        Assert.Equal(2, invoice.EInvoiceAttemptCount);
        Assert.Null(invoice.EInvoiceNextAttemptAt);
    }

    // [US-5] A failed invoice can be retried (re-queued), which resets the retry budget.
    [Fact]
    public void Failed_Invoice_Can_Be_Requeued()
    {
        var invoice = IssuedInvoice();
        invoice.QueueForElFatoora();
        var next = DateTime.UtcNow.AddMinutes(1);
        invoice.RecordEInvoiceFailure("x", maxAttempts: 1, nextAttemptAt: next); // → Failed

        Assert.Equal(EInvoiceStatus.Failed, invoice.EInvoiceStatus);
        Assert.True(invoice.CanSubmitToElFatoora);

        invoice.QueueForElFatoora();

        Assert.Equal(EInvoiceStatus.Queued, invoice.EInvoiceStatus);
        Assert.Equal(0, invoice.EInvoiceAttemptCount);
    }
}
