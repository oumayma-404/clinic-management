using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// The minutely pass that moves a visit to « En cours » once its own slot has begun.
///
/// <para><b>Every instant here is a fixed literal.</b> The whole behaviour is a clock boundary, which is why the
/// job takes « now » as a parameter — a fixture built from <c>DateTime.UtcNow</c> agrees with a clock-reading
/// implementation by construction and additionally passes or fails depending on when the suite runs
/// (<c>ClinicClockTests</c>' standing lesson).</para>
///
/// <para><b>The selection itself is the repository's</b>, and half of it is SQL, so this class holds what the
/// repository cannot: that the pass asks for the right window, only ever moves a visit <i>forward</i>, saves and
/// broadcasts per clinic, and survives one clinic failing. The window predicate is proven separately by
/// <c>AppointmentProgressQueryTranslationTests</c>.</para>
/// </summary>
public class AppointmentProgressJobTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 14, 16, 0, DateTimeKind.Utc);
    private static readonly Guid ClinicA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ClinicB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>The user's own case: booked 14:00→15:00, and it is 14:16.</summary>
    private static Appointment RunningNow(Guid clinicId) =>
        new(Guid.NewGuid(), clinicId, Guid.NewGuid(), null,
            new DateTime(2026, 8, 14, 14, 0, 0, DateTimeKind.Utc), TimeSpan.FromHours(1));

    private sealed class Harness
    {
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public Mock<IAuditActorProvider> AuditActor { get; } = new();

        // The real scope, not a mock: `UseSystemWide` is the declaration the query filters refuse without, and a
        // mock would accept a job that never made it.
        public TenantScope TenantScope { get; } = new(NullLogger<TenantScope>.Instance);

        public TimeSpan? LongestVisitAskedFor { get; private set; }

        public Harness(params Appointment[] running)
        {
            Appointments
                .Setup(r => r.GetRunningNotStartedAsync(Now, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .Callback<DateTime, TimeSpan, CancellationToken>((_, window, _) => LongestVisitAskedFor = window)
                .ReturnsAsync(running);
        }

        public AppointmentProgressJob Job() => new(
            Appointments.Object,
            UnitOfWork.Object,
            Realtime.Object,
            AuditActor.Object,
            TenantScope,
            NullLogger<AppointmentProgressJob>.Instance);
    }

    // The headline: a visit whose slot contains this minute is started.
    [Fact]
    public async Task A_Visit_Whose_Slot_Has_Begun_Is_Moved_To_In_Progress()
    {
        var appointment = RunningNow(ClinicA);
        var harness = new Harness(appointment);

        await harness.Job().StartRunningAppointments(Now);

        Assert.Equal(AppointmentStatus.InProgress, appointment.Status);
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // The window is asked for with a real span. A zero or default would silently return nothing every tick, which
    // is indistinguishable from « no visit is running » on every screen.
    [Fact]
    public async Task The_Pass_Asks_For_A_Window_Wide_Enough_To_Hold_A_Visit()
    {
        var harness = new Harness();

        await harness.Job().StartRunningAppointments(Now);

        Assert.NotNull(harness.LongestVisitAskedFor);
        Assert.True(harness.LongestVisitAskedFor >= TimeSpan.FromHours(8));
    }

    // [US-2][R-1] Appointment is clinic-filtered and this pass covers every clinic. Without the declaration it
    // reads nothing anywhere and logs a clean run.
    [Fact]
    public async Task The_Pass_Declares_A_Cross_Clinic_Scope()
    {
        var harness = new Harness();

        await harness.Job().StartRunningAppointments(Now);

        Assert.Equal(TenantScopeKind.SystemWide, harness.TenantScope.Kind);
        Assert.False(string.IsNullOrWhiteSpace(harness.TenantScope.SystemWideReason));
    }

    // [I6] …and names itself, or every row it writes reads « Tâche automatique » with no clue which pass wrote it.
    [Fact]
    public async Task The_Pass_Names_Itself_As_The_Audit_Actor()
    {
        var harness = new Harness();

        await harness.Job().StartRunningAppointments(Now);

        harness.AuditActor.Verify(a => a.RunAs(nameof(AppointmentProgressJob)), Times.Once);
    }

    // A status the domain refuses is skipped rather than thrown through. The repository predicate already
    // excludes these, so this is the guard on the two staying in agreement — a widened read must not take the
    // whole clinic's batch down with an InvalidOperationException.
    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task A_Visit_The_Domain_Will_Not_Start_Is_Skipped_Rather_Than_Thrown_Through(AppointmentStatus status)
    {
        var appointment = RunningNow(ClinicA);
        switch (status)
        {
            case AppointmentStatus.Completed: appointment.Complete(); break;
            case AppointmentStatus.Cancelled: appointment.Cancel("motif"); break;
            case AppointmentStatus.NoShow: appointment.MarkAsNoShow(); break;
        }

        var harness = new Harness(appointment);

        await harness.Job().StartRunningAppointments(Now);

        Assert.Equal(status, appointment.Status);
        // Nothing changed, so nothing is saved and no browser is told to refetch over a no-op.
        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // An already-started visit is left alone — `Start()` is a no-op there, and saving anyway would write an audit
    // row a minute, for ever, for every visit in progress.
    [Fact]
    public async Task An_Already_Started_Visit_Costs_No_Save()
    {
        var appointment = RunningNow(ClinicA);
        appointment.Start();
        var harness = new Harness(appointment);

        await harness.Job().StartRunningAppointments(Now);

        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // An empty pass — the ordinary case, minute after minute — touches nothing at all.
    [Fact]
    public async Task A_Pass_With_Nothing_Running_Saves_Nothing_And_Broadcasts_Nothing()
    {
        var harness = new Harness();

        await harness.Job().StartRunningAppointments(Now);

        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // The broadcast carries the key the appointment COMMANDS emit. A wrong key is a signal nobody listens for,
    // which on screen is indistinguishable from the job not running at all.
    [Fact]
    public async Task Each_Clinic_Is_Told_Its_Appointments_Changed()
    {
        var harness = new Harness(RunningNow(ClinicA));

        await harness.Job().StartRunningAppointments(Now);

        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicA, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // One save and one broadcast PER CLINIC, not one for the batch: a clinic must never be told to refetch
    // because another clinic's visit started.
    [Fact]
    public async Task Two_Clinics_Are_Saved_And_Broadcast_Separately()
    {
        var harness = new Harness(RunningNow(ClinicA), RunningNow(ClinicB));

        await harness.Job().StartRunningAppointments(Now);

        harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicA, "appointments", It.IsAny<CancellationToken>()), Times.Once);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicB, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // One clinic's failure must not stop the others. Without this the first refused save would silently leave
    // every later clinic on the list unstarted, every minute, with one log line to show for it.
    [Fact]
    public async Task One_Clinic_Failing_Does_Not_Stop_The_Rest()
    {
        var first = RunningNow(ClinicA);
        var second = RunningNow(ClinicB);
        var harness = new Harness(first, second);

        var saves = 0;
        harness.UnitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                saves++;
                return saves == 1
                    ? Task.FromException<int>(new InvalidOperationException("boom"))
                    : Task.FromResult(1);
            });

        await harness.Job().StartRunningAppointments(Now);

        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicB, "appointments", It.IsAny<CancellationToken>()), Times.Once);
        harness.Realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicA, "appointments", It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(AppointmentStatus.InProgress, second.Status);
    }
}
