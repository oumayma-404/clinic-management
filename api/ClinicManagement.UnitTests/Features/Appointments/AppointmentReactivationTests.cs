using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// fix-appointment-lifecycle #4: a cancelled appointment can be edited/reactivated. The domain's
/// <c>Reschedule</c> throws on a cancelled appointment, so the handler must un-cancel first when the caller
/// sets status back to Scheduled, and must NOT attempt a reschedule (→ 400) when a cancelled appointment
/// stays cancelled but the sent start time differs (e.g. by zeroed seconds).
/// </summary>
public class AppointmentReactivationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // Patient-less "busy slot" appointment so the post-commit notification/reminder paths (gated on a
    // patient) don't fire — keeps these tests focused on the reschedule/status logic.
    private static Appointment CancelledAppointment(DateTime when)
    {
        var appt = new Appointment(Guid.NewGuid(), ClinicId, patientId: null, doctorId: null,
            appointmentDateTime: when, duration: TimeSpan.FromMinutes(30));
        appt.Cancel();
        return appt;
    }

    // Minimal scope factory for the fire-and-forget Google sync: it resolves its logger (outside the try)
    // and the connectivity probe (returns offline → the sync short-circuits before any network work).
    private static IServiceScopeFactory ScopeFactory()
    {
        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(ILogger<UpdateAppointmentCommandHandler>)))
            .Returns(NullLogger<UpdateAppointmentCommandHandler>.Instance);
        provider.Setup(p => p.GetService(typeof(IInternetProbe))).Returns(probe.Object);
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);
        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }

    private static UpdateAppointmentCommandHandler Handler(Appointment appointment)
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        return new UpdateAppointmentCommandHandler(
            repo.Object, new Mock<IProcedureTypeRepository>().Object, clinicResolver.Object,
            new Mock<IClinicContext>().Object, new Mock<IUnitOfWork>().Object, ScopeFactory(),
            new Mock<INotificationGenerator>().Object, new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);
    }

    // [AC-1] Reactivate a cancelled appointment AND move it to a new time in one edit.
    [Fact]
    public async Task Reactivate_Cancelled_And_Move_To_New_Time_Succeeds()
    {
        var appt = CancelledAppointment(new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc));
        var newTime = new DateTime(2026, 8, 2, 14, 0, 0, DateTimeKind.Utc);

        var result = await Handler(appt).Handle(
            new UpdateAppointmentCommand { Id = appt.Id, Status = "Scheduled", AppointmentDateTime = newTime },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Scheduled", result.Value!.Status);
        Assert.Equal(newTime, result.Value.AppointmentDateTime);
    }

    // [AC-1] Reactivate a cancelled appointment at its existing time (status → Scheduled, no date change).
    [Fact]
    public async Task Reactivate_Cancelled_Same_Time_Succeeds()
    {
        var appt = CancelledAppointment(new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc));

        var result = await Handler(appt).Handle(
            new UpdateAppointmentCommand { Id = appt.Id, Status = "Scheduled" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Scheduled", result.Value!.Status);
    }

    // [AC-2] Editing a field of a cancelled appointment (status unchanged) must not 400 on the reschedule
    // guard, even when the sent start time differs only by zeroed seconds — it stays cancelled.
    [Fact]
    public async Task Edit_Cancelled_Field_With_Seconds_Diff_Does_Not_Fail_And_Stays_Cancelled()
    {
        var appt = CancelledAppointment(new DateTime(2026, 8, 1, 10, 30, 45, DateTimeKind.Utc));
        var sameMinuteSecondsZeroed = new DateTime(2026, 8, 1, 10, 30, 0, DateTimeKind.Utc);

        var result = await Handler(appt).Handle(
            new UpdateAppointmentCommand { Id = appt.Id, Notes = "updated note", AppointmentDateTime = sameMinuteSecondsZeroed },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value!.Status);
    }
}
