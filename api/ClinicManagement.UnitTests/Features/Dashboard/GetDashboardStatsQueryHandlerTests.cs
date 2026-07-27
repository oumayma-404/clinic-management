using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Dashboard.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Dashboard;

public class GetDashboardStatsQueryHandlerTests
{
    private readonly Mock<IAppointmentRepository> _appointmentRepository = new();
    private readonly Mock<IPatientRepository> _patientRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IInvoiceRepository> _invoiceRepository = new();
    private readonly Mock<ITreatmentPlanRepository> _planRepository = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private const string Auth0Sub = "auth0|user-123";
    private static readonly Guid ClinicId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTime TodayStart = new(2026, 6, 25, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TodayEnd = new(2026, 6, 25, 23, 59, 59, DateTimeKind.Utc);
    private static readonly DateTime WeekStart = new(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WeekEnd = new(2026, 6, 28, 23, 59, 59, DateTimeKind.Utc);

    private GetDashboardStatsQueryHandler CreateHandler() =>
        new(_appointmentRepository.Object, _patientRepository.Object, _userRepository.Object, _invoiceRepository.Object, _planRepository.Object, _clinicContext.Object);

    private static GetDashboardStatsQuery CreateQuery() => new()
    {
        TodayStart = TodayStart,
        TodayEnd = TodayEnd,
        WeekStart = WeekStart,
        WeekEnd = WeekEnd
    };

    private void SetupAuthenticatedUser()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _userRepository
            .Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(Auth0Sub, ClinicId, "doctor"));

        // Money aggregates default to nothing owed/collected so these appointment-count assertions are
        // unaffected by the unified-ledger additions (installment revenue + total outstanding).
        _invoiceRepository
            .Setup(r => r.GetOutstandingByPatientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid PatientId, decimal Outstanding)>());
        _invoiceRepository
            .Setup(r => r.GetTreatmentPlanLinksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)>());
        _planRepository
            .Setup(r => r.GetInstallmentCollectedBetweenAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0m);
        _planRepository
            .Setup(r => r.GetInstallmentOutstandingByPatientAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid PatientId, decimal Outstanding, DateTime? OldestOverdueDueDate)>());
    }

    // [AC-12a] The dashboard's outstanding KPI de-duplicates a bridged plan exactly like « Solde patient »:
    // a plan represented by an issued invoice is passed to the plan aggregate as an excluded id, so its
    // échéancier is never added on top of the invoice's own balance.
    [Fact]
    public async Task Handle_Should_Exclude_Plans_Already_Billed_To_An_Invoice_From_Outstanding()
    {
        SetupAuthenticatedUser();

        var billedPlanId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var cancelledBridgePlanId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        _invoiceRepository
            .Setup(r => r.GetTreatmentPlanLinksAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(Guid TreatmentPlanId, Guid InvoiceId, string? Number, InvoiceStatus Status)>
            {
                (billedPlanId, Guid.NewGuid(), "2026-0031", InvoiceStatus.Issued),
                // A cancelled bridge is void: the plan carries its own balance again and must NOT be excluded.
                (cancelledBridgePlanId, Guid.NewGuid(), "2026-0030", InvoiceStatus.Cancelled)
            });

        await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        _planRepository.Verify(
            r => r.GetInstallmentOutstandingByPatientAsync(
                ClinicId,
                It.IsAny<DateTime>(),
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(billedPlanId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-1][AC-3][AC-4] Returns the real, clinic-scoped counts mapped onto the DTO.
    [Fact]
    public async Task Handle_Should_Return_All_Counts_Mapped_From_Repositories()
    {
        SetupAuthenticatedUser();

        _appointmentRepository
            .Setup(r => r.CountByClinicIdAsync(ClinicId, TodayStart, TodayEnd, null, It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _appointmentRepository
            .Setup(r => r.CountByClinicIdAsync(ClinicId, WeekStart, WeekEnd, null, It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _appointmentRepository
            .Setup(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), AppointmentStatus.Scheduled, It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _patientRepository
            .Setup(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);
        _patientRepository
            .Setup(r => r.CountFlaggedByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.TodaysAppointments);
        Assert.Equal(42, result.Value!.TotalPatients);
        Assert.Equal(5, result.Value!.UpcomingPending);
        Assert.Equal(10, result.Value!.ThisWeekAppointments);
        Assert.Equal(2, result.Value!.UrgentPatients);
    }

    // [AC-2] "Pending" counts upcoming appointments in the Scheduled (awaiting-confirmation) state.
    [Fact]
    public async Task Handle_Should_Count_Pending_Using_Scheduled_Status()
    {
        SetupAuthenticatedUser();

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _appointmentRepository.Verify(
            r => r.CountByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), AppointmentStatus.Scheduled, It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // [AC-1] Counts are scoped to the authenticated user's clinic.
    [Fact]
    public async Task Handle_Should_Scope_Counts_To_Users_Clinic()
    {
        SetupAuthenticatedUser();

        await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        _patientRepository.Verify(r => r.CountByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        _patientRepository.Verify(r => r.CountFlaggedByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
        _appointmentRepository.Verify(
            r => r.CountByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AppointmentStatus?>(), It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    // [AC-7] Fails (no fabricated data) when there is no user id in the token.
    [Fact]
    public async Task Handle_Should_Fail_When_No_User_Id_In_Token()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns((string?)null);

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _appointmentRepository.Verify(
            r => r.CountByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<AppointmentStatus?>(), It.IsAny<IReadOnlyCollection<AppointmentStatus>?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // [AC-7] Fails when the token's user is not found in the database.
    [Fact]
    public async Task Handle_Should_Fail_When_User_Not_Found()
    {
        _clinicContext.Setup(c => c.GetUserId()).Returns(Auth0Sub);
        _userRepository
            .Setup(r => r.GetByAuth0SubAsync(Auth0Sub, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await CreateHandler().Handle(CreateQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _patientRepository.Verify(r => r.CountByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
