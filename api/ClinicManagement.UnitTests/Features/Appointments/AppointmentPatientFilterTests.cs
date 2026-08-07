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

        return new GetAppointmentsQueryHandler(repo.Object, invoices.Object, users.Object, context.Object);
    }
}
