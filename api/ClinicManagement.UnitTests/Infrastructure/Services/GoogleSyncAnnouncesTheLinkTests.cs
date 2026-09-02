using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// Linking an appointment to its Google event <b>tells the clinic's screens</b>.
///
/// <para><b>The defect this holds.</b> `AppointmentGoogleSyncDispatcher` pushes to Google fire-and-forget, in a
/// fresh DI scope, <i>after</i> the command that created the appointment has already answered. So the save that
/// sets <c>GoogleCalendarEventId</c> — the only write in the product that flips
/// <c>AppointmentDto.IsSyncedToGoogle</c> — happens outside MediatR, and
/// <c>RealtimeBroadcastBehavior</c> is a <b>pipeline</b> behaviour over commands: it never sees a raw repository
/// save inside a service. Nothing told the agenda, so a séance that was in the practice's Google calendar kept
/// the « non synchronisé » badge until somebody reloaded the page — the badge truthful about the response it was
/// rendered from and false about the world, with a « Envoyer vers Google Agenda » button beside it offering to
/// re-push what was already pushed.</para>
///
/// <para>⚠️ The update branch must stay silent. It rewrites the Google event's fields and leaves the stored id
/// alone, so no client is rendering anything that changed — a broadcast there is a refetch of the whole agenda
/// for every edit of every synced visit.</para>
/// </summary>
public class GoogleSyncAnnouncesTheLinkTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PatientId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTime At = new(2026, 9, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IGoogleCalendarService> _google = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IGoogleTokenProtector> _protector = new();
    private readonly Mock<IRealtimeNotifier> _realtime = new();

    private Appointment Wire(string? existingEventId)
    {
        var appointment = new Appointment(
            Guid.NewGuid(), ClinicId, PatientId, doctorId: null, At, TimeSpan.FromMinutes(45));
        if (existingEventId is not null)
        {
            appointment.SetGoogleCalendarEventId(existingEventId);
        }

        _appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(appointment);
        _patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(
                PatientId, ClinicId, "Jean", "Dupont", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M"));

        var clinic = new Clinic(ClinicId, "Cabinet Test", code: "CODE01");
        clinic.SetGoogleCalendarConnection("protected-blob", calendarId: "primary");
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);

        var token = "refresh-token";
        _protector.Setup(p => p.TryUnprotect("protected-blob", out token)).Returns(true);

        return appointment;
    }

    private GoogleCalendarSyncService Service() => new(
        _google.Object, _appointments.Object, _patients.Object, _clinics.Object, _uow.Object,
        _protector.Object, _realtime.Object, NullLogger<GoogleCalendarSyncService>.Instance);

    [Fact]
    public async Task Creating_The_Link_Broadcasts_So_The_Badge_Clears_Without_A_Reload()
    {
        var appointment = Wire(existingEventId: null);
        _google.Setup(g => g.CreateEventAsync(
                It.IsAny<GoogleCalendarConnection>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-google-event-id");

        await Service().SyncAppointmentToGoogleCalendarAsync(appointment.Id, CancellationToken.None);

        Assert.Equal("new-google-event-id", appointment.GoogleCalendarEventId);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // The resource key is the agenda's own — `clinic-hub.ts` declares « appointments », and
        // `RealtimeResourceResolverTests` holds the two sets equal, so a typo here is a signal into the void.
        _realtime.Verify(
            r => r.NotifyEntityChangedAsync(ClinicId, "appointments", It.IsAny<CancellationToken>()), Times.Once);
    }

    // A failed broadcast must never turn a completed sync into an error: the link is already committed, and
    // realtime is additive by its own contract. The worst case is the stale badge that existed before.
    [Fact]
    public async Task A_Failed_Broadcast_Does_Not_Fail_The_Sync()
    {
        var appointment = Wire(existingEventId: null);
        _google.Setup(g => g.CreateEventAsync(
                It.IsAny<GoogleCalendarConnection>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-google-event-id");
        _realtime.Setup(r => r.NotifyEntityChangedAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        await Service().SyncAppointmentToGoogleCalendarAsync(appointment.Id, CancellationToken.None);

        Assert.Equal("new-google-event-id", appointment.GoogleCalendarEventId);
    }

    // ⚠️ The other half of the rule. Re-syncing an already-linked visit rewrites the Google event and changes
    // nothing a client renders, so it must not make every edit refetch the whole agenda.
    [Fact]
    public async Task Updating_An_Existing_Event_Broadcasts_Nothing()
    {
        var appointment = Wire(existingEventId: "already-linked");

        await Service().SyncAppointmentToGoogleCalendarAsync(appointment.Id, CancellationToken.None);

        _google.Verify(g => g.UpdateEventAsync(
            It.IsAny<GoogleCalendarConnection>(), "already-linked", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _realtime.Verify(
            r => r.NotifyEntityChangedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
