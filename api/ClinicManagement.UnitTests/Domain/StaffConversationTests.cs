using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>Pins staff-chat FR-1 (who reads a thread) and FR-2 (who it rings) — the one owner of both rules.</summary>
public class StaffConversationTests
{
    private const string Dentist = "local|aaaaaaaa-0000-0000-0000-000000000001";
    private const string OtherDentist = "google-oauth2|1234567890";
    private const string Salma = "local|bbbbbbbb-0000-0000-0000-000000000002";
    private const string Amel = "local|cccccccc-0000-0000-0000-000000000003";

    private static readonly IReadOnlyCollection<StaffChatMember> Roster = new[]
    {
        new StaffChatMember(Dentist, IsSecretary: false, IsActive: true),
        new StaffChatMember(OtherDentist, IsSecretary: false, IsActive: true),
        new StaffChatMember(Salma, IsSecretary: true, IsActive: true),
        new StaffChatMember(Amel, IsSecretary: true, IsActive: true),
    };

    [Fact]
    public void Direct_Key_Is_The_Same_Whichever_Way_Round_And_Survives_Pipes_In_Ids()
    {
        var one = StaffConversation.Direct(Dentist, Salma);
        var other = StaffConversation.Direct(Salma, Dentist);

        Assert.Equal(one.Key, other.Key);
        Assert.DoesNotContain("|", one.Key);
        Assert.True(StaffConversation.TryParse(one.Key, out var parsed));
        Assert.Equal(StaffConversationKind.Direct, parsed.Kind);
        Assert.Equal(one, parsed);
        Assert.Contains(Dentist, new[] { parsed.First, parsed.Second });
        Assert.Contains(Salma, new[] { parsed.First, parsed.Second });
    }

    [Fact]
    public void Desk_And_Clinic_Keys_Round_Trip()
    {
        Assert.True(StaffConversation.TryParse(StaffConversation.Desk(OtherDentist).Key, out var desk));
        Assert.Equal(StaffConversationKind.Desk, desk.Kind);
        Assert.Equal(OtherDentist, desk.First);

        Assert.True(StaffConversation.TryParse("clinic", out var clinic));
        Assert.Equal(StaffConversationKind.Clinic, clinic.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dm:a")]
    [InlineData("dm:b:a")]
    [InlineData("dm:a:a")]
    [InlineData("desk:")]
    [InlineData("other:a")]
    [InlineData("dm:a%:b")]
    public void A_Key_This_Class_Could_Not_Have_Produced_Is_Refused(string? key)
    {
        Assert.False(StaffConversation.TryParse(key, out _));
    }

    [Fact]
    public void Writing_To_Oneself_Is_Refused()
    {
        Assert.Throws<ArgumentException>(() => StaffConversation.Direct(Salma, Salma));
    }

    [Fact]
    public void A_Two_Person_Thread_Is_Read_By_Its_Two_People_Only_Admin_Included()
    {
        var dm = StaffConversation.Direct(Dentist, Salma);

        Assert.True(dm.IsMember(Dentist, Roster));
        Assert.True(dm.IsMember(Salma, Roster));
        Assert.False(dm.IsMember(Amel, Roster));
        Assert.False(dm.IsMember(OtherDentist, Roster));
        Assert.Equal(new[] { Dentist, Salma }.OrderBy(x => x, StringComparer.Ordinal), dm.Members(Roster).OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_Desk_Thread_Is_Its_Owner_And_Every_Secretary_Not_Another_Dentist()
    {
        var desk = StaffConversation.Desk(Dentist);

        Assert.True(desk.IsMember(Dentist, Roster));
        Assert.True(desk.IsMember(Salma, Roster));
        Assert.True(desk.IsMember(Amel, Roster));
        Assert.False(desk.IsMember(OtherDentist, Roster));
    }

    [Fact]
    public void A_Desk_Thread_Does_Not_Exist_Without_A_Secretary_Or_For_A_Secretary_Owner() // EC-2, EC-14
    {
        var noSecretary = new[]
        {
            new StaffChatMember(Dentist, false, true),
            new StaffChatMember(OtherDentist, false, true),
        };

        Assert.False(StaffConversation.Desk(Dentist).IsMember(Dentist, noSecretary));
        Assert.False(StaffConversation.Desk(Salma).IsMember(Salma, Roster));
    }

    [Fact]
    public void The_Cabinet_Thread_Is_Everyone_And_An_Unknown_Account_Is_Nobody()
    {
        Assert.All(Roster, m => Assert.True(StaffConversation.Clinic.IsMember(m.UserId, Roster)));
        Assert.False(StaffConversation.Clinic.IsMember("local|stranger", Roster));
    }

    [Fact]
    public void A_Two_Person_Message_Rings_The_Other_Person_Only()
    {
        Assert.Equal(new[] { Salma }, StaffConversation.Direct(Dentist, Salma).AlertsFor(Dentist, Roster));
    }

    [Fact]
    public void A_Dentist_Message_To_The_Desk_Rings_Every_Secretary() // FR-2, AC-5.1
    {
        var alerted = StaffConversation.Desk(Dentist).AlertsFor(Dentist, Roster);

        Assert.Equal(new[] { Amel, Salma }.OrderBy(x => x, StringComparer.Ordinal), alerted.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void A_Secretary_Reply_On_The_Desk_Rings_The_Dentist_Only() // FR-2, AC-5.3
    {
        var desk = StaffConversation.Desk(Dentist);

        Assert.Equal(new[] { Dentist }, desk.AlertsFor(Salma, Roster));
        Assert.False(desk.Alerts(Amel, Salma, Roster));
        Assert.True(desk.IsMember(Amel, Roster));
    }

    [Fact]
    public void A_Cabinet_Message_Rings_Everyone_But_The_Sender()
    {
        var alerted = StaffConversation.Clinic.AlertsFor(Salma, Roster);

        Assert.DoesNotContain(Salma, alerted);
        Assert.Equal(3, alerted.Count);
    }

    [Fact]
    public void A_Deactivated_Account_Is_Never_Rung_But_Still_Reads_Its_History() // FR-9
    {
        var roster = new[]
        {
            new StaffChatMember(Dentist, false, true),
            new StaffChatMember(Salma, true, IsActive: false),
        };
        var dm = StaffConversation.Direct(Dentist, Salma);

        Assert.Empty(dm.AlertsFor(Dentist, roster));
        Assert.True(dm.IsMember(Salma, roster));
    }
}
