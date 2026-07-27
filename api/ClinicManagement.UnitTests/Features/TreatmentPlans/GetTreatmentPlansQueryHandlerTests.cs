using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.TreatmentPlans.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.TreatmentPlans;

/// <summary>
/// [AC-6] The list page's read contract: whatever the number of plans or distinct patients, the handler
/// issues exactly one appointments query, one invoice-links query and one patient query — never one per row.
/// The per-patient <c>GetByIdAsync</c> N+1 this replaced is pinned as never-called.
/// </summary>
public class GetTreatmentPlansQueryHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientAId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid PatientBId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly Mock<ITreatmentPlanRepository> _plans = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IInvoiceRepository> _invoices = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetTreatmentPlansQueryHandler CreateHandler() => new(
        _plans.Object, _patients.Object, _appointments.Object, _invoices.Object, _clinicResolver.Object,
        NullLogger<GetTreatmentPlansQueryHandler>.Instance);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

    private static Patient PatientFixture(Guid id, string first) => new(
        id, ClinicId, first, "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email($"{first.ToLowerInvariant()}@example.com"), new PhoneNumber("+21620123456"));

    private static TreatmentPlan PlanFixture(Guid patientId)
    {
        var plan = new TreatmentPlan(Guid.NewGuid(), ClinicId, patientId, "Plan");
        plan.SetItems(new[] { ("Couronne", 500m, (Guid?)null, (string?)null, (IReadOnlyList<int>)new[] { 11 }) });
        return plan;
    }

    /// <summary>Four plans across two patients — enough that an N+1 would show up as 4 (or 2) calls.</summary>
    private void MultiPlanMultiPatientPage()
    {
        Authenticated();
        _plans.Setup(r => r.GetFilteredAsync(
                ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                PlanFixture(PatientAId), PlanFixture(PatientAId),
                PlanFixture(PatientBId), PlanFixture(PatientBId),
            });
        _patients.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { PatientFixture(PatientAId, "Jean"), PatientFixture(PatientBId, "Marie") });
        _appointments.Setup(r => r.GetByTreatmentPlanItemIdsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>());
        _invoices.Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());
    }

    // [AC-6] One appointments query and one invoice-links query for the whole page.
    [Fact]
    public async Task Handle_Issues_One_Appointments_And_One_Invoice_Links_Query_For_The_Page()
    {
        MultiPlanMultiPatientPage();

        var result = await CreateHandler().Handle(new GetTreatmentPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value!.Count);
        _appointments.Verify(r => r.GetByTreatmentPlanItemIdsAsync(
            ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _invoices.Verify(r => r.GetTreatmentPlanLinksAsync(
            ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-6] Patient names come from one clinic-wide read; the pre-existing per-patient lookup is gone.
    [Fact]
    public async Task Handle_Resolves_Patient_Names_Without_A_Per_Patient_Lookup()
    {
        MultiPlanMultiPatientPage();

        var result = await CreateHandler().Handle(new GetTreatmentPlansQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!, d => d.PatientName == "Jean Dupont");
        Assert.Contains(result.Value!, d => d.PatientName == "Marie Dupont");
        _patients.Verify(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
        _patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-24] The list reads only the caller's clinic — the resolved clinic id is what reaches every repo.
    [Fact]
    public async Task Handle_Scopes_Every_Read_To_The_Caller_Clinic()
    {
        MultiPlanMultiPatientPage();

        await CreateHandler().Handle(new GetTreatmentPlansQuery(), CancellationToken.None);

        _plans.Verify(r => r.GetFilteredAsync(
            ClinicId, It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5] The derived progress counts are always populated, even with no appointment or invoice in play.
    [Fact]
    public async Task Handle_Populates_Derived_Progress_Counts()
    {
        MultiPlanMultiPatientPage();

        var result = await CreateHandler().Handle(new GetTreatmentPlansQuery(), CancellationToken.None);

        Assert.All(result.Value!, dto =>
        {
            Assert.Equal(1, dto.ItemsTotal);
            Assert.Equal(0, dto.ItemsDone);
            Assert.Null(dto.NextAppointmentAt);
            Assert.Null(dto.LinkedInvoiceId);
        });
    }

    // An unparseable status filter is a client error, not an empty list — and nothing is read.
    [Fact]
    public async Task Handle_Rejects_An_Invalid_Status_Filter()
    {
        Authenticated();

        var result = await CreateHandler().Handle(
            new GetTreatmentPlansQuery { Status = "Pas-Un-Statut" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _plans.Verify(r => r.GetFilteredAsync(
            It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<TreatmentPlanStatus?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
