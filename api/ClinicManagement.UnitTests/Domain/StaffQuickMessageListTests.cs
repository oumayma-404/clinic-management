using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

public class StaffQuickMessageListTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Nine = new(2026, 10, 15, 9, 0, 0, DateTimeKind.Utc);

    private static List<StaffQuickMessageInput> Defaults() => StaffQuickMessageDefaults.Items()
        .Select(i => new StaffQuickMessageInput(i.Key, i.Label, i.Audience, i.AsksForPatient, i.IsUrgent))
        .ToList();

    [Fact]
    public void The_Defaults_Are_The_Seventeen_Of_FR4_With_Three_Built_Ins() // FR-4
    {
        var items = StaffQuickMessageDefaults.Items();

        Assert.Equal(17, items.Count);
        Assert.Equal(6, items.Count(i => i.Audience == StaffQuickMessageAudience.Dentist));
        Assert.Equal(7, items.Count(i => i.Audience == StaffQuickMessageAudience.Desk));
        Assert.Equal(4, items.Count(i => i.Audience == StaffQuickMessageAudience.Reply));
        Assert.Equal(new[] { "Urgence en salle d'attente", "Besoin d'aide au fauteuil" }, items.Where(i => i.IsUrgent).Select(i => i.Label));
        Assert.Equal(StaffQuickMessageDefaults.BuiltInKeys.OrderBy(k => k), items.Where(i => i.IsBuiltIn).Select(i => i.Key).OrderBy(k => k));
        Assert.Equal(items.Count, items.Select(i => i.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(items, i => Assert.InRange(i.Label.Length, 1, 40));
        Assert.Equal(Enumerable.Range(0, 17), items.Select(i => i.SortOrder));
    }

    [Fact]
    public void Saving_The_Defaults_Keeps_Their_Order_Keys_And_Built_In_Flags()
    {
        var list = StaffQuickMessageList.Create(ClinicId, Defaults(), Nine);

        Assert.Equal(StaffQuickMessageDefaults.Items().Select(i => i.Key), list.Items.OrderBy(i => i.SortOrder).Select(i => i.Key));
        Assert.Equal(3, list.Items.Count(i => i.IsBuiltIn));
        Assert.Equal(Nine, list.UpdatedAt);
    }

    [Fact]
    public void A_New_Message_Gets_Its_Own_Key_And_Is_Never_Built_In() // AC-6.1
    {
        var items = Defaults();
        items.Insert(0, new StaffQuickMessageInput(null, "Stérilisation prête", StaffQuickMessageAudience.Desk, false, false));

        var list = StaffQuickMessageList.Create(ClinicId, items, Nine);

        var added = list.Items.Single(i => i.Label == "Stérilisation prête");
        Assert.StartsWith("custom-", added.Key);
        Assert.False(added.IsBuiltIn);
        Assert.Equal(0, added.SortOrder);
    }

    [Fact]
    public void A_Built_In_Can_Be_Renamed_And_Keeps_Its_Key() // AC-6.2
    {
        var items = Defaults()
            .Select(i => i.Key == StaffQuickMessageDefaults.PatientArrivedKey ? i with { Label = "Patient en salle" } : i)
            .ToList();

        var list = StaffQuickMessageList.Create(ClinicId, items, Nine);

        var arrived = list.Items.Single(i => i.Key == StaffQuickMessageDefaults.PatientArrivedKey);
        Assert.Equal("Patient en salle", arrived.Label);
        Assert.True(arrived.IsBuiltIn);
    }

    [Fact]
    public void A_Built_In_Cannot_Be_Removed_And_Is_Named_As_The_Cabinet_Calls_It() // AC-6.2
    {
        var list = StaffQuickMessageList.Create(ClinicId,
            Defaults().Select(i => i.Key == StaffQuickMessageDefaults.ChairHelpKey ? i with { Label = "Au secours" } : i), Nine);
        var withoutIt = Defaults().Where(i => i.Key != StaffQuickMessageDefaults.ChairHelpKey);

        var ex = Assert.Throws<ArgumentException>(() => list.Replace(withoutIt, Nine.AddHours(1)));

        Assert.StartsWith("« Au secours » ne peut pas être supprimé.", ex.Message);
        Assert.Equal(Nine, list.UpdatedAt);
    }

    [Fact]
    public void Removing_Two_Built_Ins_Names_Both()
    {
        var items = Defaults().Where(i => !StaffQuickMessageDefaults.BuiltInKeys.Contains(i.Key) || i.Key == StaffQuickMessageDefaults.ChairHelpKey);

        var ex = Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));

        Assert.Contains("« Patient arrivé »", ex.Message);
        Assert.Contains("« Urgence en salle d'attente »", ex.Message);
        Assert.Contains("ne peuvent pas être supprimés", ex.Message);
    }

    [Fact]
    public void A_Built_In_Urgency_Cannot_Change() // AC-6.2
    {
        var items = Defaults()
            .Select(i => i.Key == StaffQuickMessageDefaults.EmergencyWaitingKey ? i with { IsUrgent = false } : i);

        var ex = Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));

        Assert.Contains("ne peut pas être modifiée", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Label_Is_Refused(string label)
    {
        var items = Defaults();
        items.Add(new StaffQuickMessageInput(null, label, StaffQuickMessageAudience.Reply, false, false));

        Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));
    }

    [Fact]
    public void A_Label_Over_Forty_Characters_Is_Refused() // FR-4
    {
        var items = Defaults();
        items.Add(new StaffQuickMessageInput(null, new string('a', 41), StaffQuickMessageAudience.Reply, false, false));

        var ex = Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));

        Assert.Contains("dépasse 40 caractères", ex.Message);
    }

    [Fact]
    public void A_Label_Is_Unique_Whatever_Its_Case_And_Spaces() // FR-4
    {
        var items = Defaults();
        items.Add(new StaffQuickMessageInput(null, "  ok ", StaffQuickMessageAudience.Dentist, false, false));

        var ex = Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));

        Assert.Contains("figure deux fois", ex.Message);
    }

    [Fact]
    public void More_Than_Thirty_Messages_Are_Refused() // FR-4
    {
        var items = Defaults();
        items.AddRange(Enumerable.Range(1, 14).Select(n => new StaffQuickMessageInput(null, $"Message {n}", StaffQuickMessageAudience.Reply, false, false)));

        var ex = Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));

        Assert.Contains("Au plus 30", ex.Message);
    }

    [Fact]
    public void Exactly_Thirty_Messages_Are_Accepted()
    {
        var items = Defaults();
        items.AddRange(Enumerable.Range(1, 13).Select(n => new StaffQuickMessageInput(null, $"Message {n}", StaffQuickMessageAudience.Reply, false, false)));

        var list = StaffQuickMessageList.Create(ClinicId, items, Nine);

        Assert.Equal(30, list.Items.Count);
    }

    [Fact]
    public void An_Unknown_Audience_Is_Refused() // FR-4 — « for » required
    {
        var items = Defaults();
        items.Add(new StaffQuickMessageInput(null, "Sans destinataire", (StaffQuickMessageAudience)9, false, false));

        Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));
    }

    [Fact]
    public void The_Same_Key_Twice_Is_Refused()
    {
        var items = Defaults();
        items.Add(new StaffQuickMessageInput("reply-ok", "D'accord", StaffQuickMessageAudience.Reply, false, false));

        Assert.Throws<ArgumentException>(() => StaffQuickMessageList.Create(ClinicId, items, Nine));
    }

    [Fact]
    public void A_Replace_Swaps_The_Whole_List_In_The_Given_Order_And_Moves_UpdatedAt() // AC-6.1, AC-6.5
    {
        var list = StaffQuickMessageList.Create(ClinicId, Defaults(), Nine);
        var reversed = Defaults().AsEnumerable().Reverse().Where(i => i.Key != "reply-not-now").ToList();

        list.Replace(reversed, Nine.AddHours(1));

        Assert.Equal(16, list.Items.Count);
        Assert.Equal(reversed.Select(i => i.Key), list.Items.OrderBy(i => i.SortOrder).Select(i => i.Key));
        Assert.Equal(Nine.AddHours(1), list.UpdatedAt);
    }
}
