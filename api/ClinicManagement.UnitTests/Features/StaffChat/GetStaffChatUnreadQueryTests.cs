using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

public class GetStaffChatUnreadQueryTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task The_Badge_Counts_What_Rings_And_Leaves_A_Colleagues_Desk_Reply_Out() // AC-1.1, AC-5.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var desk = StaffConversation.Desk(h.Owner.Id).Key;
        var withOwner = StaffConversation.Direct(h.Salma.Id, h.Owner.Id).Key;
        h.Messages.Setup(r => r.GetUnreadCountsAsync(StaffChatHarness.ClinicId, h.Salma.Id, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StaffUnreadCount(desk, h.Amel.Id, 4, 0, Nine),
                new StaffUnreadCount(desk, h.Owner.Id, 1, 1, Nine),
                new StaffUnreadCount(withOwner, h.Owner.Id, 2, 0, Nine),
            });

        var unread = (await h.Unread().Handle(new GetStaffChatUnreadQuery(), default)).Value!;

        Assert.Equal(3, unread.Total);
        Assert.True(unread.HasUrgent);
    }

    [Fact]
    public async Task Older_Than_The_Account_Is_Never_Unread_And_Recent_Holds_Only_What_Rings() // FR-2, AC-4.8
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var desk = StaffConversation.Desk(h.Owner.Id);
        h.Messages.Setup(r => r.GetRecentUnreadAsync(StaffChatHarness.ClinicId, h.Salma.Id, It.IsAny<IReadOnlyCollection<string>>(), h.Salma.CreatedAt, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                StaffChatHarness.Message(desk, h.Amel, Nine.AddMinutes(2), "silencieux"),
                StaffChatHarness.Message(desk, h.Owner, Nine.AddMinutes(1), "Faites entrer"),
            });

        var unread = (await h.Unread().Handle(new GetStaffChatUnreadQuery(), default)).Value!;

        Assert.Equal(new[] { "Faites entrer" }, unread.Recent.Select(m => m.Body));
        h.Messages.Verify(r => r.GetUnreadCountsAsync(
            StaffChatHarness.ClinicId, h.Salma.Id, It.IsAny<IReadOnlyCollection<string>>(), h.Salma.CreatedAt, It.IsAny<CancellationToken>()), Times.Once);
    }
}
