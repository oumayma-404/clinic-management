using ClinicManagement.Application.Features.StaffChat;
using ClinicManagement.Application.Features.StaffChat.Commands;
using ClinicManagement.Application.Features.StaffChat.Queries;
using Xunit;

namespace ClinicManagement.UnitTests.Features.StaffChat;

/// <summary>A person's colour in the chat: their place in the cabinet's order of arrival, the same on every read.</summary>
public class StaffChatToneTests
{
    private static readonly DateTime Start = new(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);

    private static StaffChatColleague Person(string id, int day, bool active = true) =>
        new(id, id, IsSecretary: false, IsDentist: false, IsActive: active, CreatedAt: Start.AddDays(day));

    [Fact]
    public void Each_Account_Takes_Its_Place_In_The_Order_Of_Arrival_Inactive_Ones_Included()
    {
        var first = Person("local|first", 0);
        var roster = new StaffChatRoster(Guid.NewGuid(), first, new[] { Person("local|third", 9), first, Person("local|left", 3, active: false) });

        Assert.Equal(0, roster.ToneOf("local|first"));
        Assert.Equal(1, roster.ToneOf("local|left"));
        Assert.Equal(2, roster.ToneOf("local|third"));
        Assert.Null(roster.ToneOf("local|gone"));
    }

    [Fact]
    public void A_New_Colleague_Changes_Nobodys_Colour()
    {
        var a = Person("local|a", 0);
        var b = Person("local|b", 2);
        var before = new StaffChatRoster(Guid.NewGuid(), a, new[] { a, b });
        var after = new StaffChatRoster(Guid.NewGuid(), a, new[] { a, b, Person("local|new", 30) });

        Assert.Equal(before.ToneOf(a.UserId), after.ToneOf(a.UserId));
        Assert.Equal(before.ToneOf(b.UserId), after.ToneOf(b.UserId));
        Assert.Equal(2, after.ToneOf("local|new"));
    }

    [Fact]
    public async Task A_Message_Carries_The_Colour_Its_Author_Has_In_The_Colleague_List()
    {
        var h = new StaffChatHarness();
        h.ActAs(h.Salma);
        var sent = (await h.Send().Handle(new SendStaffMessageCommand { Conversation = "clinic", Body = "Réunion à 13h" }, default)).Value!;

        h.ActAs(h.Owner);
        var colleagues = (await h.Colleagues().Handle(new GetStaffColleaguesQuery(), default)).Value!;

        Assert.NotNull(sent.Sender.Tone);
        Assert.Equal(colleagues.Single(c => c.UserId == h.Salma.Id).Tone, sent.Sender.Tone);
        Assert.Equal(colleagues.Count, colleagues.Select(c => c.Tone).Distinct().Count());
    }
}
