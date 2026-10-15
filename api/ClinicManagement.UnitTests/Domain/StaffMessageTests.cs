using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

public class StaffMessageTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime At = new(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
    private const string Sender = "local|sender";

    private static StaffMessage Create(string? body, StaffQuickMessageSnapshot? quick = null) =>
        StaffMessage.Create(ClinicId, StaffConversation.Clinic, Sender, body, quick, null, null, At);

    [Fact]
    public void Body_Is_Trimmed_And_Keeps_Its_Inner_Line_Breaks() // AC-2.3, FR-3
    {
        var message = Create("  Bonjour\nla suite  ");

        Assert.Equal("Bonjour\nla suite", message.Body);
        Assert.Equal("clinic", message.ConversationKey);
        Assert.Equal(At, message.SentAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    public void A_Blank_Message_With_No_Quick_Message_Is_Refused(string? body) // AC-2.3
    {
        Assert.Throws<ArgumentException>(() => Create(body));
    }

    [Fact]
    public void A_Quick_Message_Needs_No_Text_And_Keeps_Its_Label_And_Urgency_As_Sent() // AC-6.4
    {
        var message = Create(null, new StaffQuickMessageSnapshot("chair-help", "Besoin d'aide au fauteuil", IsUrgent: true));

        Assert.Null(message.Body);
        Assert.Equal("chair-help", message.QuickMessageKey);
        Assert.Equal("Besoin d'aide au fauteuil", message.QuickMessageLabel);
        Assert.True(message.IsUrgent);
    }

    [Fact]
    public void The_Limit_Counts_Characters_After_Trimming() // AC-2.3
    {
        Assert.Equal(StaffMessage.BodyMaxLength, Create(new string('a', StaffMessage.BodyMaxLength) + "   ").Body!.Length);
        Assert.Throws<ArgumentException>(() => Create(new string('a', StaffMessage.BodyMaxLength + 1)));
    }
}
