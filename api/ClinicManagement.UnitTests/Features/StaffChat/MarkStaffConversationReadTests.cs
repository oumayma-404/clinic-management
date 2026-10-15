using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

public class MarkStaffConversationReadTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_First_Read_Records_The_Messages_Time_And_Signals_Only_The_Readers_Own_Tabs() // EC-9
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var conversation = StaffConversation.Direct(h.Owner.Id, h.Salma.Id);
        var message = StaffChatHarness.Message(conversation, h.Salma, Nine);
        h.Messages.Setup(r => r.GetByIdAsync(message.Id, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        StaffChatReadMarker? added = null;
        h.Messages.Setup(r => r.AddMarkerAsync(It.IsAny<StaffChatReadMarker>(), It.IsAny<CancellationToken>()))
            .Callback<StaffChatReadMarker, CancellationToken>((m, _) => added = m);

        var result = await h.MarkRead().Handle(
            new MarkStaffConversationReadCommand { Conversation = conversation.Key, UpToMessageId = message.Id }, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(Nine, added!.LastReadAt);
        Assert.Equal(h.Owner.Id, added.UserId);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Realtime.Verify(n => n.NotifyStaffChatAsync(
            StaffChatHarness.ClinicId,
            It.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { h.Owner.Id })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reading_An_Older_Message_Changes_Nothing()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var conversation = StaffConversation.Direct(h.Owner.Id, h.Salma.Id);
        var older = StaffChatHarness.Message(conversation, h.Salma, Nine.AddMinutes(-5));
        h.Messages.Setup(r => r.GetByIdAsync(older.Id, It.IsAny<CancellationToken>())).ReturnsAsync(older);
        h.Messages.Setup(r => r.GetMarkerAsync(StaffChatHarness.ClinicId, h.Owner.Id, conversation.Key, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StaffChatReadMarker(StaffChatHarness.ClinicId, h.Owner.Id, conversation.Key, Nine));

        var result = await h.MarkRead().Handle(
            new MarkStaffConversationReadCommand { Conversation = conversation.Key, UpToMessageId = older.Id }, default);

        Assert.True(result.IsSuccess);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Message_Of_Another_Thread_Cannot_Mark_This_One_Read()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var elsewhere = StaffChatHarness.Message(StaffConversation.Clinic, h.Salma, Nine);
        h.Messages.Setup(r => r.GetByIdAsync(elsewhere.Id, It.IsAny<CancellationToken>())).ReturnsAsync(elsewhere);

        var result = await h.MarkRead().Handle(new MarkStaffConversationReadCommand
        {
            Conversation = StaffConversation.Direct(h.Owner.Id, h.Salma.Id).Key,
            UpToMessageId = elsewhere.Id,
        }, default);

        Assert.Equal(StaffChatRefusals.MessageNotFoundCode, result.Code);
        h.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
