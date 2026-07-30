using ClinicManagement.UnitTests.Common;
using ClinicManagement.Domain.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Application.Features.Invoices.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [AC-10] Invoices are strictly clinic-scoped: another clinic's invoice reads as "not found" for
/// get/update/cancel/payment/delete, and the list is scoped to the caller's clinic.
/// </summary>
public class InvoiceTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    // No avoirs in these fixtures. Stated explicitly rather than left to Moq's default, because the
    // batch read returns a dictionary the handlers immediately enumerate.
    private readonly Mock<ICreditNoteRepository> _creditNotes = NoCreditNotes();

    private static Mock<ICreditNoteRepository> NoCreditNotes()
    {
        var mock = new Mock<ICreditNoteRepository>();
        mock.Setup(r => r.GetTotalsForInvoicesAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, decimal>());
        return mock;
    }

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    private Invoice ForeignIssuedInvoice()
    {
        var invoice = new Invoice(Guid.NewGuid(), OtherClinicId, PatientId);
        invoice.SetLines(new[] { ("Acte", 1, 100m) });
        invoice.Issue("2026-0001", false, 0m, false, 0m);
        return invoice;
    }

    private Invoice ForeignDraftInvoice()
    {
        var invoice = new Invoice(Guid.NewGuid(), OtherClinicId, PatientId);
        invoice.SetLines(new[] { ("Acte", 1, 100m) });
        return invoice;
    }

    [Fact]
    public async Task Get_Foreign_Invoice_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignIssuedInvoice();
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new GetInvoiceQueryHandler(
            _invoices.Object, _patients.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoiceQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoiceQuery { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Update_Foreign_Invoice_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignDraftInvoice();
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new UpdateInvoiceCommandHandler(
            _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<UpdateInvoiceCommandHandler>.Instance);

        var result = await handler.Handle(
            new UpdateInvoiceCommand { Id = foreign.Id, PatientId = PatientId }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Foreign_Invoice_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignIssuedInvoice();
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new CancelInvoiceCommandHandler(
            _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<CancelInvoiceCommandHandler>.Instance);

        var result = await handler.Handle(
            new CancelInvoiceCommand { Id = foreign.Id, Reason = "x" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordPayment_Foreign_Invoice_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignIssuedInvoice();
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new RecordPaymentCommandHandler(
            _invoices.Object, _patients.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<RecordPaymentCommandHandler>.Instance);

        var result = await handler.Handle(
            new RecordPaymentCommand { InvoiceId = foreign.Id, Amount = 10m, Method = "Cash", PaidOn = DateTime.UtcNow },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Delete_Foreign_Invoice_Is_NotFound()
    {
        Authenticated();
        var foreign = ForeignDraftInvoice();
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var handler = new DeleteInvoiceCommandHandler(
            _invoices.Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DeleteInvoiceCommandHandler>.Instance);

        var result = await handler.Handle(new DeleteInvoiceCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _invoices.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-9/AC-10] The list is scoped to the caller's clinic (repo queried with the caller clinic id).
    [Fact]
    public async Task List_Is_Scoped_To_Caller_Clinic()
    {
        Authenticated();
        _invoices.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
                It.IsAny<InvoiceStatus?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Invoice>()).AsPage());
        _patients.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Patient>()).AsPage());

        var handler = new GetInvoicesQueryHandler(
            _invoices.Object, _patients.Object, _creditNotes.Object, _clinicResolver.Object,
            NullLogger<GetInvoicesQueryHandler>.Instance);

        var result = await handler.Handle(new GetInvoicesQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _invoices.Verify(r => r.GetFilteredAsync(
            ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(),
            It.IsAny<InvoiceStatus?>(), It.IsAny<string?>(), It.IsAny<PageRequest?>(),
                It.IsAny<CancellationToken>()), Times.Once);
    }
}
