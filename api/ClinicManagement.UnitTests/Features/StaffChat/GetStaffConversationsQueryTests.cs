using ClinicManagement.Application.Features.StaffChat.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

public class GetStaffConversationsQueryTests
{
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_Secretary_Has_The_Cabinet_Each_Dentists_Desk_And_Each_Colleague() // FR-1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);

        var result = await h.Conversations().Handle(new GetStaffConversationsQuery(), default);

        var list = result.Value!;
        Assert.Contains(list, c => c.Kind == "clinic" && c.Title == "Tout le cabinet");
        Assert.Contains(list, c => c.Kind == "desk" && c.Key == StaffConversation.Desk(h.Owner.Id).Key && c.Title == "Dr Amine");
        Assert.Contains(list, c => c.Kind == "desk" && c.Key == StaffConversation.Desk(h.Dentist.Id).Key && c.Title == "Dr Sami");
        Assert.Equal(3, list.Count(c => c.Kind == "direct"));
        Assert.DoesNotContain(list, c => c.Title.Contains("Salma"));
    }

    [Fact]
    public async Task A_Dentist_Sees_Their_Own_Desk_Titled_Secretariat_Never_A_Secretarys_Name() // FR-1
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);

        var list = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!;

        var desk = Assert.Single(list, c => c.Kind == "desk");
        Assert.Equal("Secrétariat", desk.Title);
        Assert.Equal(new[] { h.Amel.Id, h.Owner.Id, h.Salma.Id }.OrderBy(x => x, StringComparer.Ordinal),
            desk.Members.Select(m => m.UserId).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public async Task With_Two_Accounts_There_Is_No_Cabinet_Thread_Beside_The_Two_Person_One() // FR-1
    {
        var h = new StaffChatHarness();
        h.ClinicUsers.Clear();
        h.ClinicUsers.AddRange(new[] { h.Owner, h.Salma });
        h.ActAs(h.Owner);

        var list = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!;

        Assert.DoesNotContain(list, c => c.Kind == "clinic");
        Assert.Contains(list, c => c.Kind == "direct");
    }

    [Fact]
    public async Task Threads_With_History_Come_First_Newest_First_Then_The_Rest_A_To_Z() // AC-1.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        var withSami = StaffConversation.Direct(h.Owner.Id, h.Dentist.Id);
        h.Messages.Setup(r => r.GetLatestPerConversationAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                StaffChatHarness.Message(StaffConversation.Clinic, h.Salma, Nine),
                StaffChatHarness.Message(withSami, h.Dentist, Nine.AddMinutes(5)),
            });

        var titles = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!.Select(c => c.Title).ToList();

        Assert.Equal(new[] { "Dr Sami", "Tout le cabinet", "Amel", "Salma", "Secrétariat" }, titles);
    }

    [Fact]
    public async Task A_Desk_Counts_A_Colleagues_Reply_As_Unread_But_Only_The_Dentists_Message_Can_Be_Urgent() // AC-5.3
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var desk = StaffConversation.Desk(h.Owner.Id).Key;
        h.Messages.Setup(r => r.GetUnreadCountsAsync(StaffChatHarness.ClinicId, h.Salma.Id, It.IsAny<IReadOnlyCollection<string>>(), h.Salma.CreatedAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StaffUnreadCount(desk, h.Amel.Id, 2, 1, Nine),
                new StaffUnreadCount(desk, h.Owner.Id, 1, 0, Nine),
            });

        var thread = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!.Single(c => c.Key == desk);

        Assert.Equal(3, thread.UnreadCount);
        Assert.False(thread.HasUrgentUnread);
    }

    [Fact]
    public async Task A_Departed_Colleague_With_No_History_Is_Not_Offered_And_With_History_Is_Read_Only() // FR-9
    {
        var h = new StaffChatHarness();
        h.Amel.Deactivate();
        h.ActAs(h.Owner);
        var withAmel = StaffConversation.Direct(h.Owner.Id, h.Amel.Id);

        var before = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!;
        Assert.DoesNotContain(before, c => c.Key == withAmel.Key);

        h.Messages.Setup(r => r.GetLatestPerConversationAsync(StaffChatHarness.ClinicId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { StaffChatHarness.Message(withAmel, h.Amel, Nine) });

        var after = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!;
        Assert.True(after.Single(c => c.Key == withAmel.Key).ReadOnly);
    }

    [Fact]
    public async Task Online_Is_Read_From_Presence_And_Never_Claimed_For_A_Deactivated_Account() // AC-1.4
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Owner);
        h.Presence.Setup(p => p.IsOnline(StaffChatHarness.ClinicId, h.Salma.Id)).Returns(true);

        var withSalma = (await h.Conversations().Handle(new GetStaffConversationsQuery(), default)).Value!
            .Single(c => c.Key == StaffConversation.Direct(h.Owner.Id, h.Salma.Id).Key);

        Assert.True(withSalma.Members.Single(m => m.UserId == h.Salma.Id).Online);
        Assert.False(withSalma.Members.Single(m => m.UserId == h.Owner.Id).Online);
    }
}
