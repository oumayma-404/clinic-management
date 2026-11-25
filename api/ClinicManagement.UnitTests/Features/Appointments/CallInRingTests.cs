using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Appointments.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;
using ClinicManagement.UnitTests.Features.StaffChat;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Appointments;

/// <summary>
/// `salle-attente` S5 — « Patient suivant » rings reception. The ring is the half of the feature a dentist cannot see
/// working: he presses, the board moves, and whether the desk was told is decided entirely here. It must go to HIS desk
/// thread (where his secretary reads), carry the called patient, and be urgent — the notice then stays until somebody
/// answers it and rings even on a muted desk.
/// </summary>
public class CallInRingTests
{
    private static (StaffChatHarness chat, Mock<ICurrentClinicResolver> resolver, Appointment waiting) Arrange()
    {
        var chat = new StaffChatHarness();
        chat.ActAs(chat.Owner);
        chat.Context.Setup(c => c.GetUserRole()).Returns(User.RoleAdmin);
        chat.Doctors.Setup(r => r.GetByUserIdAsync(chat.Owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(chat.OwnerDoctor);

        var waiting = new Appointment(
            Guid.NewGuid(), StaffChatHarness.ClinicId, Guid.NewGuid(), chat.OwnerDoctor.Id,
            DateTime.UtcNow.AddMinutes(-10), TimeSpan.FromMinutes(30));
        waiting.MoveInWaitingRoom(WaitingRoomLane.Waiting, DateTime.UtcNow.AddMinutes(-8));

        chat.Appointments
            .Setup(r => r.GetByClinicIdAsync(StaffChatHarness.ClinicId, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new List<Appointment> { waiting });

        var resolver = new Mock<ICurrentClinicResolver>();
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(StaffChatHarness.ClinicId));
        return (chat, resolver, waiting);
    }

    private static CallNextPatientCommandHandler Handler(StaffChatHarness chat, Mock<ICurrentClinicResolver> resolver) => new(
        chat.Appointments.Object, chat.Doctors.Object, chat.Users.Object, chat.Patients.Object, chat.Messages.Object,
        chat.UnitOfWork.Object, resolver.Object, chat.Context.Object, chat.Realtime.Object, chat.Push.Object,
        NullLogger<CallNextPatientCommandHandler>.Instance);

    [Fact]
    public async Task Calling_The_Next_Patient_Rings_The_Dentists_Desk_Urgently_About_That_Patient()
    {
        var (chat, resolver, waiting) = Arrange();
        StaffMessage? rung = null;
        chat.Messages.Setup(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()))
            .Callback((StaffMessage m, CancellationToken _) => rung = m);

        var result = await Handler(chat, resolver).Handle(
            new CallNextPatientCommand { ExpectedNextId = waiting.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, waiting.WaitingRoomLane);
        Assert.NotNull(rung);
        Assert.Equal(CallNextPatientCommandHandler.CallInKey, rung!.QuickMessageKey);
        Assert.True(rung.IsUrgent);
        Assert.Equal(StaffConversation.Desk(chat.Owner.Id).Key, rung.ConversationKey);
        Assert.Equal(waiting.Id, rung.AppointmentId);
        Assert.Equal(waiting.PatientId, rung.PatientId);
    }

    // No secretary on the desk thread means nobody to ring: the call still lands, and nothing is posted to a thread
    // only the dentist reads.
    [Fact]
    public async Task A_Desk_With_No_Secretary_Is_Not_Rung_And_The_Call_Still_Lands()
    {
        var (chat, resolver, waiting) = Arrange();
        chat.ClinicUsers.RemoveAll(u => u.IsSecretary());

        var result = await Handler(chat, resolver).Handle(
            new CallNextPatientCommand { ExpectedNextId = waiting.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WaitingRoomLane.InChair, waiting.WaitingRoomLane);
        chat.Messages.Verify(r => r.AddAsync(It.IsAny<StaffMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
