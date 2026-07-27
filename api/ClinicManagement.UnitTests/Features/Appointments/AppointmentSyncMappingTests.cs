using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Application.Features.Appointments.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// Verifies the additive <c>IsSyncedToGoogle</c> field is mapped (derived from
/// <c>GoogleCalendarEventId != null</c>) consistently across all four appointment handlers — R-5
/// guards against the badge being wrong because one mapper was missed (US-3, FR-D4).
/// </summary>
public class AppointmentSyncMappingTests
{
    private static readonly Guid ClinicId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private static Appointment NewAppointment(string? googleEventId)
    {
        var appointment = new Appointment(
            Guid.NewGuid(),
            ClinicId,
            patientId: null, // busy slot → no patient lookup needed, no domain event
            doctorId: null,
            appointmentDateTime: DateTime.UtcNow.AddDays(1),
            duration: TimeSpan.FromMinutes(30),
            doctorName: "Dr Test",
            notes: null,
            recurringAppointmentId: null,
            // The fixture carries a booked act on purpose. The bare `new UpdateAppointmentCommand { Id }`
            // below used to pass only because this was null — with an act present, the pre-tri-state handler
            // nulled it (an omitted procedureTypeId bound to null and read as "different from current"), so
            // this test was pinning the data-loss defect rather than the mapping it claims to cover.
            procedureTypeId: Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
            procedureDurationMinutes: 45,
            procedureColorHex: "#4F83CC");

        if (googleEventId is not null)
        {
            appointment.SetGoogleCalendarEventId(googleEventId);
        }

        return appointment;
    }

    // ---- GetAppointmentQuery -------------------------------------------------

    [Theory]
    [InlineData(null, false)]
    [InlineData("gcal-evt-1", true)]
    public async Task GetAppointment_Maps_IsSyncedToGoogle(string? eventId, bool expected)
    {
        var appointment = NewAppointment(eventId);
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);

        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var handler = new GetAppointmentQueryHandler(repo.Object, users.Object, context.Object);
        var result = await handler.Handle(new GetAppointmentQuery { Id = appointment.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.IsSyncedToGoogle);
    }

    // ---- GetAppointmentsQuery ------------------------------------------------

    [Fact]
    public async Task GetAppointments_Maps_IsSyncedToGoogle_Per_Appointment()
    {
        var synced = NewAppointment("gcal-evt-2");
        var unsynced = NewAppointment(null);

        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { synced, unsynced });

        var handler = new GetAppointmentsQueryHandler(repo.Object, users.Object, context.Object);
        var result = await handler.Handle(new GetAppointmentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Single(a => a.Id == synced.Id).IsSyncedToGoogle);
        Assert.False(result.Value!.Single(a => a.Id == unsynced.Id).IsSyncedToGoogle);
    }

    // ---- CreateAppointmentCommand -------------------------------------------

    [Fact]
    public async Task CreateAppointment_Maps_IsSyncedToGoogle_False_For_New_Appointment()
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var handler = new CreateAppointmentCommandHandler(
            new Mock<IAppointmentRepository>().Object,
            new Mock<IPatientRepository>().Object,
            new Mock<IDoctorRepository>().Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<ITreatmentPlanRepository>().Object,
            users.Object,
            context.Object,
            new Mock<IUnitOfWork>().Object,
            new Mock<INotificationGenerator>().Object,
            new Mock<IReminderScheduler>().Object,
            new Mock<IAppointmentGoogleSyncDispatcher>().Object, NullLogger<CreateAppointmentCommandHandler>.Instance);

        var result = await handler.Handle(
            new CreateAppointmentCommand { AppointmentDateTime = DateTime.UtcNow.AddDays(1), DurationMinutes = 30 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        // A freshly created appointment has no Google event yet.
        Assert.False(result.Value!.IsSyncedToGoogle);
    }

    // ---- UpdateAppointmentCommand -------------------------------------------

    [Theory]
    [InlineData(null, false)]
    [InlineData("gcal-evt-3", true)]
    public async Task UpdateAppointment_Maps_IsSyncedToGoogle(string? eventId, bool expected)
    {
        var appointment = NewAppointment(eventId);
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);

        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<IDoctorRepository>().Object,
            new Mock<ITreatmentPlanRepository>().Object,
            clinicResolver.Object,
            new Mock<IClinicContext>().Object,
            new Mock<IUnitOfWork>().Object,
            new Mock<IAppointmentGoogleSyncDispatcher>().Object,
            new Mock<INotificationGenerator>().Object,
            new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateAppointmentCommand { Id = appointment.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.IsSyncedToGoogle);

        // A bare update sends no procedureTypeId, so the booked act must survive it untouched.
        Assert.Equal(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), appointment.ProcedureTypeId);
        Assert.Equal(45, appointment.ProcedureDurationMinutes);
    }

    /// <summary>
    /// Minimal scope factory so the handler's fire-and-forget Google sync can resolve its services
    /// without touching a real DI container or the network.
    /// </summary>
    private static IServiceScopeFactory ScopeFactory()
    {
        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(IGoogleCalendarSyncService)))
            .Returns(new Mock<IGoogleCalendarSyncService>().Object);
        provider.Setup(p => p.GetService(typeof(ILogger<UpdateAppointmentCommandHandler>)))
            .Returns(NullLogger<UpdateAppointmentCommandHandler>.Instance);
        // The create path resolves its own logger + the connectivity probe from the scope (probe reachable
        // so the fire-and-forget sync proceeds to the no-op mock sync service, matching prior behavior).
        provider.Setup(p => p.GetService(typeof(ILogger<CreateAppointmentCommandHandler>)))
            .Returns(NullLogger<CreateAppointmentCommandHandler>.Instance);
        var probe = new Mock<IInternetProbe>();
        probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        provider.Setup(p => p.GetService(typeof(IInternetProbe))).Returns(probe.Object);

        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(provider.Object);

        var factory = new Mock<IServiceScopeFactory>();
        factory.Setup(f => f.CreateScope()).Returns(scope.Object);
        return factory.Object;
    }
}
