using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// A séance retirée is gone from <c>GET /api/appointments</c> — the agenda and the patient's history alike.
///
/// <para>The mark already left the dashboard's figures when it shipped, because the worklist was its only caller
/// and a worklist row is not drawn on a calendar. « Supprimer (créé par erreur) » gives it a second caller, and a
/// row the user was told they deleted that keeps drawing itself on the agenda is the whole feature failing while
/// every figure it touches is right — no error anywhere, and the practice concludes the button does nothing.</para>
/// </summary>
public class DisregardedVisitsLeaveTheAgendaTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task A_Retired_Séance_Is_Not_In_The_Agenda_And_Its_Neighbours_Still_Are()
    {
        var kept = AppointmentAt(9);
        var retired = AppointmentAt(10);
        retired.Disregard("local|someone", DateTime.UtcNow);

        var result = await Handler(kept, retired).Handle(new GetAppointmentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { kept.Id }, result.Value!.Select(a => a.Id));
    }

    private static Appointment AppointmentAt(int hourUtc) =>
        new(Guid.NewGuid(), ClinicId, Guid.NewGuid(), doctorId: null,
            new DateTime(2026, 8, 14, hourUtc, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(30));

    private static GetAppointmentsQueryHandler Handler(params Appointment[] appointments)
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(
                ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointments);

        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var invoices = new Mock<IInvoiceRepository>();
        invoices.Setup(r => r.GetAppointmentLinksAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(Guid, Guid, string?, InvoiceStatus)>());

        var doctors = new Mock<IDoctorRepository>();
        doctors.Setup(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Doctor>());

        return new GetAppointmentsQueryHandler(
            repo.Object, invoices.Object, users.Object, doctors.Object, context.Object);
    }
}
