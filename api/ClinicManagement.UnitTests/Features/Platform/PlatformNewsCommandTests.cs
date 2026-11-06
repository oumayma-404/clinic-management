using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Application.Features.Platform.Dtos;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console's news commands and the background sender, over mocked repositories and a mocked mail sender.
/// </summary>
public class PlatformNewsCommandTests
{
    private static readonly Guid Essai = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Payant = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly DateTime At = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IPlatformNewsRepository> _news = new();
    private readonly Mock<IClinicActivityRepository> _activity = new();
    private readonly Mock<IPlatformAccountRepository> _accounts = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IPublicAppUrlProvider> _appUrl = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly PlatformAccount _account = PlatformAccount.Create("oumayma@apexa.tn", "Oumayma", "hash");
    private readonly List<PlatformNewsBroadcast> _added = [];

    public PlatformNewsCommandTests()
    {
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent);
        _appUrl.SetupGet(a => a.IsConfigured).Returns(true);
        _appUrl.SetupGet(a => a.BaseUrl).Returns("https://app.apexa.tn");
        _accounts.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(_account);
        _news.Setup(n => n.AddAsync(It.IsAny<PlatformNewsBroadcast>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformNewsBroadcast, CancellationToken>((b, _) => _added.Add(b))
            .Returns(Task.CompletedTask);
        _activity.Setup(a => a.GetNewsAudienceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new PlatformNewsAudienceClinic(Essai, "Cabinet Essai", [new("a@cab.tn", "Dr A")]),
                new PlatformNewsAudienceClinic(Payant, "Cabinet Payant", [new("b@cab.tn", "Dr B"), new("b2@cab.tn", null)]),
            ]);
    }

    private FakePlatformSession Session => new() { AccountId = _account.Id, Email = _account.Email };

    private static ITenantScope SystemWide()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private SendPlatformNewsCommandHandler Send(ITenantScope? scope = null) =>
        new(_news.Object, _activity.Object, _accounts.Object, Session, _email.Object, _unitOfWork.Object,
            scope ?? SystemWide(), NullLogger<SendPlatformNewsCommandHandler>.Instance);

    private static SendPlatformNewsCommand Command(Guid? requestId = null) => new()
    {
        RequestId = requestId ?? Guid.NewGuid(),
        Subject = "Les nouveautés Apexa ✨",
        Items = [new PlatformNewsItemRequest("🚀", "Agenda plus rapide.")],
    };

    [Fact]
    public async Task Send_Records_The_Audience_Fixed_At_The_Click_And_Answers_With_Its_Counts()
    {
        var result = await Send().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var broadcast = Assert.Single(_added);
        Assert.Equal(new[] { "a@cab.tn", "b@cab.tn", "b2@cab.tn" }, broadcast.Recipients.Select(r => r.Email));
        Assert.All(broadcast.Recipients, r => Assert.Equal(PlatformNewsDeliveryStatus.Pending, r.Status));
        Assert.Equal("Oumayma", broadcast.SentByName);
        Assert.Equal((2, 3, false, true), (result.Value!.Clinics, result.Value.Recipients, result.Value.AlreadyQueued, result.Value.InProgress));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Repeated_Request_Id_Answers_The_First_Broadcast_And_Writes_Nothing()
    {
        var first = PlatformNewsBroadcast.Create(Guid.NewGuid(), "Objet", [new PlatformNewsItemInput("🚀", "x")],
            [new PlatformNewsRecipientInput(Essai, "Cabinet Essai", "a@cab.tn", null)], _account.Id, "Oumayma", At);
        _news.Setup(n => n.GetByRequestIdAsync(first.RequestId, It.IsAny<CancellationToken>())).ReturnsAsync(first);

        var result = await Send().Handle(Command(first.RequestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((first.Id, true, true), (result.Value!.BroadcastId, result.Value.AlreadyQueued, result.Value.InProgress));
        Assert.Empty(_added);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Two_Presses_Racing_Past_The_Read_Answer_The_Winner_Instead_Of_Failing()
    {
        var requestId = Guid.NewGuid();
        var winner = PlatformNewsBroadcast.Create(requestId, "Objet", [new PlatformNewsItemInput("🚀", "x")],
            [new PlatformNewsRecipientInput(Essai, "Cabinet Essai", "a@cab.tn", null)], _account.Id, "Oumayma", At);
        _news.SetupSequence(n => n.GetByRequestIdAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformNewsBroadcast?)null)
            .ReturnsAsync(winner);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(SaveFailure("23505"));

        var result = await Send().Handle(Command(requestId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal((winner.Id, true), (result.Value!.BroadcastId, result.Value.AlreadyQueued));
    }

    [Fact]
    public async Task A_Save_Failure_That_Is_Not_The_Request_Id_Race_Is_A_Failure_Not_A_Replay()
    {
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(SaveFailure("22001"));

        var result = await Send().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _news.Verify(n => n.GetByRequestIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static DbUpdateException SaveFailure(string sqlState) =>
        new("save failed", new Npgsql.PostgresException("refused", "ERROR", "ERROR", sqlState));

    [Fact]
    public async Task An_Install_That_Cannot_Send_Mail_Refuses_Before_Reading_The_Audience_Or_Writing()
    {
        _email.SetupGet(e => e.IsConfigured).Returns(false);

        var result = await Send().Handle(Command(), CancellationToken.None);

        Assert.Equal(PlatformNewsRules.EmailNotConfiguredCode, result.Code);
        Assert.Empty(_added);
        _activity.Verify(a => a.GetNewsAudienceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task No_Eligible_Admin_Address_Refuses_With_Its_Own_Code()
    {
        _activity.Setup(a => a.GetNewsAudienceAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PlatformNewsAudienceClinic(Essai, "Cabinet Essai", [])]);

        var result = await Send().Handle(Command(), CancellationToken.None);

        Assert.Equal(PlatformNewsRules.NoAudienceCode, result.Code);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task An_Invalid_Form_Or_A_Missing_Request_Id_Is_Refused_And_Nothing_Is_Written()
    {
        var empty = await Send().Handle(new SendPlatformNewsCommand { RequestId = Guid.NewGuid(), Subject = "S", Items = [] }, CancellationToken.None);
        var anonymous = await Send().Handle(Command(Guid.Empty), CancellationToken.None);

        Assert.Equal(PlatformNewsRules.InvalidCode, empty.Code);
        Assert.Equal(PlatformNewsRules.InvalidCode, anonymous.Code);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Sending_Without_A_Declared_Cross_Cabinet_Scope_Throws_Rather_Than_Reaching_Nobody()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Send(new TenantScope(NullLogger<TenantScope>.Instance)).Handle(Command(), CancellationToken.None));
    }

    private SendPlatformNewsTestCommandHandler Test() =>
        new(_accounts.Object, Session, _email.Object, _appUrl.Object, NullLogger<SendPlatformNewsTestCommandHandler>.Instance);

    [Fact]
    public async Task The_Test_Goes_To_The_Signed_In_Account_Only_With_A_Test_Subject()
    {
        string? to = null, subject = null;
        EmailContent? content = null;
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((t, s, c, _) => (to, subject, content) = (t, s, c))
            .ReturnsAsync(TransactionalEmailResult.Sent);

        var result = await Test().Handle(
            new SendPlatformNewsTestCommand { Subject = "Objet", Items = [new PlatformNewsItemRequest("🚀", "x")] }, CancellationToken.None);

        Assert.Equal("oumayma@apexa.tn", result.Value!.TestSentTo);
        Assert.Equal(("oumayma@apexa.tn", "[Test] Objet"), (to, subject));
        Assert.Equal("Bonjour Oumayma,", content!.Greeting);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, TransactionalEmailOutcome.Sent, PlatformNewsRules.EmailNotConfiguredCode)]
    [InlineData(true, TransactionalEmailOutcome.Failed, SendPlatformNewsTestCommandHandler.SendFailedCode)]
    public async Task A_Test_That_Cannot_Leave_Says_Why_With_A_Code(bool configured, TransactionalEmailOutcome outcome, string code)
    {
        _email.SetupGet(e => e.IsConfigured).Returns(configured);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionalEmailResult(outcome, "refused"));

        var result = await Test().Handle(
            new SendPlatformNewsTestCommand { Subject = "Objet", Items = [new PlatformNewsItemRequest("🚀", "x")] }, CancellationToken.None);

        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task The_Preview_Greets_The_Console_Account_And_Inlines_The_Logo()
    {
        var handler = new PreviewPlatformNewsQueryHandler(
            _accounts.Object, Session, _appUrl.Object, NullLogger<PreviewPlatformNewsQueryHandler>.Instance);

        var result = await handler.Handle(
            new PreviewPlatformNewsQuery { Subject = "Objet", Items = [new PlatformNewsItemRequest("🚀", "Agenda")] }, CancellationToken.None);

        Assert.Contains("Bonjour Oumayma,", result.Value!.NewsHtml);
        Assert.Contains("Cabinet Exemple", result.Value.NewsHtml);
        Assert.DoesNotContain("cid:", result.Value.NewsHtml);
    }

    private PlatformNewsBroadcast Pending(params string[] emails) =>
        PlatformNewsBroadcast.Create(Guid.NewGuid(), "Les nouveautés", [new PlatformNewsItemInput("🚀", "Agenda")],
            emails.Select(e => new PlatformNewsRecipientInput(Essai, "Cabinet " + e[0], e, "Dr " + e[0])), _account.Id, "Oumayma", At);

    private PlatformNewsDispatcher Dispatcher(PlatformNewsBroadcast broadcast)
    {
        _news.Setup(n => n.GetWithRecipientsAsync(broadcast.Id, It.IsAny<CancellationToken>())).ReturnsAsync(broadcast);
        return new PlatformNewsDispatcher(_news.Object, _email.Object, _appUrl.Object, _unitOfWork.Object,
            NullLogger<PlatformNewsDispatcher>.Instance);
    }

    [Fact]
    public async Task The_Sender_Tries_Every_Address_Records_Each_Outcome_And_Completes()
    {
        var broadcast = Pending("a@cab.tn", "b@cab.tn", "c@cab.tn");
        _email.Setup(e => e.SendAsync("b@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        _email.Setup(e => e.SendAsync("c@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("550 mailbox unavailable"));

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        var byEmail = broadcast.Recipients.ToDictionary(r => r.Email);
        Assert.Equal(PlatformNewsDeliveryStatus.Sent, byEmail["a@cab.tn"].Status);
        Assert.Equal((PlatformNewsDeliveryStatus.Failed, "smtp down"), (byEmail["b@cab.tn"].Status, byEmail["b@cab.tn"].Error));
        Assert.Equal((PlatformNewsDeliveryStatus.Failed, "550 mailbox unavailable"), (byEmail["c@cab.tn"].Status, byEmail["c@cab.tn"].Error));
        Assert.NotNull(broadcast.CompletedAtUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(7));
    }

    [Fact]
    public async Task Each_Recipient_Is_Claimed_And_Saved_Before_Its_Email_Leaves()
    {
        var broadcast = Pending("a@cab.tn");
        var statusAtSend = (PlatformNewsDeliveryStatus?)null;
        var savesBeforeSend = 0;
        var saves = 0;
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => saves++).ReturnsAsync(1);
        _email.Setup(e => e.SendAsync("a@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback(() => (statusAtSend, savesBeforeSend) = (broadcast.Recipients.Single().Status, saves))
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        Assert.Equal((PlatformNewsDeliveryStatus.Sending, 1), (statusAtSend, savesBeforeSend));
    }

    [Fact]
    public async Task A_Run_That_Loses_A_Claim_To_Another_Run_Stops_Without_Sending()
    {
        var broadcast = Pending("a@cab.tn", "b@cab.tn");
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new ConflictException("conflict"));

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.StopTracking(broadcast.Recipients.First()), Times.Once);
    }

    [Fact]
    public async Task A_Claim_Left_By_A_Dead_Run_Ends_As_Failed_And_Is_Never_Resent()
    {
        var broadcast = Pending("a@cab.tn", "b@cab.tn");
        var stale = broadcast.Recipients.First();
        broadcast.Claim(stale.Id, DateTime.UtcNow - PlatformNewsDispatcher.ClaimLifetime - TimeSpan.FromMinutes(1));

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        Assert.Equal(PlatformNewsDeliveryStatus.Failed, stale.Status);
        _email.Verify(e => e.SendAsync("a@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendAsync("b@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(broadcast.CompletedAtUtc);
    }

    [Fact]
    public async Task A_Fresh_Claim_Is_Left_To_Its_Run_And_The_Send_Stays_Open()
    {
        var broadcast = Pending("a@cab.tn");
        broadcast.Claim(broadcast.Recipients.Single().Id, DateTime.UtcNow);

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        Assert.Equal(PlatformNewsDeliveryStatus.Sending, broadcast.Recipients.Single().Status);
        Assert.Null(broadcast.CompletedAtUtc);
        _email.Verify(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_Sweep_Resumes_Every_Unfinished_Send_And_Stops_Quietly_On_A_Failure()
    {
        var first = Pending("a@cab.tn");
        var broken = Guid.NewGuid();
        var never = Guid.NewGuid();
        var dispatcher = Dispatcher(first);
        _news.Setup(n => n.ListUnfinishedIdsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([first.Id, broken, never]);
        _news.Setup(n => n.GetWithRecipientsAsync(broken, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db"));

        await dispatcher.ResumeUnfinishedAsync(CancellationToken.None);

        Assert.NotNull(first.CompletedAtUtc);
        _news.Verify(n => n.GetWithRecipientsAsync(never, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Resumed_Send_Never_Mails_An_Address_Already_Sent()
    {
        var broadcast = Pending("a@cab.tn", "b@cab.tn");
        broadcast.RecordSent(broadcast.Recipients.First().Id, At);

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        _email.Verify(e => e.SendAsync("a@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(e => e.SendAsync("b@cab.tn", It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Each_Recipient_Is_Greeted_By_Name_And_Told_Which_Cabinet_It_Administers()
    {
        var broadcast = Pending("a@cab.tn");
        EmailContent? content = null;
        _email.Setup(e => e.SendAsync("a@cab.tn", "Les nouveautés", It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((_, _, c, _) => content = c)
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        Assert.Equal("Bonjour Dr a,", content!.Greeting);
        Assert.Equal("Vous recevez cet e-mail en tant qu'administrateur du cabinet Cabinet a.", content.Note);
        Assert.Equal("https://app.apexa.tn/login", content.Action?.Url);
    }

    [Fact]
    public async Task An_Unconfigured_Sender_Records_Every_Address_As_Failed_With_A_French_Reason()
    {
        var broadcast = Pending("a@cab.tn");
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.NotConfigured);

        await Dispatcher(broadcast).DispatchAsync(broadcast.Id, CancellationToken.None);

        var recipient = Assert.Single(broadcast.Recipients);
        Assert.Equal((PlatformNewsDeliveryStatus.Failed, PlatformNewsRules.EmailNotConfigured), (recipient.Status, recipient.Error));
    }

    [Fact]
    public async Task The_History_Reports_A_Send_Without_An_End_As_In_Progress()
    {
        _news.Setup(n => n.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new PlatformNewsSummaryRow(Guid.NewGuid(), "En cours", At, null, "Oumayma", 2, 3, 1, 0, []),
            new PlatformNewsSummaryRow(Guid.NewGuid(), "Fini", At, At, "Oumayma", 2, 3, 2, 1, ["Cabinet Payant"]),
        ]);

        var result = await new ListPlatformNewsQueryHandler(_news.Object, NullLogger<ListPlatformNewsQueryHandler>.Instance)
            .Handle(new ListPlatformNewsQuery(), CancellationToken.None);

        var rows = result.Value!;
        Assert.Equal(new[] { true, false }, rows.Select(r => r.InProgress));
        Assert.Equal(new[] { "Cabinet Payant" }, rows[1].FailedClinics);
    }
}
