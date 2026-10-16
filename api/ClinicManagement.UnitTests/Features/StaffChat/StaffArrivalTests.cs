using ClinicManagement.Application.Common;
using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.Services;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>« Signaler l'arrivée » (US-3, FR-5, EC-5/6/15) and a message's patient / appointment link (AC-2.4, FR-3, EC-8).</summary>
public class StaffArrivalTests
{
    private static readonly Guid PatientId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>An hour into the clinic's today — always inside the window the handler reads with its own clock.</summary>
    private static DateTime TodayAtOne() => ClinicClock.TodayRangeUtc().From.AddHours(1);

    private static StaffLinkedAppointment Appointment(
        StaffChatHarness h, Guid? doctorId, DateTime? startsAt = null, AppointmentStatus status = AppointmentStatus.Scheduled,
        Guid? patientId = null, bool noPatient = false)
    {
        var appointment = new StaffLinkedAppointment(
            Guid.NewGuid(), noPatient ? null : patientId ?? PatientId, doctorId, startsAt ?? TodayAtOne(), status, false);
        h.Messages.Setup(r => r.GetLinkedAppointmentsAsync(
                StaffChatHarness.ClinicId,
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(appointment.Id)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { appointment });
        return appointment;
    }

    // ── Where it goes ────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Secretary_Announces_Into_The_Dentists_Secretariat_With_The_Appointment() // AC-3.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        StaffMessage? added = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => added = m);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffConversation.Desk(h.Owner.Id).Key, added!.ConversationKey);
        Assert.Equal(StaffQuickMessageDefaults.PatientArrivedKey, added.QuickMessageKey);
        Assert.Equal("Patient arrivé", added.QuickMessageLabel);
        Assert.Equal(PatientId, added.PatientId);
        Assert.Equal(appointment.Id, added.AppointmentId);
        Assert.Null(added.Body);
        Assert.Equal("Dr Amine Ben Salah", result.Value!.Appointment!.DentistName);
    }

    [Fact]
    public async Task Another_Dentist_Announces_Into_The_Two_Person_Thread() // AC-3.2, EC-15
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Dentist);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        StaffMessage? added = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => added = m);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(StaffConversation.Direct(h.Dentist.Id, h.Owner.Id).Key, added!.ConversationKey);
    }

    [Fact]
    public async Task The_Arrival_Uses_The_Built_In_As_The_Cabinet_Renamed_It() // AC-6.2
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        var list = StaffQuickMessageList.Create(StaffChatHarness.ClinicId, StaffQuickMessageDefaults.Items()
            .Select(i => new StaffQuickMessageInput(
                i.Key, i.Key == StaffQuickMessageDefaults.PatientArrivedKey ? "Patient en salle" : i.Label,
                i.Audience, i.AsksForPatient, i.IsUrgent)), DateTime.UtcNow);
        h.QuickMessageLists.Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(list);
        StaffMessage? added = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => added = m);

        await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal("Patient en salle", added!.QuickMessageLabel);
    }

    [Fact]
    public async Task An_Appointment_Marked_Absent_Can_Still_Be_Announced() // FR-5 — the patient came late
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id, status: AppointmentStatus.NoShow);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.True(result.IsSuccess);
    }

    // ── Twice ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Second_Announcement_Says_When_And_By_Whom_And_Sends_Nothing() // AC-3.3, EC-5
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        var sentAt = ClinicClock.ToUtc(ClinicClock.ClinicToday().AddHours(9).AddMinutes(32));
        h.Messages.Setup(r => r.GetLastArrivalAsync(StaffChatHarness.ClinicId, appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffArrival(appointment.Id, sentAt, h.Salma.Id));

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.ArrivalAlreadySentCode, result.Code);
        Assert.Equal("Signalé à 09:32 par Salma.", result.Error);
        h.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Confirming_Sends_It_Again() // AC-3.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        h.Messages.Setup(r => r.GetLastArrivalAsync(StaffChatHarness.ClinicId, appointment.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffArrival(appointment.Id, DateTime.UtcNow, h.Salma.Id));

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id, Resend = true }, default);

        Assert.True(result.IsSuccess);
        h.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Why it cannot go (AC-3.4) ────────────────────────────────────────────

    [Fact]
    public async Task No_Dentist_On_The_Appointment()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, doctorId: null);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.NoDentistCode, result.Code);
    }

    [Fact]
    public async Task A_Dentist_With_No_Account()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var noAccount = new Doctor(Guid.NewGuid(), StaffChatHarness.ClinicId, "Nadia", "Trabelsi", "General");
        h.ClinicDoctors.Add(noAccount);
        var appointment = Appointment(h, noAccount.Id);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.DentistNoAccountCode, result.Code);
        Assert.Equal("Ce praticien n'a pas de compte dans l'application.", result.Error);
    }

    [Fact]
    public async Task A_Retired_Dentist()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.SecondDoctor.Retire();
        var appointment = Appointment(h, h.SecondDoctor.Id);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.DentistInactiveCode, result.Code);
    }

    [Fact]
    public async Task A_Dentist_Whose_Account_Is_Deactivated()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        h.Dentist.Deactivate();
        var appointment = Appointment(h, h.SecondDoctor.Id);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.DentistInactiveCode, result.Code);
    }

    [Fact]
    public async Task Ones_Own_Appointment()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var appointment = Appointment(h, h.OwnerDoctor.Id);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.OwnAppointmentCode, result.Code);
    }

    [Fact]
    public async Task Not_Today()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id, startsAt: TodayAtOne().AddDays(-1));

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.AppointmentNotTodayCode, result.Code);
    }

    [Fact]
    public async Task A_Cancelled_Appointment()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id, status: AppointmentStatus.Cancelled);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = appointment.Id }, default);

        Assert.Equal(StaffChatRefusals.AppointmentCancelledCode, result.Code);
    }

    [Fact]
    public async Task Another_Cabinets_Appointment_Is_Not_Found()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Arrival().Handle(new SendArrivalNoticeCommand { AppointmentId = Guid.NewGuid() }, default);

        Assert.Equal(StaffChatRefusals.AppointmentNotFoundCode, result.Code);
    }

    // ── Today's actions ──────────────────────────────────────────────────────

    [Fact]
    public async Task Todays_Actions_Say_Who_Announced_And_Why_One_Cannot_Go() // AC-3.3, AC-3.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);
        var noAccount = new Doctor(Guid.NewGuid(), StaffChatHarness.ClinicId, "Nadia", "Trabelsi", "General");
        h.ClinicDoctors.Add(noAccount);
        var announced = new StaffLinkedAppointment(Guid.NewGuid(), PatientId, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Confirmed, false);
        var blocked = new StaffLinkedAppointment(Guid.NewGuid(), PatientId, noAccount.Id, TodayAtOne(), AppointmentStatus.Scheduled, false);
        var cancelled = new StaffLinkedAppointment(Guid.NewGuid(), PatientId, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Cancelled, false);
        var busySlot = new StaffLinkedAppointment(Guid.NewGuid(), null, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Scheduled, false);
        h.Messages.Setup(r => r.GetAppointmentsBetweenAsync(StaffChatHarness.ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { announced, blocked, cancelled, busySlot });
        h.Messages.Setup(r => r.GetArrivalsAsync(StaffChatHarness.ClinicId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new StaffArrival(announced.Id, TodayAtOne(), h.Salma.Id) });

        var items = (await h.TodayArrivals().Handle(new GetTodayArrivalsQuery(), default)).Value!;

        Assert.Equal(new[] { announced.Id, blocked.Id }, items.Select(i => i.AppointmentId));
        Assert.Equal("Salma", items[0].SentBy!.Name);
        Assert.Null(items[0].Unavailable);
        Assert.Null(items[1].SentAt);
        Assert.Equal("Ce praticien n'a pas de compte dans l'application.", items[1].Unavailable);
    }

    // ── A message's links (AC-2.4, FR-3, EC-8) ───────────────────────────────

    [Fact]
    public async Task An_Appointment_Alone_Brings_Its_Patient() // AC-2.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id);
        h.Patients.Setup(r => r.GetByIdAsync(PatientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(PatientId, StaffChatHarness.ClinicId, "Leila", "Ben Ali", null, "F"));
        StaffMessage? added = null;
        h.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback<StaffMessage, CancellationToken>((m, _) => added = m);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key,
            Body = "Elle attend",
            AppointmentId = appointment.Id,
        }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PatientId, added!.PatientId);
        Assert.Equal(appointment.Id, added.AppointmentId);
    }

    [Fact]
    public async Task An_Appointment_Of_Another_Patient_Is_Refused()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var appointment = Appointment(h, h.OwnerDoctor.Id);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = "clinic",
            Body = "?",
            PatientId = Guid.NewGuid(),
            AppointmentId = appointment.Id,
        }, default);

        Assert.Equal(StaffChatRefusals.AppointmentNotFoundCode, result.Code);
        h.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_Cabinets_Patient_Is_Refused()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var foreign = Guid.NewGuid();
        h.Patients.Setup(r => r.GetByIdAsync(foreign, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Patient(foreign, StaffChatHarness.OtherClinicId, "X", "Y", null, "M"));

        var result = await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = "?", PatientId = foreign }, default);

        Assert.Equal(StaffChatRefusals.PatientNotFoundCode, result.Code);
    }

    [Fact]
    public async Task A_Link_Is_Shown_As_It_Is_Now_Archived_Cancelled_Or_Gone() // FR-3, EC-8
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var desk = StaffConversation.Desk(h.Owner.Id);
        var archivedId = Guid.NewGuid();
        var goneId = Guid.NewGuid();
        var archived = new Patient(archivedId, StaffChatHarness.ClinicId, "Leila", "Ben Ali", null, "F");
        archived.Archive();
        var cancelled = new StaffLinkedAppointment(Guid.NewGuid(), archivedId, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Cancelled, false);
        var removed = new StaffLinkedAppointment(Guid.NewGuid(), goneId, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Scheduled, true);
        var first = StaffMessage.Create(StaffChatHarness.ClinicId, desk, h.Salma.Id, "a", null, archivedId, cancelled.Id, TodayAtOne());
        var second = StaffMessage.Create(StaffChatHarness.ClinicId, desk, h.Salma.Id, "b", null, goneId, removed.Id, TodayAtOne().AddMinutes(1));
        h.Messages.Setup(r => r.GetPageAsync(StaffChatHarness.ClinicId, desk.Key, null, null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { second, first });
        h.Patients.Setup(r => r.GetByIdsAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient> { [archivedId] = archived });
        h.Messages.Setup(r => r.GetLinkedAppointmentsAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { cancelled, removed });

        var items = (await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = desk.Key }, default)).Value!.Items;

        var withArchived = items.Single(i => i.Body == "a");
        Assert.Equal("Leila Ben Ali", withArchived.Patient!.Name);
        Assert.True(withArchived.Patient.Archived);
        Assert.True(withArchived.Appointment!.Cancelled);
        Assert.Equal("Dr Amine Ben Salah", withArchived.Appointment.DentistName);

        var withGone = items.Single(i => i.Body == "b");
        Assert.True(withGone.Patient!.Missing);
        Assert.Null(withGone.Patient.Name);
        Assert.True(withGone.Appointment!.Missing);
    }

    [Fact]
    public async Task The_Conversation_List_Carries_The_Last_Messages_Links() // FR-3 — every read of a message fills them
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var desk = StaffConversation.Desk(h.Owner.Id);
        var patient = new Patient(PatientId, StaffChatHarness.ClinicId, "Leila", "Ben Ali", null, "F");
        var visit = new StaffLinkedAppointment(Guid.NewGuid(), PatientId, h.OwnerDoctor.Id, TodayAtOne(), AppointmentStatus.Scheduled, false);
        var last = StaffMessage.Create(StaffChatHarness.ClinicId, desk, h.Salma.Id, "Patient arrivé", null, PatientId, visit.Id, TodayAtOne());
        h.Messages.Setup(r => r.GetLatestPerConversationAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { last });
        h.Patients.Setup(r => r.GetByIdsAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, Patient> { [PatientId] = patient });
        h.Messages.Setup(r => r.GetLinkedAppointmentsAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { visit });

        var list = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!;

        var lastMessage = list.Single(c => c.Key == desk.Key).LastMessage!;
        Assert.Equal("Leila Ben Ali", lastMessage.Patient!.Name);
        Assert.Equal(visit.Id, lastMessage.Appointment!.Id);
        Assert.Equal("Dr Amine Ben Salah", lastMessage.Appointment.DentistName);
    }
}
