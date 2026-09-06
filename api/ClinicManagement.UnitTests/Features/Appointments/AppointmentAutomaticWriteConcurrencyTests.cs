using System.Reflection;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure;
using ClinicManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// A write <b>nobody made</b> must stop spending the concurrency token somebody is holding.
///
/// <para><b>The production incident, 2026-09-05.</b> One clinic, one user, one browser session. The edit dialog
/// read the appointment at 15:50:04; <c>AppointmentProgressJob</c> — which runs every minute — advanced the visit
/// <c>Scheduled → InProgress</c> at 15:50:05, because 15:50 was its own start minute; the user pressed Enregistrer
/// at 15:50:32 and was told « cet enregistrement a été modifié par quelqu'un d'autre pendant votre saisie ». It had
/// not been. The token is <c>xmin</c>, which is per-<i>row</i>, so a write to any field ages every version anybody
/// holds, and the sentence the 409 carries names a person because a person is the only writer it can imagine.</para>
///
/// <para>The job's own save already skips <c>SetExpectedVersion</c>, with the comment « a user's concurrent edit
/// legitimately wins ». It did not win: the job wrote first, so the user's edit was the one refused. These tests
/// pin the two halves that make that comment true — the marker is maintained by whoever writes, and the handler
/// honours it — and, as importantly, the half that must NOT move: a colleague's edit is still a 409.</para>
/// </summary>
public class AppointmentAutomaticWriteConcurrencyTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime At = new(2026, 9, 5, 14, 50, 0, DateTimeKind.Utc);

    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public AppointmentAutomaticWriteConcurrencyTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));
    }

    // ------------------------------------------------------------------ the marker's rule

    // The version a person last saw is what an automatic write records — that is the whole point: it is the token
    // the form in front of them is holding.
    [Fact]
    public void An_Automatic_Write_Records_The_Version_A_Person_Was_Holding()
    {
        Assert.Equal(7u, AutomaticWriteInterceptor.MarkerAfterWrite(automatic: true, current: null, version: 7u));
    }

    // A RUN of them keeps pointing at the first, and this is the routine case rather than an edge one: a visit
    // advances Scheduled → InProgress at its start minute and InProgress → AwaitingClosure at its end. Recording
    // the second would forgive only the last minute of a stale form's life — the 15:50:04 form would still 409.
    [Fact]
    public void A_Run_Of_Automatic_Writes_Still_Points_At_The_Version_A_Person_Saw()
    {
        Assert.Equal(7u, AutomaticWriteInterceptor.MarkerAfterWrite(automatic: true, current: 7u, version: 9u));
    }

    // The guarantee that keeps the token worth having. A person's write means every older version is now somebody's
    // work, so the forgiveness ends there.
    [Fact]
    public void A_Persons_Write_Ends_The_Forgiveness()
    {
        Assert.Null(AutomaticWriteInterceptor.MarkerAfterWrite(automatic: false, current: 7u, version: 9u));
        Assert.Null(AutomaticWriteInterceptor.MarkerAfterWrite(automatic: false, current: null, version: 9u));
    }

    // The discriminator is the audit actor, and it must stay the audit actor: a background job is `job|<name>`,
    // and the vendor's console and an archive restore are deliberately NOT automatic — both have a person behind
    // them, and forgiving their writes would let a stale form overwrite one.
    [Fact]
    public void Only_A_Process_Counts_As_Automatic()
    {
        Assert.True(AuditActor.Process(nameof(ClinicManagement.API.BackgroundJobs.AppointmentProgressJob)).IsProcess);
        Assert.True(AuditActor.Unknown.IsProcess);
        Assert.False(AuditActor.Console(Guid.NewGuid()).IsProcess);
        Assert.False(new AuditActor("local|" + Guid.NewGuid(), "eya@example.tn").IsProcess);
        Assert.False(new AuditActor("local|" + Guid.NewGuid(), null).AsRestore().IsProcess);
    }

    // ------------------------------------------------------------------ what the aggregate accepts

    [Fact]
    public void The_Current_Version_Is_Always_Accepted()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);

        Assert.True(appointment.AcceptsExpectedVersion(9u));
    }

    [Fact]
    public void The_Version_An_Automatic_Run_Superseded_Is_Accepted()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);
        SetMarker(appointment, 7u);

        Assert.True(appointment.AcceptsExpectedVersion(7u));
    }

    // Neither a version from before the last human write, nor any version at all once the marker is clear. This is
    // the test that fails if the fix is ever widened into « stale is fine ».
    [Fact]
    public void Any_Other_Version_Is_Still_A_Conflict()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);
        SetMarker(appointment, 7u);

        Assert.False(appointment.AcceptsExpectedVersion(5u));

        SetMarker(appointment, null);
        Assert.False(appointment.AcceptsExpectedVersion(7u));
    }

    // ------------------------------------------------------------------ what the handler does with it

    // The incident, end to end: the form holds 7, the job has moved the row to 9, and the save goes through —
    // checked against 9, so a colleague writing between this handler's read and its save is still caught.
    [Fact]
    public async Task A_Save_Against_A_Version_Only_The_Job_Superseded_Is_Not_A_Conflict()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);
        SetMarker(appointment, 7u);

        var result = await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "Prévoir une radio", Version = 7u }, default);

        Assert.True(result.IsSuccess);
        _uow.Verify(u => u.SetExpectedVersion(appointment, 9u), Times.Once);
    }

    // …and the case the token exists for is untouched: a colleague saved, which cleared the marker, so the stale
    // version reaches `SetExpectedVersion` unchanged and the save is refused by the database.
    [Fact]
    public async Task A_Colleagues_Edit_Is_Still_Checked_Against_The_Version_The_User_Held()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);
        SetMarker(appointment, null);

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "Prévoir une radio", Version = 7u }, default);

        _uow.Verify(u => u.SetExpectedVersion(appointment, 7u), Times.Once);
    }

    // `0` means « not supplied » and skips the check entirely — a decision made in `SetExpectedVersion`, not here.
    // Rewriting it to the current version would look identical and would quietly move that decision.
    [Fact]
    public async Task An_Unsupplied_Version_Is_Passed_Through_Untouched()
    {
        var appointment = Booked();
        SetVersion(appointment, 9u);
        SetMarker(appointment, 7u);

        await CreateHandler().Handle(
            new UpdateAppointmentCommand { Id = appointment.Id, Notes = "Prévoir une radio" }, default);

        _uow.Verify(u => u.SetExpectedVersion(appointment, 0u), Times.Once);
    }

    // ------------------------------------------------------------------ the wiring, which fails silently

    // The marker is maintained by an interceptor and by nothing else, so an unregistered interceptor is a fix that
    // compiles, passes every test above and does nothing at all in production — the shape this repository produces
    // most. Asserted against the options `AddInfrastructure` really builds, and deliberately not against a
    // container this test assembles itself, which would only prove that `AddInterceptors` works.
    [Fact]
    public void The_Interceptor_Is_Wired_Into_The_Real_DbContext_Registration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Enough to resolve the deployment profile and reach the DbContext registration; no connection is
                // ever opened, because building the options does not touch a database.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=none;Username=none;Password=none",
                ["Deployment:Profile"] = "SelfHostedLan",
            })
            .Build());

        using var scope = services.BuildServiceProvider().CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        Assert.Contains(
            options.FindExtension<CoreOptionsExtension>()!.Interceptors!,
            i => i is AutomaticWriteInterceptor);
    }

    // ------------------------------------------------------------------ fixtures

    private UpdateAppointmentCommandHandler CreateHandler() => new(
        _appointments.Object,
        new Mock<IProcedureTypeRepository>().Object,
        new Mock<IDoctorRepository>().Object,
        new Mock<IClinicRepository>().Object,
        new Mock<ITreatmentPlanRepository>().Object,
        _clinicResolver.Object,
        new Mock<IClinicContext>().Object,
        _uow.Object,
        new Mock<IAppointmentGoogleSyncDispatcher>().Object,
        new Mock<INotificationGenerator>().Object,
        new Mock<IReminderScheduler>().Object,
        NullLogger<UpdateAppointmentCommandHandler>.Instance);

    private Appointment Booked()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, null, At, TimeSpan.FromMinutes(30));

        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        return appointment;
    }

    // Both setters are private and EF-managed, so the tests reach them the way `ConcurrencyConflictTests` does.
    // `Version` is looked up on the DECLARING type: private members are not inherited for reflection, so the
    // derived type's PropertyInfo reports no setter at all.
    private static void SetVersion(Appointment appointment, uint version) =>
        typeof(Entity<Guid>)
            .GetProperty(nameof(Entity<Guid>.Version), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(appointment, new object[] { version });

    private static void SetMarker(Appointment appointment, uint? marker) =>
        typeof(Appointment)
            .GetProperty(nameof(Appointment.VersionBeforeAutoAdvance), BindingFlags.Public | BindingFlags.Instance)!
            .GetSetMethod(nonPublic: true)!
            .Invoke(appointment, new object?[] { marker });
}
