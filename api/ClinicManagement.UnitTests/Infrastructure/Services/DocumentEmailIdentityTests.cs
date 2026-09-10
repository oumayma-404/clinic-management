using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// A document leaves the cabinet as the practitioner who sent it, and the three headers that says are decided
/// in one place. Both directions are asserted: the practitioner reaches <c>From</c> when we know them, and the
/// cabinet's own identity is untouched when we do not — which is every document queued before this shipped.
/// </summary>
public class DocumentEmailIdentityTests
{
    private const string Cabinet = "cabinet@ibnkhaldoun.tn";
    private const string CabinetName = "Cabinet Ibn Khaldoun";
    private const string Doctor = "salma.benyoussef@ibnkhaldoun.tn";
    private const string DoctorName = "Dr Salma Ben Youssef";

    [Fact]
    public void Practitioner_Is_On_From_With_The_Cabinet_As_Sender()
    {
        var identity = DocumentEmailIdentity.ForPractitioner(Cabinet, CabinetName, Doctor, DoctorName);

        Assert.Equal(Doctor, identity.FromAddress);
        Assert.Equal(DoctorName, identity.FromDisplayName);
        Assert.Equal(Cabinet, identity.SenderAddress); // RFC 5322 Sender is the mailbox that actually transmitted it
        Assert.Equal(Doctor, identity.ReplyToAddress); // a reply must reach the practitioner, not the install
        Assert.True(identity.SpeaksForSomeoneElse);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("two@addresses@example.tn")]
    public void An_Unusable_Practitioner_Address_Leaves_The_Cabinet_Identity_Untouched(string? practitioner)
    {
        var identity = DocumentEmailIdentity.ForPractitioner(Cabinet, CabinetName, practitioner, DoctorName);

        Assert.Equal(Cabinet, identity.FromAddress);
        Assert.Equal(CabinetName, identity.FromDisplayName);
        Assert.Null(identity.SenderAddress);
        Assert.Null(identity.ReplyToAddress); // there is no address to reply to
        Assert.False(identity.SpeaksForSomeoneElse); // nothing was changed, so nothing can be refused
    }

    [Fact]
    public void The_Same_Mailbox_Gets_The_Practitioners_Name_And_No_Extra_Headers()
    {
        var identity = DocumentEmailIdentity.ForPractitioner(Cabinet, CabinetName, "  CABINET@ibnkhaldoun.tn ", DoctorName);

        Assert.Equal(Cabinet, identity.FromAddress);
        Assert.Equal(DoctorName, identity.FromDisplayName);
        Assert.Null(identity.SenderAddress); // From is already the authenticated mailbox
        Assert.Null(identity.ReplyToAddress); // a Reply-To pointing at From is noise
        Assert.False(identity.SpeaksForSomeoneElse);
    }

    [Fact]
    public void The_Fallback_Keeps_The_Practitioner_Reachable()
    {
        var identity = DocumentEmailIdentity.Configured(Cabinet, CabinetName, Doctor, DoctorName);

        Assert.Equal(Cabinet, identity.FromAddress); // a relay that refused the practitioner's From must not be given it twice
        Assert.Equal(DoctorName, identity.FromDisplayName); // forbidden to send AS her, not to say she wrote it
        Assert.Null(identity.SenderAddress); // From is the authenticated mailbox again
        Assert.Equal(Doctor, identity.ReplyToAddress); // the whole point of the fallback is that a reply still reaches them
        Assert.Equal(DoctorName, identity.ReplyToDisplayName);
        Assert.False(identity.SpeaksForSomeoneElse); // so the sender cannot fall back a second time and loop
    }

    [Fact]
    public void The_Fallback_Without_A_Practitioner_Is_The_Old_Behaviour_Exactly()
    {
        var identity = DocumentEmailIdentity.Configured(Cabinet, CabinetName);

        Assert.Equal(Cabinet, identity.FromAddress);
        Assert.Equal(CabinetName, identity.FromDisplayName);
        Assert.Null(identity.SenderAddress);
        Assert.Null(identity.ReplyToAddress);
    }

    [Fact]
    public void A_Nameless_Practitioner_Still_Reaches_From()
    {
        var identity = DocumentEmailIdentity.ForPractitioner(Cabinet, CabinetName, Doctor, "   ");

        Assert.Equal(Doctor, identity.FromAddress);
        Assert.Null(identity.FromDisplayName); // a blank display name is no name, not an empty one
        Assert.Equal(Doctor, identity.ReplyToAddress);
    }

    [Fact]
    public void A_Nameless_Cabinet_Is_Carried_Through()
    {
        var identity = DocumentEmailIdentity.ForPractitioner(Cabinet, null, null, null);

        Assert.Equal(Cabinet, identity.FromAddress);
        Assert.Null(identity.FromDisplayName);
    }
}
