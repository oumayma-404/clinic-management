using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// Hardening pass — cross-clinic isolation for the appointment commands: an appointment/patient/
/// procedure-type from another clinic must read as "not found" (AC-1, AC-2).
/// </summary>
public class AppointmentTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Appointment Appointment(Guid clinicId) => new(
        Guid.NewGuid(),
        clinicId,
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

    private static Patient Patient(Guid clinicId) => new(
        Guid.NewGuid(),
        clinicId,
        "Jean",
        "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "M",
        new Email("jean.dupont@example.com"),
        new PhoneNumber("+21620123456"));

    private static ProcedureType ProcedureType(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, "Cleaning", 30, new ColorHex("#4F83CC"));

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

    // ---- UpdateAppointmentCommand (AC-1) ------------------------------------

    [Fact]
    public async Task UpdateAppointment_Should_Return_NotFound_For_Other_Clinic()
    {
        var foreign = Appointment(OtherClinicId);
        var repo = new Mock<IAppointmentRepository>();
        repo.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);
        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
        var uow = new Mock<IUnitOfWork>();

        var handler = new UpdateAppointmentCommandHandler(
            repo.Object,
            new Mock<IProcedureTypeRepository>().Object,
            clinicResolver.Object,
            new Mock<IClinicContext>().Object,
            uow.Object,
            ScopeFactory(),
            new Mock<INotificationGenerator>().Object,
            new Mock<IReminderScheduler>().Object,
            NullLogger<UpdateAppointmentCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateAppointmentCommand { Id = foreign.Id, Notes = "hacked" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        repo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- CreateAppointmentCommand (AC-2) ------------------------------------

    private static (CreateAppointmentCommandHandler handler, Mock<IAppointmentRepository> appts, Mock<IUnitOfWork> uow)
        CreateHandler(Mock<IPatientRepository> patients, Mock<IProcedureTypeRepository> procedures)
    {
        var user = User.CreateLocalUser(ClinicId, "secretary", "sec@clinic.com", "HASH", "Sec");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        var appts = new Mock<IAppointmentRepository>();
        var uow = new Mock<IUnitOfWork>();

        var handler = new CreateAppointmentCommandHandler(
            appts.Object,
            patients.Object,
            procedures.Object,
            users.Object,
            context.Object,
            uow.Object,
            new Mock<INotificationGenerator>().Object,
            new Mock<IReminderScheduler>().Object,
            ScopeFactory());
        return (handler, appts, uow);
    }

    [Fact]
    public async Task CreateAppointment_Should_Fail_For_Other_Clinic_Patient()
    {
        var patients = new Mock<IPatientRepository>();
        var foreignPatient = Patient(OtherClinicId);
        patients.Setup(r => r.GetByIdAsync(foreignPatient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignPatient);
        var (handler, appts, uow) = CreateHandler(patients, new Mock<IProcedureTypeRepository>());

        var result = await handler.Handle(
            new CreateAppointmentCommand
            {
                PatientId = foreignPatient.Id,
                AppointmentDateTime = DateTime.UtcNow.AddDays(1),
                DurationMinutes = 30
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        appts.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAppointment_Should_Fail_For_Other_Clinic_ProcedureType()
    {
        var procedures = new Mock<IProcedureTypeRepository>();
        var foreignProcedure = ProcedureType(OtherClinicId);
        procedures.Setup(r => r.GetByIdAsync(foreignProcedure.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignProcedure);
        var (handler, appts, uow) = CreateHandler(new Mock<IPatientRepository>(), procedures);

        var result = await handler.Handle(
            new CreateAppointmentCommand
            {
                ProcedureTypeId = foreignProcedure.Id,
                AppointmentDateTime = DateTime.UtcNow.AddDays(1),
                DurationMinutes = 30
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        appts.Verify(r => r.AddAsync(It.IsAny<Appointment>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
