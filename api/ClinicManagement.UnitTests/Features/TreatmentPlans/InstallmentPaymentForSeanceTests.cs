using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.TreatmentPlans.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// The desk collects a devis séance from « À encaisser » (`seance-au-fauteuil`), and the payment names that séance's
/// fiche — the key <c>TreatmentPlan.CollectedOnRecord</c> reads. Without it the dentist re-saving the fiche with
/// « Encaissé sur le traitement » filled would take the money a second time.
/// </summary>
public class InstallmentPaymentForSeanceTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime PaidOn = new(2026, 3, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IDentalRecordRepository> _records = new();
    private readonly Mock<ICurrentClinicResolver> _resolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public InstallmentPaymentForSeanceTests()
    {
        _resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        _patients.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Patient?)null);
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus, decimal TotalTtc, decimal Outstanding)>());
    }

    private TreatmentPlan AcceptedPlan()
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, PatientId, "Réhabilitation");
        plan.SetItems(new[] { ("Couronne", 1000m, (IReadOnlyList<int>)new[] { 11 }) });
        plan.Accept("2026-0014");
        _plans.Setup(r => r.GetByIdAsync(plan.Id, It.IsAny<CancellationToken>())).ReturnsAsync(plan);
        return plan;
    }

    private DentalRecord FicheOf(Guid patientId)
    {
        var record = new DentalRecord(Guid.NewGuid(), patientId, ClinicId, PaidOn, 0m, true);
        _records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);
        return record;
    }

    private Task<Result<Application.DTOs.TreatmentPlanDto>> Collect(TreatmentPlan plan, Guid? dentalRecordId) =>
        new RecordInstallmentPaymentCommandHandler(
                _plans.Object, _invoices.Object, _patients.Object, _records.Object, _resolver.Object, _uow.Object,
                NullLogger<RecordInstallmentPaymentCommandHandler>.Instance)
            .Handle(
                new RecordInstallmentPaymentCommand
                {
                    PlanId = plan.Id,
                    InstallmentId = plan.Installments.First().Id,
                    Amount = 400m,
                    Method = nameof(PaymentMethod.Cash),
                    PaidOn = PaidOn,
                    DentalRecordId = dentalRecordId,
                },
                CancellationToken.None);

    [Fact]
    public async Task A_Desk_Payment_For_A_Seance_Is_That_Fiches_Money()
    {
        var plan = AcceptedPlan();
        var fiche = FicheOf(PatientId);

        var result = await Collect(plan, fiche.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(400m, plan.CollectedOnRecord(fiche.Id));
    }

    // Another patient's fiche would make THAT fiche's next save believe the money was already taken.
    [Fact]
    public async Task Another_Patients_Fiche_Is_Refused()
    {
        var plan = AcceptedPlan();
        var stranger = FicheOf(Guid.NewGuid());

        var result = await Collect(plan, stranger.Id);

        Assert.True(result.IsFailure);
        Assert.Equal(0m, plan.Installments.First().AmountPaid);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // An ordinary payment names no séance, exactly as before.
    [Fact]
    public async Task A_Payment_Without_A_Fiche_Is_Unchanged()
    {
        var plan = AcceptedPlan();

        var result = await Collect(plan, null);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(400m, plan.Installments.First().AmountPaid);
        _records.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
