using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Feedback;
using ClinicManagement.Application.Features.Feedback.Commands;
using ClinicManagement.Application.Features.Feedback.Queries;
using ClinicManagement.Application.Features.Platform.Queries;
using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Feedback;

/// <summary>
/// « Donner mon avis » and the week-one question (<c>guided-first-days</c> E): when the question is put, what an
/// answer must contain, who is told, and what the console's « Retours » may show.
/// </summary>
public class FeedbackTests
{
    private static readonly Guid ClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string Oumayma = "console-a@apexa.tn";
    private const string Eya = "console-b@apexa.tn";

    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IFeedbackRepository> _feedback = new();
    private readonly Mock<IClinicActivityRepository> _activity = new();
    private readonly Mock<IPlatformAccountRepository> _accounts = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();

    private readonly List<ProductFeedback> _added = [];
    private readonly List<UserFeedbackPrompt> _addedPrompts = [];
    private readonly List<string> _mailedTo = [];
    private UserFeedbackPrompt? _prompt;
    private readonly User _secretary;

    public FeedbackTests()
    {
        _secretary = User.CreateLocalUser(ClinicId, User.RoleSecretary, "amel@cabinet.tn", "HASH", "Amel");
        SetCreatedAt(_secretary, DateTime.UtcNow.AddDays(-10));

        _context.Setup(c => c.GetUserId()).Returns(_secretary.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(_secretary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_secretary);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Dr Geller", city: "Tunis"));
        _feedback.Setup(r => r.AddAsync(It.IsAny<ProductFeedback>(), It.IsAny<CancellationToken>()))
            .Callback<ProductFeedback, CancellationToken>((f, _) => _added.Add(f))
            .Returns(Task.CompletedTask);
        _feedback.Setup(r => r.GetPromptAsync(_secretary.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => _prompt);
        _feedback.Setup(r => r.AddPromptAsync(It.IsAny<UserFeedbackPrompt>(), It.IsAny<CancellationToken>()))
            .Callback<UserFeedbackPrompt, CancellationToken>((p, _) => _addedPrompts.Add(p))
            .Returns(Task.CompletedTask);
        _accounts.Setup(r => r.GetActiveEmailsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Oumayma, Eya });
        _email.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((to, _, _, _) => _mailedTo.Add(to))
            .ReturnsAsync(TransactionalEmailResult.Sent);
    }

    private static void SetCreatedAt(User user, DateTime createdAtUtc) =>
        typeof(User).GetProperty(nameof(User.CreatedAt))!.GetSetMethod(nonPublic: true)!
            .Invoke(user, new object[] { createdAtUtc });

    private void ActiveDays(int withWrites, int withoutWrites = 0)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var days = Enumerable.Range(1, withWrites)
            .Select(i => new ClinicActivityDay(ClinicId, today.AddDays(-i), 4, 1, 0, DateTime.UtcNow))
            .Concat(Enumerable.Range(1, withoutWrites)
                .Select(i => new ClinicActivityDay(ClinicId, today.AddDays(-20 - i), 0, 0, 0, DateTime.UtcNow)))
            .ToList();
        _activity.Setup(r => r.GetDaysAsync(ClinicId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(days);
    }

    private async Task<bool> PromptShows()
    {
        var handler = new GetFeedbackPromptQueryHandler(
            _context.Object, _users.Object, _feedback.Object, _activity.Object,
            NullLogger<GetFeedbackPromptQueryHandler>.Instance);
        var result = await handler.Handle(new GetFeedbackPromptQuery(), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!.Show;
    }

    private Task<Result<FeedbackReceiptDto>> Submit(SubmitFeedbackCommand command) =>
        new SubmitFeedbackCommandHandler(
                _context.Object, _users.Object, _clinics.Object, _feedback.Object, _accounts.Object,
                _email.Object, _unitOfWork.Object, NullLogger<SubmitFeedbackCommandHandler>.Instance)
            .Handle(command, CancellationToken.None);

    private Task<Result> SetPrompt(string? action) =>
        new SetFeedbackPromptCommandHandler(
                _context.Object, _users.Object, _feedback.Object, _unitOfWork.Object,
                NullLogger<SetFeedbackPromptCommandHandler>.Instance)
            .Handle(new SetFeedbackPromptCommand { Action = action }, CancellationToken.None);

    // --- When the week-one question is put -------------------------------------------------------------------

    [Fact]
    public async Task The_Question_Is_Put_After_A_Week_Of_Real_Use()
    {
        ActiveDays(withWrites: FeedbackRules.MinActiveDays);

        Assert.True(await PromptShows());
    }

    [Fact]
    public async Task Never_In_The_First_Week_And_The_Activity_Is_Not_Even_Read()
    {
        SetCreatedAt(_secretary, DateTime.UtcNow.AddDays(-(FeedbackRules.MinAccountDays - 1)));
        ActiveDays(withWrites: 10);

        Assert.False(await PromptShows());
        _activity.Verify(r => r.GetDaysAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Cabinet_That_Barely_Worked_Is_Not_Asked_And_A_Day_With_No_Write_Does_Not_Count()
    {
        ActiveDays(withWrites: FeedbackRules.MinActiveDays - 1, withoutWrites: 5);

        Assert.False(await PromptShows());
    }

    [Fact]
    public async Task An_Answered_Question_Is_Never_Put_Again()
    {
        ActiveDays(withWrites: 10);
        _prompt = new UserFeedbackPrompt(_secretary.Id);
        _prompt.Close(UserFeedbackPrompt.ClosedByAnswer);

        Assert.False(await PromptShows());
    }

    [Fact]
    public void Plus_Tard_Waits_A_Week_And_The_Question_Is_Shown_At_Most_Twice()
    {
        var now = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var prompt = new UserFeedbackPrompt("u1", now);
        Assert.True(prompt.MayShow(now));

        prompt.MarkShown(now);
        prompt.Later(now);
        Assert.False(prompt.MayShow(now.AddDays(6)));
        Assert.True(prompt.MayShow(now.AddDays(7)));

        prompt.MarkShown(now.AddDays(7));
        Assert.False(prompt.MayShow(now.AddDays(30)));
    }

    [Fact]
    public void Reporting_The_Same_Showing_From_Several_Pages_Counts_It_Once()
    {
        var now = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var prompt = new UserFeedbackPrompt("u1", now);

        prompt.MarkShown(now);
        prompt.MarkShown(now.AddMinutes(1));
        prompt.MarkShown(now.AddMinutes(2));

        Assert.Equal(1, prompt.TimesShown);
        Assert.True(prompt.MayShow(now.AddMinutes(3)));
    }

    [Fact]
    public void Plus_Tard_On_The_Last_Showing_Closes_It_So_Later_Never_Means_Again()
    {
        var now = new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);
        var prompt = new UserFeedbackPrompt("u1", now);
        prompt.MarkShown(now);
        prompt.Later(now);
        prompt.MarkShown(now.AddDays(7));

        prompt.Later(now.AddDays(7));

        Assert.True(prompt.IsClosed);
        Assert.Equal(UserFeedbackPrompt.ClosedByNever, prompt.ClosedReason);
    }

    [Fact]
    public async Task Ne_Plus_Demander_Closes_The_Question_And_Creates_The_Row_When_There_Was_None()
    {
        var result = await SetPrompt("never");

        Assert.True(result.IsSuccess, result.Error);
        var prompt = Assert.Single(_addedPrompts);
        Assert.True(prompt.IsClosed);
        Assert.Equal(UserFeedbackPrompt.ClosedByNever, prompt.ClosedReason);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_Unknown_Prompt_Action_Is_Refused_With_A_Code_And_Saves_Nothing()
    {
        var result = await SetPrompt("encore");

        Assert.True(result.IsFailure);
        Assert.Equal(FeedbackRefusals.InvalidCode, result.Code);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- What an answer must contain ------------------------------------------------------------------------

    [Fact]
    public async Task An_Empty_Message_Is_Refused_With_Its_Own_Code_And_Nobody_Is_Told()
    {
        var result = await Submit(new SubmitFeedbackCommand { Source = "aide", Message = "   " });

        Assert.True(result.IsFailure);
        Assert.Equal(FeedbackRefusals.EmptyCode, result.Code);
        Assert.Empty(_added);
        Assert.Empty(_mailedTo);
    }

    [Theory]
    [InlineData("idee", "aide", 6, null)]
    [InlineData("idee", "aide", null, "salle-attente")]
    [InlineData("reclamation", "aide", 3, null)]
    [InlineData("idee", "popup", 3, null)]
    public async Task A_Value_Outside_The_Closed_Sets_Is_Refused_As_Invalid(
        string kind, string source, int? help, string? area)
    {
        var result = await Submit(new SubmitFeedbackCommand
        {
            Kind = kind,
            Source = source,
            HelpRating = help,
            LikedAreas = area is null ? null : [area],
            Message = area is null && help is null ? "x" : null,
        });

        Assert.True(result.IsFailure);
        Assert.Equal(FeedbackRefusals.InvalidCode, result.Code);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task One_Star_Alone_Is_An_Answer_And_The_Areas_Are_Kept_Canonical()
    {
        var result = await Submit(new SubmitFeedbackCommand
        {
            Source = "semaine-1",
            EaseRating = 4,
            LikedAreas = ["Agenda", "agenda", "caisse"],
        });

        Assert.True(result.IsSuccess, result.Error);
        var saved = Assert.Single(_added);
        Assert.Equal(FeedbackRules.KindReview, saved.Kind);
        Assert.Equal(new[] { "agenda", "caisse" }, saved.LikedAreas());
        Assert.Equal(User.RoleSecretary, saved.AuthorRole);
    }

    [Fact]
    public async Task Answering_The_Week_One_Card_Closes_The_Question_For_Good()
    {
        _prompt = new UserFeedbackPrompt(_secretary.Id);
        _prompt.MarkShown();

        await Submit(new SubmitFeedbackCommand { Source = "semaine-1", HelpRating = 5 });

        Assert.True(_prompt.IsClosed);
        Assert.Equal(UserFeedbackPrompt.ClosedByAnswer, _prompt.ClosedReason);
    }

    [Fact]
    public async Task A_Message_From_Aide_Leaves_The_Week_One_Question_Alone()
    {
        await Submit(new SubmitFeedbackCommand { Kind = "question", Source = "aide", Message = "Comment imprimer ?" });

        Assert.Empty(_addedPrompts);
        _feedback.Verify(r => r.GetPromptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Who is told ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_Console_Account_Gets_One_Message_After_The_Save()
    {
        var order = new List<string>();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("save")).ReturnsAsync(1);
        _email.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((to, _, _, _) => order.Add(to))
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Submit(new SubmitFeedbackCommand { Kind = "idee", Source = "aide", Message = "Un export Excel" });

        Assert.Equal(new[] { "save", Oumayma, Eya }, order);
    }

    [Fact]
    public async Task A_Mail_Server_That_Throws_Never_Fails_The_Message()
    {
        _email.Setup(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Submit(new SubmitFeedbackCommand { Kind = "probleme", Source = "aide", Message = "Bug" });

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(_added);
    }

    [Fact]
    public void The_Address_Reaches_The_Notice_Only_When_The_Person_Agreed()
    {
        var agreed = Feedback(mayContact: true);
        var declined = Feedback(mayContact: false);

        var withAddress = EmailLayout.PlainText(ConsoleTeamFeedbackNotice.Compose(
            new FeedbackNoticeFacts("Cabinet Dr Geller", "Amel", User.RoleSecretary, "amel@cabinet.tn", agreed)));
        var withoutAddress = EmailLayout.PlainText(ConsoleTeamFeedbackNotice.Compose(
            new FeedbackNoticeFacts("Cabinet Dr Geller", "Amel", User.RoleSecretary, "amel@cabinet.tn", declined)));

        Assert.Contains("amel@cabinet.tn", withAddress);
        Assert.Contains("Secrétaire", withAddress);
        Assert.DoesNotContain("amel@cabinet.tn", withoutAddress);
    }

    [Fact]
    public void The_Subject_Is_Fixed_Text() =>
        Assert.DoesNotContain("Geller", ConsoleTeamFeedbackNotice.Subject);

    // --- What « Retours » may show ---------------------------------------------------------------------------

    [Fact]
    public async Task Retours_Refuses_To_Read_Without_A_Cross_Cabinet_Scope()
    {
        var handler = new GetPlatformFeedbackQueryHandler(
            _feedback.Object, new TenantScope(NullLogger<TenantScope>.Instance),
            NullLogger<GetPlatformFeedbackQueryHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(new GetPlatformFeedbackQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task Retours_Shows_French_Labels_And_The_Address_Only_When_The_Person_Agreed()
    {
        var rows = new List<ProductFeedbackRow>
        {
            new(Feedback(mayContact: true), "Cabinet Dr Geller", "Amel", "amel@cabinet.tn"),
            new(Feedback(mayContact: false), "Cabinet Dr Geller", null, "gone@cabinet.tn"),
        };
        _feedback.Setup(r => r.GetPageAsync(null, It.IsAny<PageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<ProductFeedbackRow>.Unpaged(rows));
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        scope.UseSystemWide("test");

        var result = await new GetPlatformFeedbackQueryHandler(
                _feedback.Object, scope, NullLogger<GetPlatformFeedbackQueryHandler>.Instance)
            .Handle(new GetPlatformFeedbackQuery { Kind = "nimporte" }, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        var (agreed, declined) = (result.Value!.Items[0], result.Value.Items[1]);
        Assert.Equal("amel@cabinet.tn", agreed.ContactEmail);
        Assert.Equal("Secrétaire", agreed.AuthorRoleLabel);
        Assert.Equal(new[] { "Agenda" }, agreed.LikedAreas);
        Assert.Null(declined.ContactEmail);
        Assert.Equal("Compte supprimé", declined.AuthorName);
    }

    private static ProductFeedback Feedback(bool mayContact) => new(
        ClinicId, "user-1", User.RoleSecretary, FeedbackRules.KindIdea, FeedbackRules.SourceHelp,
        helpRating: 4, easeRating: null, likedAreas: ["agenda"], hardAreas: [], message: "Un export Excel",
        fromScreen: "/appointments", mayContact: mayContact);
}
