using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Platform.Auth;
using Xunit;

namespace ClinicManagement.UnitTests.Common.SignIn;

/// <summary>
/// sign-in-alerts: what counts as a refusal, how an address is shown, and when the vendor is told. The thresholds
/// are what decide whether the owner's inbox carries signal or noise, so each one is pinned on both sides.
/// </summary>
public class SignInRefusalRulesTests
{
    private const string Address = "41.226.10.20";

    // Who tried is what the vendor needs, to call them back (owner, 2026-11-05).
    [Theory]
    [InlineData("karim.benali@gmail.com", "karim.benali@gmail.com")]
    [InlineData("  Karim.Benali@GMAIL.com ", "karim.benali@gmail.com")]
    [InlineData("a@b.tn", "a@b.tn")]
    [InlineData("dr.ben-salah+rdv@cabinet.com.tn", "dr.ben-salah+rdv@cabinet.com.tn")]
    [InlineData("", SignInRefusalRules.Blank)]
    [InlineData("   ", SignInRefusalRules.Blank)]
    [InlineData(null, SignInRefusalRules.Blank)]
    public void A_Typed_Address_Is_Shown_In_Full(string? typed, string expected)
    {
        Assert.Equal(expected, SignInRefusalRules.ShownEmail(typed));
    }

    // Typed into the e-mail box by mistake, a password must not be written down — not even part of it.
    [Theory]
    [InlineData("Tunis2024!")]
    [InlineData("Karim@Dentiste2024")]
    [InlineData("P@ssw0rd")]
    [InlineData("@gmail.com")]
    [InlineData("karim.gmail.com")]
    public void Text_That_Is_Not_An_Address_Is_Not_Written_Down(string typed)
    {
        Assert.Equal(SignInRefusalRules.NotAnAddress, SignInRefusalRules.ShownEmail(typed));
    }

    // Why the rule is stricter than the one that stores an address: that one takes a password with an « @ » in it.
    [Fact]
    public void The_Storing_Rule_Would_Take_A_Password_For_An_Address()
    {
        Assert.Null(EmailAddressInput.Read("Karim@Dentiste2024", out _));
    }

    // What is shown reaches a log line and an e-mail: it must not be able to break either.
    [Theory]
    [InlineData("ab\r\ncd@evil.tn")]
    [InlineData("ab@evil.tn\r\nSign-in refused")]
    [InlineData("<script>@evil.tn")]
    [InlineData("\"a b\"@evil.tn")]
    [InlineData("Ünïcödé.Nàme@Éxample.tn")]
    public void An_Address_That_Could_Break_A_Log_Line_Or_An_Email_Is_Not_Shown(string typed)
    {
        Assert.Equal(SignInRefusalRules.NotAnAddress, SignInRefusalRules.ShownEmail(typed));
    }

    [Fact]
    public void An_Address_Longer_Than_Smtp_Carries_Is_Not_Shown()
    {
        var typed = "ab@" + string.Join('.', Enumerable.Repeat(new string('x', 60), 5)) + ".tn";

        Assert.Equal(SignInRefusalRules.NotAnAddress, SignInRefusalRules.ShownEmail(typed));
    }

    [Theory]
    [InlineData(ClinicAuthRefusals.InvalidCredentials, false, false, false, SignInRefusalReason.UnknownEmail)]
    [InlineData(ClinicAuthRefusals.InvalidCredentials, false, true, false, SignInRefusalReason.WrongSecret)]
    [InlineData(ClinicAuthRefusals.InvalidCredentials, true, false, false, SignInRefusalReason.EmptyFields)]
    [InlineData(ClinicAuthRefusals.TotpInvalid, false, true, false, SignInRefusalReason.WrongSecret)]
    [InlineData(ClinicAuthRefusals.TooManyAttempts, false, true, false, SignInRefusalReason.Locked)]
    [InlineData(ClinicAuthRefusals.AccountDisabled, false, true, false, SignInRefusalReason.Deactivated)]
    [InlineData(ClinicAuthRefusals.AccountDisabled, false, true, true, SignInRefusalReason.PendingActivation)]
    public void Each_Refusal_Code_Reads_As_What_Happened(
        string code, bool blank, bool exists, bool pending, SignInRefusalReason expected)
    {
        Assert.Equal(expected, SignInRefusalRules.Classify(code, blank, exists, pending));
    }

    [Theory]
    [InlineData(ClinicAuthRefusals.TotpRequired)]
    [InlineData(ClinicAuthRefusals.TotpEnrolmentRequired)]
    [InlineData(ClinicAuthRefusals.RetiredRelayAdminsOnly)]
    [InlineData(ClinicAuthRefusals.PasswordPolicy)]
    [InlineData(PlatformAuthRefusals.NoSession)]
    public void A_Step_Of_Signing_In_Is_Not_A_Refusal(string code)
    {
        Assert.False(SignInRefusalRules.IsRecorded(code));
        Assert.Null(SignInRefusalRules.Classify(code, false, true, false));
    }

    // The console's refusals are classified through the clinic's constants today; this is what notices a rename.
    [Fact]
    public void Every_Console_Refusal_Code_Is_Recorded_Like_The_Clinics()
    {
        Assert.True(SignInRefusalRules.IsRecorded(PlatformAuthRefusals.InvalidCredentials));
        Assert.True(SignInRefusalRules.IsRecorded(PlatformAuthRefusals.TotpInvalid));
        Assert.True(SignInRefusalRules.IsRecorded(PlatformAuthRefusals.TooManyAttempts));
        Assert.True(SignInRefusalRules.IsRecorded(PlatformAuthRefusals.AccountDisabled));
    }

    [Fact]
    public void Every_Console_Refusal_Is_Due_At_Once()
    {
        var due = SignInRefusalRules.Due(Refusal(SignInDoor.ConsoleSignIn, SignInRefusalReason.WrongSecret), Address, 1, 1);

        Assert.Equal(SignInAlertKind.ConsoleRefused, Assert.Single(due).Kind);
    }

    [Fact]
    public void One_Account_Is_Due_At_The_Fifth_Refusal_And_Not_Before()
    {
        var refusal = Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.WrongSecret);

        Assert.Empty(SignInRefusalRules.Due(refusal, Address, SignInRefusalRules.AccountThreshold - 1, 1));
        Assert.Equal(
            SignInAlertKind.AccountUnderAttack,
            Assert.Single(SignInRefusalRules.Due(refusal, Address, SignInRefusalRules.AccountThreshold, 1)).Kind);
    }

    // The 31/10 case: three tries with an address nobody owns.
    [Fact]
    public void An_Address_With_No_Account_Is_Due_At_The_Third_Refusal_And_Not_Before()
    {
        var refusal = Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.UnknownEmail);

        Assert.Empty(SignInRefusalRules.Due(refusal, Address, SignInRefusalRules.UnknownEmailThreshold - 1, 1));
        Assert.Equal(
            SignInAlertKind.UnknownEmailRepeated,
            Assert.Single(SignInRefusalRules.Due(refusal, Address, SignInRefusalRules.UnknownEmailThreshold, 1)).Kind);
    }

    [Fact]
    public void One_Address_Is_Due_At_The_Tenth_Refusal_Whatever_The_Account()
    {
        var refusal = Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.UnknownEmail);

        Assert.DoesNotContain(
            SignInRefusalRules.Due(refusal, Address, 1, SignInRefusalRules.AddressThreshold - 1),
            d => d.Kind == SignInAlertKind.AddressGuessing);
        Assert.Contains(
            SignInRefusalRules.Due(refusal, Address, 1, SignInRefusalRules.AddressThreshold),
            d => d.Kind == SignInAlertKind.AddressGuessing && d.QuietKey == $"address:{Address}");
    }

    [Fact]
    public void A_Deactivated_Account_Presenting_Its_Password_Is_Due_At_Once()
    {
        var due = SignInRefusalRules.Due(Refusal(SignInDoor.ClinicSignIn, SignInRefusalReason.Deactivated), Address, 1, 1);

        Assert.Equal(SignInAlertKind.DeactivatedAccountTried, Assert.Single(due).Kind);
    }

    // Somebody waiting for their administrator has done nothing wrong; a typo is not an attack.
    [Theory]
    [InlineData(SignInRefusalReason.PendingActivation)]
    [InlineData(SignInRefusalReason.EmptyFields)]
    [InlineData(SignInRefusalReason.WrongSecret)]
    public void A_Single_Ordinary_Refusal_Tells_Nobody(SignInRefusalReason reason)
    {
        Assert.Empty(SignInRefusalRules.Due(Refusal(SignInDoor.ClinicSignIn, reason), Address, 1, 1));
    }

    [Theory]
    [InlineData("Cabinet Bouka", "secretary", "Cabinet Bouka · secrétaire")]
    [InlineData("Cabinet Bouka", "admin", "Cabinet Bouka · administrateur")]
    [InlineData(null, "doctor", "médecin")]
    [InlineData("  ", null, "compte")]
    public void An_Account_Is_Named_By_Its_Cabinet_And_Role(string? clinic, string? role, string expected)
    {
        Assert.Equal(expected, SignInRefusalRules.AccountLabel(clinic, role));
    }

    private static SignInRefusal Refusal(SignInDoor door, SignInRefusalReason reason) =>
        new(door, reason, "karim.benali@gmail.com", "karim.benali@gmail.com", null,
            new DateTime(2026, 10, 31, 13, 56, 58, DateTimeKind.Utc));
}
