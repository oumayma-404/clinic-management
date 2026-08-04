using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// [J4] The second of the three guards: <c>EInvoiceService.ProcessAsync</c> never declares a <b>cancelled</b>
/// note, even if it finds one queued.
///
/// <para>
/// Three guards for one rule looks redundant and is not. <c>Invoice.Cancel</c> now dequeues (covered by
/// <c>InvoiceEInvoiceTests</c>) and <c>GetDueForElFatooraDispatchAsync</c> excludes cancelled rows — but the
/// dispatcher itself is the last line, and it is the one that talks to TTN. It also covers the rows cancelled
/// **before** the dequeue existed, which are still sitting in the outbox with a due date. A note validated at
/// TTN can never be cancelled there, so the cost of the guards disagreeing even once is a note that is annulée
/// in the clinic's books and « validée » in the national registry, permanently.
/// </para>
/// <para>
/// The assertion that matters is a <b>negative</b> one: no signing, no submission. Asserting only on the stored
/// status would pass against a service that submitted first and refused afterwards.
/// </para>
/// </summary>
public class CancelledInvoiceIsNotDispatchedTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITeifXmlGenerator> _teif = new();
    private readonly Mock<IEInvoiceSigner> _signer = new();
    private readonly Mock<ITtnClient> _ttn = new();
    private readonly Mock<IFileStorage> _storage = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public CancelledInvoiceIsNotDispatchedTests()
    {
        _ttn.SetupGet(c => c.Environment).Returns(Clinic.TtnEnvironmentSandbox);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
    }

    private EInvoiceService Service() => new(
        _invoices.Object, _clinics.Object, _patients.Object, _teif.Object, _signer.Object,
        new[] { _ttn.Object }, _storage.Object, _uow.Object,
        new ConfigurationBuilder().Build(), NullLogger<EInvoiceService>.Instance);

    /// <summary>
    /// An issued, queued note that has then been <b>cancelled</b> — the state the outbox must refuse.
    /// <para>
    /// ⚠️ Built by cancelling *before* re-queuing, because <c>Cancel()</c> now dequeues (J4's first guard) and
    /// <c>QueueForElFatoora()</c> refuses a cancelled invoice. Reaching this state through the public API is
    /// therefore impossible by design — which is exactly why the dispatcher's own check is the interesting one:
    /// it defends against the **legacy rows** cancelled before the dequeue shipped, still queued with a due date.
    /// The private setter is used only to reconstruct that historical row.
    /// </para>
    /// </summary>
    private static Invoice CancelledButStillQueued()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        invoice.Issue("2026-0001", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        invoice.QueueForElFatoora();
        invoice.Cancel("Montant saisi par erreur");

        // Re-arm the outbox state a pre-J4 row would still be carrying.
        typeof(Invoice).GetProperty(nameof(Invoice.EInvoiceStatus))!
            .SetValue(invoice, EInvoiceStatus.Queued);
        typeof(Invoice).GetProperty(nameof(Invoice.EInvoiceNextAttemptAt))!
            .SetValue(invoice, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        return invoice;
    }

    private void AssertNothingWasDeclared()
    {
        _teif.Verify(g => g.Generate(It.IsAny<TeifInvoiceInput>()), Times.Never);
        _ttn.Verify(c => c.SubmitAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [J4] The guard itself: a cancelled note found queued is skipped, and nothing reaches TTN.
    [Fact]
    public async Task A_Cancelled_Invoice_Found_Queued_Is_Not_Submitted()
    {
        var invoice = CancelledButStillQueued();
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        await Service().ProcessAsync(invoice.Id, CancellationToken.None);

        AssertNothingWasDeclared();
        Assert.Equal(InvoiceStatus.Cancelled, invoice.Status);
    }

    // [J4] The skip must not be mistaken for a transient failure: recording one would leave the row queued with a
    // fresh due date and have the next tick try again forever.
    [Fact]
    public async Task The_Skip_Does_Not_Schedule_A_Retry()
    {
        var invoice = CancelledButStillQueued();
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        await Service().ProcessAsync(invoice.Id, CancellationToken.None);

        Assert.Equal(0, invoice.EInvoiceAttemptCount);
        Assert.Null(invoice.EInvoiceLastError);
    }

    // [J4] A missing invoice is a no-op, not a throw — ProcessAsync is called from the outbox job and from a
    // command, and its contract is that it never throws to the caller.
    [Fact]
    public async Task A_Missing_Invoice_Is_A_Silent_NoOp()
    {
        _invoices.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Invoice?)null);

        await Service().ProcessAsync(Guid.NewGuid(), CancellationToken.None);

        AssertNothingWasDeclared();
    }

    // [J4] And a note that is not queued at all is left alone — the pre-existing gate still runs first, so the
    // new status check cannot be the only thing standing between a Valid invoice and a second declaration.
    [Fact]
    public async Task A_NotSubmitted_Invoice_Is_Not_Dispatched()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        invoice.Issue("2026-0002", vatApplicable: false, vatRate: 0m, stampDutyEnabled: false, stampDutyAmount: 0m);
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        await Service().ProcessAsync(invoice.Id, CancellationToken.None);

        AssertNothingWasDeclared();
        Assert.Equal(EInvoiceStatus.NotSubmitted, invoice.EInvoiceStatus);
    }
}
