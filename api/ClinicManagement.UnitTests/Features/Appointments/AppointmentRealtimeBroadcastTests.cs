using ClinicManagement.Application.Common.Interfaces;
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
/// Verifies the appointment create/update handlers broadcast "appointments changed" to the clinic
/// (AC-1) and only <b>after</b> the change is committed — never on a pre-commit failure (Edge Case:
/// broadcast fires only after commit). Cancellation is an update to status=Cancelled, so wiring the
/// update path covers live removals too. Complements <see cref="AppointmentSyncMappingTests"/>, which
/// covers the deferred broadcast behavior noted at implementation time.
/// </summary>
public class AppointmentRealtimeBroadcastTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Appointment NewAppointment()
    {
        return new Appointment(
            Guid.NewGuid(),
            ClinicId,
            patientId: null,
            doctorId: null,
            appointmentDateTime: DateTime.UtcNow.AddDays(1),
            duration: TimeSpan.FromMinutes(30),
            doctorName: "Dr Test",
            notes: null,
            recurringAppointmentId: null,
            procedureTypeId: null,
            procedureDurationMinutes: null,
            procedureColorHex: null);
    }

    // ---- CreateAppointmentCommand -------------------------------------------

    // [AC-1] A committed create broadcasts to the creating user's clinic.
    [Fact]
    public async Task CreateAppointment_Broadcasts_To_Clinic_After_Commit()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var notifier = new Mock<IRealtimeNotifier>();

        var handler = new CreateAppointmentCommandHandler(
            new Mock<IAppointmentRepository>().Object,
            new Mock<IPatientRepository>().Object,
            new Mock<IProcedureTypeRepository>().Object,
            users.Object,
            context.Object,
            new Mock<IUnitOfWork>().Object,
            notifier.Object);

        var result = await handler.Handle(
            new CreateAppointmentCommand { AppointmentDateTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 30 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifier.Verify(n => n.NotifyAppointmentsChangedAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-1][Edge] A create that fails before commit (user not resolved) must NOT broadcast.
    [Fact]
    public async Task CreateAppointment_Does_Not_Broadcast_When_Save_Not_Reached()
    {
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns("local|missing");
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var notifier = new Mock<IRealtimeNotifier>();

        var handler = new CreateAppointmentCommandHandler(
            new Mock<IAppointmentRepository>().Object,
            new Mock<IPatientRepository>().Object,
            new Mock<IProcedureTypeRepository>().Object,
            users.Object,
            context.Object,
            new Mock<IUnitOfWork>().Object,
            notifier.Object);

        var result = await handler.Handle(
            new CreateAppointmentCommand { AppointmentDateTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 30 },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        notifier.Verify(
            n => n.NotifyAppointmentsChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- UpdateAppointmentCommand -------------------------------------------

    // [AC-1] A committed update broadcasts to the appointment's clinic.
    [Fact]
    public async Task UpdateAppointment_Broadcasts_To_Clinic_After_Commit()
    {
        var appointment = NewAppointment();
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        var notifier = new Mock<IRealtimeNotifier>();

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            ScopeFactory(),
            NullLogger<UpdateAppointmentCommandHandler>.Instance,
            notifier.Object);

        var result = await handler.Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "updated" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        notifier.Verify(n => n.NotifyAppointmentsChangedAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-1][Edge] Cancellation is an update to status=Cancelled, so it broadcasts like any other update.
    [Fact]
    public async Task UpdateAppointment_Cancellation_Broadcasts_To_Clinic()
    {
        var appointment = NewAppointment(); // starts Scheduled
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);
        var notifier = new Mock<IRealtimeNotifier>();

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            ScopeFactory(),
            NullLogger<UpdateAppointmentCommandHandler>.Instance,
            notifier.Object);

        var result = await handler.Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Status = "Cancelled" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Cancelled", result.Value!.Status);
        notifier.Verify(n => n.NotifyAppointmentsChangedAsync(ClinicId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-1][Edge] An update that fails before commit (appointment not found) must NOT broadcast.
    [Fact]
    public async Task UpdateAppointment_Does_Not_Broadcast_When_Appointment_Not_Found()
    {
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Appointment?)null);
        var notifier = new Mock<IRealtimeNotifier>();

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<IUnitOfWork>().Object,
            ScopeFactory(),
            NullLogger<UpdateAppointmentCommandHandler>.Instance,
            notifier.Object);

        var result = await handler.Handle(new UpdateAppointmentCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        notifier.Verify(
            n => n.NotifyAppointmentsChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Minimal scope factory so the update handler's fire-and-forget Google sync can resolve its
    /// services without a real DI container or the network (mirrors <see cref="AppointmentSyncMappingTests"/>).
    /// </summary>
    private static IServiceScopeFactory ScopeFactory()
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IGoogleCalendarSyncService)))
            .Returns(new Mock<IGoogleCalendarSyncService>().Object);
        provider.Setup(p => p.GetService(typeof(ILogger<UpdateAppointmentCommandHandler>)))
            .Returns(NullLogger<UpdateAppointmentCommandHandler>.Instance);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
