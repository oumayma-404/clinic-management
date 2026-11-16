using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// <c>GET /api/appointments?patientId=</c> narrows to that patient, and does it in SQL.
///
/// <para>The client had been sending the parameter since the patient page was written and nothing bound it —
/// neither the controller action nor <see cref="GetAppointmentsQuery"/> — so the page received the clinic's whole
/// agenda and its « À compléter » section listed every undocumented visit in the practice. A dropped optional
/// filter is invisible to every other test in the suite: the handler succeeds, the mapping is correct, and only
/// the row count is wrong.</para>
/// </summary>
public class AppointmentPatientFilterTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid PatientId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    [Fact]
    public async Task The_Patient_Filter_Reaches_The_Repository()
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), PatientId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>())
            .Verifiable();

        var result = await Handler(repo).Handle(
            new GetAppointmentsQuery { PatientId = PatientId }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify();
    }

    /// <summary>The agenda reads the whole clinic — a null filter must stay null, not become an empty Guid.</summary>
    [Fact]
    public async Task No_Patient_Filter_Reads_The_Whole_Clinic()
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>())
            .Verifiable();

        var result = await Handler(repo).Handle(new GetAppointmentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify();
    }

    // [cabinet-de-groupe D10] A dentist's agenda keeps the RDV that name no dentist — a « créneau occupé » blocks their
    // chair too — and still hides a colleague's.
    [Fact]
    public async Task A_Dentists_Agenda_Keeps_The_Unassigned_Rows_And_Drops_A_Colleagues()
    {
        var salma = Guid.NewGuid();
        var nadia = Guid.NewGuid();
        var at = new DateTime(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);
        var hers = new Appointment(Guid.NewGuid(), ClinicId, PatientId, salma, at, TimeSpan.FromMinutes(30));
        var block = new Appointment(Guid.NewGuid(), ClinicId, null, null, at.AddHours(3), TimeSpan.FromMinutes(60));
        var colleagues = new Appointment(Guid.NewGuid(), ClinicId, PatientId, nadia, at.AddHours(1), TimeSpan.FromMinutes(30));
        var repo = new Mock<IAppointmentRepository>();
        // The widened read asks for the whole window, then keeps hers and nobody's.
        repo.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { hers, block, colleagues });

        var result = await Handler(repo).Handle(
            new GetAppointmentsQuery { DoctorId = salma, IncludeUnassigned = true }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            new[] { hers.Id, block.Id }.OrderBy(id => id),
            result.Value!.Select(a => a.Id).OrderBy(id => id));
    }

    [Fact]
    public async Task Without_The_Flag_The_Dentist_Filter_Stays_Strict()
    {
        var salma = Guid.NewGuid();
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), salma, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Appointment>())
            .Verifiable();

        var result = await Handler(repo).Handle(new GetAppointmentsQuery { DoctorId = salma }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        repo.Verify();
    }

    private static GetAppointmentsQueryHandler Handler(Mock<IAppointmentRepository> repo)
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        return new GetAppointmentsQueryHandler(
            repo.Object, invoices.Object, users.Object, NoDoctors(), context.Object);
    }
    /// <summary>An empty practitioner roster — these cases assert other columns, not the praticien.</summary>
    private static IDoctorRepository NoDoctors()
    {
        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());
        return doctors.Object;
    }

}
