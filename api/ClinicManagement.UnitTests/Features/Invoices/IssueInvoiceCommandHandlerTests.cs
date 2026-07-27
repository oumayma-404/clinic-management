using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Invoices.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Invoices;

/// <summary>
/// [AC-2] Issuance assigns a per-clinic, gapless, year-scoped number (<c>AAAA-NNNN</c>) and is
/// concurrency-safe (unique-index collision → recompute and retry).
/// </summary>
public class IssueInvoiceCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private Invoice DraftInvoice()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Détartrage", 1, 100m) });
        return invoice;
    }

    private readonly Mock<ITreatmentPlanRepository> _plans = new();

    private IssueInvoiceCommandHandler CreateHandler() => new(
        _invoices.Object, _clinics.Object, _patients.Object, _plans.Object, _clinicResolver.Object, _uow.Object,
        NullLogger<IssueInvoiceCommandHandler>.Instance);

    private void Arrange(Invoice invoice)
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _invoices.Setup(r => r.GetByIdAsync(invoice.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Test"));
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync((Patient?)null);
    }

    // [AC-2] First invoice of the year gets sequence 0001 in AAAA-NNNN format.
    [Fact]
    public async Task Issue_Assigns_First_Sequence()
    {
        var invoice = DraftInvoice();
        Arrange(invoice);
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{DateTime.UtcNow.Year}-0001", result.Value!.Number);
    }

    // [AC-2] The next number is max sequence + 1 (gapless).
    [Fact]
    public async Task Issue_Uses_Next_Sequence()
    {
        var invoice = DraftInvoice();
        Arrange(invoice);
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(41);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{DateTime.UtcNow.Year}-0042", result.Value!.Number);
    }

    // [AC-2] A concurrent numbering collision (unique-index violation) is retried with a recomputed number.
    [Fact]
    public async Task Issue_Retries_On_Number_Collision()
    {
        var invoice = DraftInvoice();
        Arrange(invoice);

        var sequence = 4;
        _invoices.Setup(r => r.GetMaxSequenceForYearAsync(ClinicId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sequence);

        var attempts = 0;
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                attempts++;
                if (attempts == 1)
                {
                    // Simulate a concurrent issuance grabbing 0005 first.
                    sequence = 5;
                    throw new DbUpdateException("unique violation");
                }
                return Task.FromResult(1);
            });

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = invoice.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"{DateTime.UtcNow.Year}-0006", result.Value!.Number);
        Assert.Equal(2, attempts);
    }

    // Tenant isolation: an invoice owned by another clinic reads as "not found".
    [Fact]
    public async Task Issue_Foreign_Clinic_Invoice_Is_NotFound()
    {
        var foreign = new Invoice(Guid.NewGuid(), Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), PatientId);
        foreign.SetLines(new[] { ("Acte", 1, 10m) });
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _invoices.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await CreateHandler().Handle(new IssueInvoiceCommand { Id = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
