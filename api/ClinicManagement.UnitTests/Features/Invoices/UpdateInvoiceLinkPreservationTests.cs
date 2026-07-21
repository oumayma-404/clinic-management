using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// fix-misc-data-integrity #10: the edit UI sends only patient + lines, so the handler must preserve the
/// draft invoice's existing header dental-record / appointment links rather than nulling them (the header
/// DentalRecordId drives the "already invoiced" guard).
/// </summary>
public class UpdateInvoiceLinkPreservationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task Update_Preserves_Existing_Links_When_Request_Omits_Them()
    {
        var patientId = Guid.NewGuid();
        var dentalRecordId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        var invoice = new Invoice(Guid.NewGuid(), ClinicId, patientId);
        invoice.SetLines(new[] { ("Acte", 1, 100m) });
        invoice.UpdateLinks(patientId, dentalRecordId, appointmentId); // seeded from a dental record

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var patient = new Patient(patientId, ClinicId, "Jean", "Dupont",
            new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));
        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateInvoiceCommandHandler(
            invoices.Object, patients.Object, resolver.Object, uow.Object,
            NullLogger<UpdateInvoiceCommandHandler>.Instance);

        // Edit request carries NO links (mirrors the FE edit modal) — just patient + a changed line.
        var result = await handler.Handle(new UpdateInvoiceCommand
        {
            Id = invoice.Id,
            PatientId = patientId,
            Lines = new List<InvoiceLineRequest> { new() { Designation = "Acte", Quantity = 1, UnitPriceHt = 120m } },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(dentalRecordId, invoice.DentalRecordId); // preserved, not nulled
        Assert.Equal(appointmentId, invoice.AppointmentId);   // preserved, not nulled
    }
}
