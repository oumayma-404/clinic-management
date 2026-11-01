using ClinicManagement.API.BackgroundJobs;
using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ClinicManagement.UnitTests.Api;

/// <summary>
/// sms-rappels-tunisie-sms — a reminder's text is written at SEND time from the live appointment, so it always states
/// the current moment, acts and wording; consent and the Tunisian-mobile rule are asked again at send time; a row
/// whose visit moved is caught on the moment it recorded, not by parsing its body.
/// </summary>
public class NotificationJobRenderAtSendTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class RecordingSender : IReminderChannelSender
    {
        public RecordingSender(NotificationType channel) => Channel = channel;

        public NotificationType Channel { get; }
        public int Calls { get; private set; }
        public string? LastMessage { get; private set; }
        public string? LastPhone { get; private set; }

        public Task<ReminderSendResult> SendAsync(
            string phoneE164, string message, ResolvedReminderSettings settings, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastMessage = message;
            LastPhone = phoneE164;
            return Task.FromResult(ReminderSendResult.Sent);
        }
    }

    private sealed class Harness
    {
        public Harness(Appointment appointment, Patient patient, Notification row, string? template = null)
        {
            Row = row;
            Sms = new RecordingSender(NotificationType.SMS);
            WhatsApp = new RecordingSender(NotificationType.WhatsApp);

            var notifications = new Mock<INotificationRepository>();
            notifications.Setup(r => r.GetDueForDispatchAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { row });
            notifications.Setup(r => r.GetBlockedForReviewAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<Notification>());
            notifications.Setup(r => r.UpdateAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var patients = new Mock<IPatientRepository>();
            patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

            var appointments = new Mock<IAppointmentRepository>();
            appointments.Setup(r => r.GetByIdAsync(appointment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(appointment);

            var probe = new Mock<IInternetProbe>();
            probe.Setup(p => p.IsInternetReachableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

            var settings = new Mock<IReminderSettingsProvider>();
            settings.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ResolvedReminderSettings
                {
                    EnabledChannels = [NotificationType.SMS, NotificationType.WhatsApp],
                    MessageTemplateBody = template,
                });

            var clinics = new Mock<IClinicRepository>();
            clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(ClinicId, "Cabinet El Menzah"));

            var policy = new Mock<ISubscriptionPolicy>();
            policy.Setup(p => p.RequiresSubscription).Returns(false);

            Job = new NotificationJob(
                notifications.Object, patients.Object, appointments.Object, new Mock<IUnitOfWork>().Object, probe.Object,
                settings.Object, new ConfigurationBuilder().Build(), new IReminderChannelSender[] { Sms, WhatsApp },
                new Mock<INotificationGenerator>().Object, policy.Object, new Mock<IClinicSubscriptionRepository>().Object,
                new Mock<IVendorMessagingAvailability>().Object, new Mock<IMessagingAllowanceRepository>().Object,
                new Mock<IClinicReminderSettingsRepository>().Object, clinics.Object,
                new Mock<IAuditActorProvider>().Object, new Mock<ITenantScope>().Object,
                ClinicManagement.UnitTests.Common.TestFence.None,
                NullLogger<NotificationJob>.Instance);
        }

        public Notification Row { get; }
        public RecordingSender Sms { get; }
        public RecordingSender WhatsApp { get; }
        public NotificationJob Job { get; }
    }

    // Tomorrow, 14:30 in Tunis.
    private static DateTime Tomorrow1430() =>
        ClinicClock.ToUtc(ClinicClock.ClinicToday().AddDays(1).AddHours(14).AddMinutes(30));

    private static Appointment AppointmentAt(DateTime utc, Guid patientId, params string[] acts)
    {
        var appointment = new Appointment(Guid.NewGuid(), ClinicId, patientId, null, utc, TimeSpan.FromMinutes(30));
        if (acts.Length > 0)
        {
            appointment.SetProcedures(acts.Select(a => new AppointmentProcedureInput(Guid.NewGuid(), a, 15, null, null, null)));
        }

        return appointment;
    }

    private static Patient Patient(string phone = "20 123 456") =>
        new(Guid.NewGuid(), ClinicId, "Sami", "Ben Ali", new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
            phoneNumber: new PhoneNumber(phone));

    private static Notification RowFor(
        NotificationType channel, Appointment appointment, Patient patient, DateTime? queuedFor, string body = "aperçu") =>
        new(Guid.NewGuid(), channel, "Rappel de rendez-vous", body, DateTime.UtcNow.AddMinutes(-1),
            appointment.Id, patient.Id, ClinicId, queuedFor);

    [Fact]
    public async Task The_Text_Is_Written_From_The_Live_Appointment_And_Recorded_On_The_Row()
    {
        var patient = Patient();
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id, "Détartrage", "Obturation");
        var row = RowFor(NotificationType.SMS, appointment, patient, appointment.AppointmentDateTime);
        var h = new Harness(appointment, patient, row, "{patient} : {acte} le {date} chez {clinic}.");

        await h.Job.ProcessPendingNotifications();

        var expected = $"Sami Ben Ali : Détartrage + Obturation le {ReminderMessage.FormatAppointmentMoment(appointment.AppointmentDateTime)} chez Cabinet El Menzah.";
        Assert.Equal(expected, h.Sms.LastMessage);
        Assert.Equal(expected, row.Message);
        Assert.Equal(NotificationStatus.Sent, row.Status);
    }

    [Fact]
    public async Task A_Visit_That_Moved_After_Queueing_Is_Not_Announced()
    {
        var patient = Patient();
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id);
        var row = RowFor(NotificationType.SMS, appointment, patient, queuedFor: appointment.AppointmentDateTime.AddHours(-3));
        var h = new Harness(appointment, patient, row);

        await h.Job.ProcessPendingNotifications();

        Assert.Equal(0, h.Sms.Calls);
        Assert.Equal(NotificationStatus.Failed, row.Status);
    }

    [Fact]
    public async Task A_Refusal_Recorded_After_Queueing_Stops_The_Send()
    {
        var patient = Patient();
        patient.SetReminderConsent(
            PatientReminderConsent.Refused, new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc), "reception@cabinet.tn");
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id);
        var row = RowFor(NotificationType.SMS, appointment, patient, appointment.AppointmentDateTime);
        var h = new Harness(appointment, patient, row);

        await h.Job.ProcessPendingNotifications();

        Assert.Equal(0, h.Sms.Calls);
        Assert.Equal(NotificationStatus.Failed, row.Status);
    }

    [Fact]
    public async Task An_Sms_For_A_Patient_With_Only_A_Landline_Is_Not_Sent_And_Says_Why()
    {
        var patient = Patient("71 123 456");
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id);
        var row = RowFor(NotificationType.SMS, appointment, patient, appointment.AppointmentDateTime);
        var h = new Harness(appointment, patient, row);

        await h.Job.ProcessPendingNotifications();

        Assert.Equal(0, h.Sms.Calls);
        Assert.Equal(NotificationStatus.Failed, row.Status);
        Assert.Contains("mobile tunisien", row.ErrorMessage);
    }

    [Fact]
    public async Task The_Sms_Goes_To_The_Extra_Mobile_When_The_Primary_Is_A_Landline()
    {
        var patient = Patient("71 123 456");
        patient.SetAdditionalPhoneNumbers([new PatientPhone("98 765 432")]);
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id);
        var row = RowFor(NotificationType.SMS, appointment, patient, appointment.AppointmentDateTime);
        var h = new Harness(appointment, patient, row);

        await h.Job.ProcessPendingNotifications();

        Assert.Equal("+21698765432", h.Sms.LastPhone);
    }

    [Fact]
    public async Task WhatsApp_Receives_The_Text_With_Its_Lines_Joined()
    {
        var patient = Patient();
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id);
        var row = RowFor(NotificationType.WhatsApp, appointment, patient, appointment.AppointmentDateTime);
        var h = new Harness(appointment, patient, row, "Rappel le {date}\nMerci de venir en avance.");

        await h.Job.ProcessPendingNotifications();

        Assert.DoesNotContain('\n', h.WhatsApp.LastMessage!);
        Assert.Contains(" · Merci de venir en avance.", h.WhatsApp.LastMessage);
    }

    /// <summary>A row queued before this change keeps its frozen body — no column, so no re-render.</summary>
    [Fact]
    public async Task A_Legacy_Row_Sends_The_Body_It_Was_Queued_With()
    {
        var patient = Patient();
        var appointment = AppointmentAt(Tomorrow1430(), patient.Id, "Détartrage");
        var row = RowFor(NotificationType.SMS, appointment, patient, queuedFor: null, body: "Rappel : Sami, a bientot.");
        var h = new Harness(appointment, patient, row, "{patient} {acte} {date}");

        await h.Job.ProcessPendingNotifications();

        Assert.Equal("Rappel : Sami, a bientot.", h.Sms.LastMessage);
    }
}
