using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>The « nouveau cabinet inscrit » notice: it reaches the active console accounts and nobody else.</summary>
public class ConsoleTeamSignupNoticeTests
{
    private const string Oumayma = "benkhalifa.oumayma98@gmail.com";
    private const string Eya = "eyaboukadhaba@gmail.com";
    private const string Visitor = "dr.benali@cabinet.tn";

    private static readonly SignupNoticeFacts Facts =
        new("Cabinet Benali", "Dr Sami Benali", Visitor, "+216 22 123 456");

    [Fact]
    public async Task Each_Active_Console_Account_Gets_Its_Own_Message_And_Nobody_Else_Does()
    {
        var sent = new List<(string To, string Subject)>();
        var sender = Sender(sent);

        await ConsoleTeamSignupNotice.SendAsync(
            Accounts(Oumayma, Eya).Object, sender.Object, Facts, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { (Oumayma, ConsoleTeamSignupNotice.Subject), (Eya, ConsoleTeamSignupNotice.Subject) }, sent);
        Assert.DoesNotContain(sent, s => s.To == Visitor);
    }

    [Fact]
    public async Task No_Active_Console_Account_Means_No_Message_At_All()
    {
        var sent = new List<(string To, string Subject)>();

        await ConsoleTeamSignupNotice.SendAsync(
            Accounts().Object, Sender(sent).Object, Facts, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(sent);
    }

    [Fact]
    public void The_Message_Carries_The_Cabinet_The_Person_The_Email_And_The_Phone()
    {
        var text = EmailLayout.PlainText(ConsoleTeamSignupNotice.Compose(Facts));

        Assert.Contains("Cabinet Benali", text);
        Assert.Contains("Dr Sami Benali", text);
        Assert.Contains(Visitor, text);
        Assert.Contains("+216 22 123 456", text);
    }

    [Fact]
    public void A_Missing_Phone_Reads_Non_Renseigne()
    {
        var text = EmailLayout.PlainText(ConsoleTeamSignupNotice.Compose(Facts with { Phone = "  " }));

        Assert.Contains("non renseigné", text);
    }

    // The cabinet name is a stranger's input: it must never reach a mail header.
    [Fact]
    public void The_Subject_Is_Fixed_Text()
    {
        Assert.DoesNotContain("Cabinet Benali", ConsoleTeamSignupNotice.Subject);
    }

    [Fact]
    public async Task One_Failed_Send_Still_Reaches_The_Other_Account_And_Nothing_Throws()
    {
        var reached = new List<string>();
        var sender = new Mock<ITransactionalEmailSender>();
        sender.Setup(s => s.SendAsync(Oumayma, It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        sender.Setup(s => s.SendAsync(Eya, It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((to, _, _, _) => reached.Add(to))
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await ConsoleTeamSignupNotice.SendAsync(
            Accounts(Oumayma, Eya).Object, sender.Object, Facts, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { Eya }, reached);
    }

    [Fact]
    public async Task An_Unreadable_Account_List_Sends_Nothing_And_Does_Not_Throw()
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(r => r.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("db down"));
        var sent = new List<(string To, string Subject)>();

        await ConsoleTeamSignupNotice.SendAsync(
            accounts.Object, Sender(sent).Object, Facts, NullLogger.Instance, CancellationToken.None);

        Assert.Empty(sent);
    }

    // End to end through the real verification: the visitor gets the welcome only, the team the notice only.
    [Fact]
    public async Task Verifying_A_Signup_Sends_The_Notice_To_The_Team_And_Only_The_Welcome_To_The_Visitor()
    {
        const string rawToken = "raw-token-for-the-test";
        var signup = ClinicSignup.Create(
            "Cabinet Benali", "Dr Sami Benali", Visitor, "hashed",
            ClinicSignup.HashToken(rawToken), DateTime.UtcNow, phone: "+216 22 123 456");

        var signups = new Mock<IClinicSignupRepository>();
        signups.Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(signup);

        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var appUrl = new Mock<IPublicAppUrlProvider>();
        appUrl.SetupGet(u => u.BaseUrl).Returns("https://app.example.tn");

        var sent = new List<(string To, string Subject)>();

        var handler = new VerifyClinicSignUpCommandHandler(
            signups.Object,
            new Mock<IClinicRepository>().Object,
            users.Object,
            new Mock<IDoctorRepository>().Object,
            new Mock<IProcedureTypeRepository>().Object,
            new Mock<IClinicCatalogSeeder>().Object,
            new Mock<IClinicSubscriptionRepository>().Object,
            new Mock<ISubscriptionPolicy>().Object,
            new Mock<IMessagingAllowanceRepository>().Object,
            new Mock<IMessagingAllowancePolicy>().Object,
            new Mock<IUnitOfWork>().Object,
            Sender(sent).Object,
            appUrl.Object,
            Accounts(Oumayma, Eya).Object,
            new Mock<ILogger<VerifyClinicSignUpCommandHandler>>().Object);

        var result = await handler.Handle(new VerifyClinicSignUpCommand { Token = rawToken }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(
            new[] { Oumayma, Eya },
            sent.Where(s => s.Subject == ConsoleTeamSignupNotice.Subject).Select(s => s.To));
        Assert.All(sent.Where(s => s.To == Visitor), s => Assert.NotEqual(ConsoleTeamSignupNotice.Subject, s.Subject));
        Assert.Single(sent, s => s.To == Visitor);
    }

    private static Mock<IPlatformAccountRepository> Accounts(params string[] emails)
    {
        var accounts = new Mock<IPlatformAccountRepository>();
        accounts.Setup(r => r.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(emails);
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
