using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// [AC-P2.13][AC-P2.14][AC-P2.15] Deleting a fiche de soins detaches its two FK-less soft links instead of
/// orphaning them — audit § 6.11.
/// <para>
/// Both links are deliberately without a foreign key (<c>InvoiceLineConfiguration:36</c>,
/// <c>TreatmentPlanItemConfiguration:55</c>), so nothing at the database level clears them. Before this, deleting a
/// fiche left a plan act « réalisé » pointing at a row that no longer existed — and because marking an act done can
/// auto-complete a plan, a deleted fiche could leave a devis closed against evidence that is gone, with no un-mark
/// anywhere to correct it. That is why § 5.3, § 5.6 and § 6.11 had to land together and in that order.
/// </para>
/// </summary>
public class DeleteDentalRecordCleanupTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid RecordId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly DateTime DoneOn = new(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public DeleteDentalRecordCleanupTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.Models.Result<Guid>.Success(ClinicId));

        var patient = new Patient(PatientId, ClinicId, "Mohamed", "Ben Ali", new DateTime(1981, 6, 14), "Male");
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var record = new DentalRecord(RecordId, PatientId, DoneOn, 0m, true);
        _records.Setup(r => r.GetByIdAsync(RecordId, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        // Default: nothing links to the fiche. Individual tests override.
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TreatmentPlan>());
        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
    }

    private DeleteDentalRecordCommandHandler Handler() => new(
        _records.Object, _patients.Object, _plans.Object, _invoices.Object,
        _clinicResolver.Object, _uow.Object, NullLogger<DeleteDentalRecordCommandHandler>.Instance);

    private static TreatmentPlan PlanWithActDoneOnRecord()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Plan");
        plan.SetItems(new[] { ("Obturation", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 26 }) });
        plan.Accept("2026-0001");
        plan.MarkItemDone(plan.Items.Single().Id, DoneOn, RecordId);
        return plan;
    }

    private async Task<Application.Common.Models.Result<bool>> DeleteAsync() =>
        await Handler().Handle(
            new DeleteDentalRecordCommand { Id = RecordId, PatientId = PatientId }, CancellationToken.None);

    // [AC-P2.13] The plan act returns to « prévu » and its link is cleared.
    [Fact]
    public async Task Deleting_A_Fiche_Returns_Its_Plan_Act_To_Planned()
    {
        var plan = PlanWithActDoneOnRecord();
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        var result = await DeleteAsync();

        Assert.True(result.IsSuccess);
        var item = plan.Items.Single();
        Assert.Equal(TreatmentPlanItemStatus.Planned, item.Status);
        Assert.Null(item.LinkedDentalRecordId);
        _plans.Verify(r => r.UpdateAsync(plan, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-P2.13] And the devis that act had auto-completed is reopened — the compounding half of § 6.11.
    [Fact]
    public async Task Deleting_A_Fiche_Reopens_The_Devis_Its_Act_Had_Closed()
    {
        var plan = PlanWithActDoneOnRecord();
        Assert.Equal(TreatmentPlanStatus.Completed, plan.Status);
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        await DeleteAsync();

        Assert.Equal(TreatmentPlanStatus.Accepted, plan.Status);
    }

    // [AC-P2.14] The invoice line forgets the fiche — but keeps its money and its number.
    [Fact]
    public async Task Deleting_A_Fiche_Detaches_Its_Invoice_Line_Without_Touching_The_Money()
    {
        var invoice = new Invoice(Guid.NewGuid(), ClinicId, PatientId);
        invoice.SetLines(new[] { ("Obturation", 1, 500m, (Guid?)RecordId, (Guid?)null, (string?)null) });
        var invoiceId = invoice.Id;
        var totalBefore = invoice.TotalTtc;

        _invoices.Setup(r => r.GetDentalRecordLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { (RecordId, invoiceId, (string?)"2026-0007", InvoiceStatus.Issued) });
        _invoices.Setup(r => r.GetByIdAsync(invoiceId, It.IsAny<CancellationToken>())).ReturnsAsync(invoice);

        var result = await DeleteAsync();

        Assert.True(result.IsSuccess);
        Assert.All(invoice.Lines, l => Assert.Null(l.DentalRecordId));
        Assert.Single(invoice.Lines);                    // the line itself survives
        Assert.Equal(totalBefore, invoice.TotalTtc);     // and so does the amount
        _invoices.Verify(r => r.UpdateAsync(invoice, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-P2.15] One transaction — a partial cleanup is the defect, not the fix.
    [Fact]
    public async Task The_Cleanup_And_The_Delete_Share_One_Transaction()
    {
        var plan = PlanWithActDoneOnRecord();
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });

        await DeleteAsync();

        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-P2.15] A failure mid-cleanup rolls the whole thing back rather than half-detaching.
    [Fact]
    public async Task A_Failure_During_Cleanup_Rolls_Back_And_Deletes_Nothing()
    {
        var plan = PlanWithActDoneOnRecord();
        _plans.Setup(r => r.GetByLinkedDentalRecordAsync(ClinicId, RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { plan });
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await DeleteAsync();

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [A-9] The clinic-resolution failure is French — it returned the English
    // "Unable to resolve current clinic" before, which the § 2 exception sweep missed.
    [Fact]
    public async Task An_Unresolvable_Clinic_Fails_In_French()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Application.Common.Models.Result<Guid>.Failure("Cabinet introuvable."));

        var result = await DeleteAsync();

        Assert.True(result.IsFailure);
        Assert.DoesNotContain("Unable to resolve", result.Error ?? string.Empty);
        _records.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // Tenant isolation: another clinic's fiche reads as not-found and nothing is deleted.
    [Fact]
    public async Task Refuses_A_Fiche_Whose_Patient_Belongs_To_Another_Clinic()
    {
        var foreign = new Patient(PatientId, OtherClinicId, "Autre", "Patient", new DateTime(1990, 1, 1), "Male");
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await DeleteAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("introuvable", result.Error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _records.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
