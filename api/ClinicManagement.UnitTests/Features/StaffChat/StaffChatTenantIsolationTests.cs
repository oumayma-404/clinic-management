using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>Another cabinet's accounts, threads and messages read as « not found » — never as a confirmation they exist.</summary>
public class StaffChatTenantIsolationTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Writing_To_An_Account_Of_Another_Cabinet_Is_Not_Found()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Send().Handle(new SendStaffMessageCommand
        {
            Conversation = StaffConversation.Direct(h.Salma.Id, h.Outsider.Id).Key,
            Body = "Bonjour",
        }, default);

        Assert.Equal(StaffChatRefusals.ConversationNotFoundCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Another_Cabinets_Message_Cannot_Mark_Anything_Read()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var foreign = StaffChatHarness.Message(StaffConversation.Clinic, h.Outsider, Nine, clinicId: StaffChatHarness.OtherClinicId);
        h.Messages.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await h.MarkRead().Handle(
            new MarkStaffConversationReadCommand { Conversation = "clinic", UpToMessageId = foreign.Id }, default);

        Assert.Equal(StaffChatRefusals.MessageNotFoundCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Every_Read_Is_Asked_Of_The_Callers_Own_Cabinet()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Outsider);

        await h.Conversations().Handle(new GetStaffConversationsQuery(), default);
        await h.Unread().Handle(new GetStaffChatUnreadQuery(), default);
        await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = "clinic" }, default);
        var colleagues = (await h.Colleagues().Handle(new GetStaffColleaguesQuery(), default)).Value!;

        h.Messages.Verify(r => r.GetLatestPerConversationAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Messages.Verify(r => r.GetUnreadCountsAsync(StaffChatHarness.ClinicId, It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Messages.Verify(r => r.GetPageAsync(StaffChatHarness.ClinicId, It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(colleagues);
    }
}
