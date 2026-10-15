using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

public class GetStaffMessagesQueryTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_Full_Page_Hands_Back_The_Cursor_Of_Its_Oldest_Message_And_The_Cursor_Reaches_The_Repository()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var conversation = StaffConversation.Clinic;
        var page = new[]
        {
            StaffChatHarness.Message(conversation, h.Salma, Nine.AddMinutes(1)),
            StaffChatHarness.Message(conversation, h.Amel, Nine),
        };
        h.Messages.Setup(r => r.GetPageAsync(StaffChatHarness.ClinicId, "clinic", null, null, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var first = (await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = "clinic", Take = 2 }, default)).Value!;
        Assert.Equal(StaffMessageCursor.Of(page[1]), first.NextCursor);

        await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = "clinic", Take = 2, Before = first.NextCursor }, default);
        h.Messages.Verify(r => r.GetPageAsync(StaffChatHarness.ClinicId, "clinic", Nine, page[1].Id, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Short_Page_Is_The_Last_One_And_The_Size_Is_Bounded()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);

        var page = (await h.Thread().Handle(new GetStaffMessagesQuery { Conversation = "clinic", Take = 5000 }, default)).Value!;

        Assert.Null(page.NextCursor);
        h.Messages.Verify(r => r.GetPageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), GetStaffMessagesQuery.MaxTake, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Another_Pairs_Thread_Reads_As_Not_Found() // FR-1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Amel);

        var result = await h.Thread().Handle(
            new GetStaffMessagesQuery { Conversation = StaffConversation.Direct(h.Owner.Id, h.Salma.Id).Key }, default);

        Assert.Equal(StaffChatRefusals.ConversationNotFoundCode, result.Code);
        h.Messages.Verify(r => r.GetPageAsync(
            It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("123_not-a-guid")]
    [InlineData("-5_0123456789abcdef0123456789abcdef")]
    public void An_Unreadable_Cursor_Starts_From_The_Newest(string? cursor)
    {
        Assert.Equal((null, null), StaffMessageCursor.Parse(cursor));
    }

    [Fact]
    public void A_Cursor_Round_Trips()
    {
        var message = StaffMessage.Create(StaffChatHarness.ClinicId, StaffConversation.Clinic, "local|x", "a", null, null, null, Nine);

        Assert.Equal((Nine, message.Id), StaffMessageCursor.Parse(StaffMessageCursor.Of(message)));
    }
}
