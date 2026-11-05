using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.SignIn;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Common.SignIn;

/// <summary>
/// sign-in-alerts: the vendor's e-mail reaches the vendor's alert inbox and nobody else, says what happened in a
/// line, and names the address that was typed — never text that is not one.
/// </summary>
public class SignInAlertNoticeTests
{
    private const string Oumayma = "benkhalifa.oumayma98@gmail.com";
    private const string Typed = "karim.benali@gmail.com";

    private static readonly SignInAlert Alert = new(
        SignInAlertKind.UnknownEmailRepeated,
        new SignInRefusal(
            SignInDoor.ClinicSignIn, SignInRefusalReason.UnknownEmail, SignInRefusalRules.ShownEmail(Typed), Typed, null,
            new DateTime(2026, 10, 31, 13, 56, 58, DateTimeKind.Utc)),
        "41.226.10.20",
        3);

    [Fact]
    public async Task With_No_Alert_Address_Configured_The_Active_Console_Accounts_Get_It()
    {
        var sent = new List<(string To, string Subject)>();

        await SignInAlertNotice.SendAsync(
            Accounts(Oumayma).Object, Sender(sent).Object, null, Alert, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { (Oumayma, SignInAlertNotice.Subject(SignInAlertKind.UnknownEmailRepeated)) }, sent);
    }

    // The same rule as the PC de secours and the backups: one inbox for every vendor alert.
    [Fact]
    public async Task A_Configured_Alert_Address_Takes_Precedence()
    {
        var sent = new List<(string To, string Subject)>();

        await SignInAlertNotice.SendAsync(
            Accounts(Oumayma).Object, Sender(sent).Object, "alertes@apexa.tn", Alert, NullLogger.Instance,
            CancellationToken.None);

        Assert.Equal(new[] { "alertes@apexa.tn" }, sent.Select(s => s.To));
    }

    [Fact]
    public async Task No_Smtp_Means_No_Attempt_And_No_Exception()
    {
        var sender = new Mock<ITransactionalEmailSender>();
        sender.SetupGet(s => s.IsConfigured).Returns(false);

        await SignInAlertNotice.SendAsync(
            Accounts(Oumayma).Object, sender.Object, null, Alert, NullLogger.Instance, CancellationToken.None);

        sender.Verify(s => s.SendAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Unreadable_Account_List_Sends_Nothing_And_Does_Not_Throw()
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(r => r.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var sent = new List<(string To, string Subject)>();

        await SignInAlertNotice.SendAsync(
            accounts.Object, Sender(sent).Object, null, Alert, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(sent);
    }

    // Who tried is what the vendor needs, to call them back (owner, 2026-11-05).
    [Fact]
    public void The_Message_Names_The_Address_That_Was_Typed()
    {
        var text = EmailLayout.PlainText(SignInAlertNotice.Compose(Alert));

        Assert.Contains($"E-mail saisi : {Typed}", text);
    }

    // The message prints what the rule shows, never the raw key: that may be a password typed into the wrong box.
    [Fact]
    public void Text_That_Is_Not_An_Address_Never_Reaches_The_Message()
    {
        const string password = "karim@dentiste2024";
        var typedPassword = Alert with
        {
            Refusal = Alert.Refusal with { ShownEmail = SignInRefusalRules.ShownEmail(password), EmailKey = password },
        };

        var text = EmailLayout.PlainText(SignInAlertNotice.Compose(typedPassword));

        Assert.DoesNotContain("dentiste2024", text);
        Assert.Contains($"E-mail saisi : {SignInRefusalRules.NotAnAddress}", text);
    }

    [Fact]
    public void The_Message_Says_When_Where_From_Where_And_How_Many_Times()
    {
        var text = EmailLayout.PlainText(SignInAlertNotice.Compose(Alert));

        Assert.Contains("31/10/2026 à 14:56", text); // Tunis time: UTC + 1
        Assert.Contains("connexion à l'application", text);
        Assert.Contains("41.226.10.20", text);
        Assert.Contains("aucun compte avec cet e-mail", text);
        Assert.Contains("3 essais", text);
    }

    // Whose account is targeted is the first thing the vendor needs; with no account, the reason already says so.
    [Fact]
    public void The_Account_Line_Names_The_Cabinet_And_Only_Appears_When_There_Is_One()
    {
        var targeted = Alert with
        {
            Kind = SignInAlertKind.AccountUnderAttack,
            Refusal = Alert.Refusal with
            {
                Reason = SignInRefusalReason.WrongSecret,
                AccountLabel = SignInRefusalRules.AccountLabel("Cabinet Bouka", "secretary"),
            },
        };

        Assert.Contains("Compte : Cabinet Bouka · secrétaire", EmailLayout.PlainText(SignInAlertNotice.Compose(targeted)));
        Assert.DoesNotContain("Compte :", EmailLayout.PlainText(SignInAlertNotice.Compose(Alert)));
    }

    // The address and the IP are a stranger's input: they must never reach a mail header.
    [Theory]
    [InlineData(SignInAlertKind.ConsoleRefused)]
    [InlineData(SignInAlertKind.AccountUnderAttack)]
    [InlineData(SignInAlertKind.AddressGuessing)]
    [InlineData(SignInAlertKind.UnknownEmailRepeated)]
    [InlineData(SignInAlertKind.DeactivatedAccountTried)]
    public void Every_Kind_Has_A_Fixed_Subject_And_A_Message(SignInAlertKind kind)
    {
        var subject = SignInAlertNotice.Subject(kind);
        var text = EmailLayout.PlainText(SignInAlertNotice.Compose(Alert with { Kind = kind }));

        Assert.StartsWith("APEXA — ", subject);
        Assert.DoesNotContain("@", subject);
        Assert.DoesNotContain("41.226", subject);
        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void Every_Door_And_Every_Reason_Has_A_French_Label()
    {
        foreach (var door in Enum.GetValues<SignInDoor>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SignInAlertNotice.DoorLabel(door)));
        }

        foreach (var reason in Enum.GetValues<SignInRefusalReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SignInAlertNotice.ReasonLabel(reason)));
        }

        foreach (var kind in Enum.GetValues<SignInAlertKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SignInAlertNotice.Title(kind)));
        }
    }

    private static Mock<IPlatformAccountRepository> Accounts(params string[] emails)
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(r => r.GetActiveEmailsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(emails);
        return accounts;
    }

    private static Mock<ITransactionalEmailSender> Sender(List<(string To, string Subject)> sent)
    {
        var sender = new Mock<ITransactionalEmailSender>();
        sender.SetupGet(s => s.IsConfigured).Returns(true);
        sender.Setup(s => s.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((to, subject, _, _) => sent.Add((to, subject)))
            .ReturnsAsync(TransactionalEmailResult.Sent);
        return sender;
    }
}
