using ClinicManagement.DesktopShell;
using Xunit;

namespace ClinicManagement.DesktopShell.Tests;

/// <summary>
/// `notify:{json}` comes from web content and lands in Windows' notification centre (staff-chat AC-4.3): only a
/// well-formed, bounded message gets through.
/// </summary>
public class ShellNotifyMessageTests
{
    [Fact]
    public void A_Chat_Message_Is_Read()
    {
        var m = ShellNotifyMessage.TryParse("""{"title":"Dr Salma Ben Youssef","body":"Patient arrivé · RDV 09:30","tag":"desk:local%7Cabc","urgent":false}""");

        Assert.NotNull(m);
        Assert.Equal("Dr Salma Ben Youssef", m!.Title);
        Assert.Equal("Patient arrivé · RDV 09:30", m.Body);
        Assert.Equal("desk:local%7Cabc", m.Conversation);
        Assert.False(m.Urgent);
    }

    [Fact]
    public void Urgent_Is_Only_A_Literal_True()
    {
        Assert.True(ShellNotifyMessage.TryParse("""{"title":"A","body":"B","tag":"clinic","urgent":true}""")!.Urgent);
        Assert.False(ShellNotifyMessage.TryParse("""{"title":"A","body":"B","tag":"clinic","urgent":"true"}""")!.Urgent);
        Assert.False(ShellNotifyMessage.TryParse("""{"title":"A","body":"B","tag":"clinic"}""")!.Urgent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"body":"B","tag":"clinic"}""")]
    [InlineData("""{"title":"A","tag":"clinic"}""")]
    [InlineData("""{"title":"A","body":"B"}""")]
    [InlineData("""{"title":" ","body":"B","tag":"clinic"}""")]
    [InlineData("""{"title":42,"body":"B","tag":"clinic"}""")]
    [InlineData("""{"title":"A\nB","body":"B","tag":"clinic"}""")]
    public void Anything_Else_Is_Refused(string? json)
    {
        Assert.Null(ShellNotifyMessage.TryParse(json));
    }

    [Fact]
    public void Each_Field_Is_Bounded()
    {
        string Json(int title, int body, int tag) =>
            $$"""{"title":"{{new string('t', title)}}","body":"{{new string('b', body)}}","tag":"{{new string('c', tag)}}"}""";

        Assert.NotNull(ShellNotifyMessage.TryParse(Json(ShellNotifyMessage.MaxTitle, ShellNotifyMessage.MaxBody, ShellNotifyMessage.MaxConversation)));
        Assert.Null(ShellNotifyMessage.TryParse(Json(ShellNotifyMessage.MaxTitle + 1, 1, 1)));
        Assert.Null(ShellNotifyMessage.TryParse(Json(1, ShellNotifyMessage.MaxBody + 1, 1)));
        Assert.Null(ShellNotifyMessage.TryParse(Json(1, 1, ShellNotifyMessage.MaxConversation + 1)));
    }

    [Fact]
    public void A_Long_Conversation_Key_Still_Makes_A_Windows_Sized_Tag()
    {
        var tag = ShellNotifications.TagOf("dm:" + new string('x', 290));

        Assert.True(tag.Length <= 64);
        Assert.Equal(tag, ShellNotifications.TagOf("dm:" + new string('x', 290)));
        Assert.NotEqual(tag, ShellNotifications.TagOf("clinic"));
    }
}
