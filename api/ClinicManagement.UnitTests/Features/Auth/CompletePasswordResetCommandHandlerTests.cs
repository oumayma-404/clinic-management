using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// « J'ai oublié mon mot de passe » — the half that actually changes an account.
/// </summary>
public class CompletePasswordResetCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string RawToken = "a-raw-single-use-token";

    private readonly Mock<IPasswordResetRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public CompletePasswordResetCommandHandlerTests()
    {
        _auth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns("NEW-HASH");
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private CompletePasswordResetCommandHandler Handler() =>
        new(_requests.Object, _users.Object, _auth.Object, _email.Object, _uow.Object,
            NullLogger<CompletePasswordResetCommandHandler>.Instance);

    /// <summary>A password comfortably over the served floor, so no test here restates the number.</summary>
    private static string GoodPassword => new('x', PasswordPolicy.MinLength + 4);

    private (User user, PasswordResetRequest row) Staged(DateTime? issuedAt = null)
    {
        var user = User.CreateLocalUser(ClinicId, "admin", "dr@clinic.tn", "OLD-HASH", "Dr House");
        var row = PasswordResetRequest.Create(
            user.Id, "dr@clinic.tn", PasswordResetRequest.HashToken(RawToken), issuedAt ?? DateTime.UtcNow);

        _requests
            .Setup(r => r.GetByTokenHashAsync(
                PasswordResetRequest.HashToken(RawToken), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _users.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        return (user, row);
    }

    private Task<Application.Common.Models.Result> Complete(string? token = null, string? password = null) =>
        Handler().Handle(
            new CompletePasswordResetCommand
            {
                Token = token ?? RawToken,
                NewPassword = password ?? GoodPassword,
            },
            CancellationToken.None);

    // ── The happy path, and the three things `SetPassword` carries with it ────────────────────────────────

    [Fact]
    public async Task A_Valid_Link_Sets_The_Password_And_Spends_The_Token()
    {
        var (user, row) = Staged();

        var result = await Complete();

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-HASH", user.PasswordHash);
        Assert.NotNull(row.ConsumedAtUtc);
        _users.Verify(r => r.Update(user), Times.Once);
    }

    /// <summary>
    /// ⚠️ Every session opened with the forgotten password dies — which is exactly right if the reason it was
    /// forgotten is that somebody else changed it.
    /// </summary>
    [Fact]
    public async Task Completing_A_Reset_Ends_Every_Existing_Session()
    {
        var (user, _) = Staged();
        var before = user.TokenVersion;

        await Complete();

        Assert.True(user.TokenVersion > before);
    }

    /// <summary>
    /// ⚠️ A person who locked themselves out guessing must not then wait fifteen minutes to use the password they
    /// just chose. `SetPassword` clears the lockout; this asserts nobody replaces it with a bare hash assignment.
    /// </summary>
    [Fact]
    public async Task Completing_A_Reset_Clears_An_Existing_Lockout()
    {
        var (user, _) = Staged();
        for (var i = 0; i < User.MaxFailedLoginAttempts; i++)
        {
            user.RecordFailedLogin();
        }

        Assert.True(user.IsLockedOut());

        await Complete();

        Assert.False(user.IsLockedOut());
    }

    /// <summary>
    /// The owner chose this password themselves, unlike an administrator's reset or the console verb — so forcing a
    /// second choice at the next screen would be a ritual with no security in it.
    /// </summary>
    [Fact]
    public async Task The_Chosen_Password_Does_Not_Force_A_Further_Change()
    {
        var (user, _) = Staged();

        await Complete();

        Assert.False(user.MustChangePassword);
    }

    /// <summary>
    /// ⚠️ <b>The load-bearing decision of the whole feature.</b> Controlling the mailbox is enough to replace a
    /// password precisely BECAUSE the second factor still gates the sign-in that follows. If this ever starts
    /// clearing the factor — or opening a replacement window as `RedeemRecoveryCodeCommand` does after proving two
    /// things — read access to one inbox becomes full account takeover.
    /// </summary>
    [Fact]
    public async Task Completing_A_Reset_Leaves_The_Second_Factor_Untouched()
    {
        var (user, _) = Staged();
        user.IssueTotpSecret("PROTECTED-SECRET");
        user.CompleteTotpEnrolment(["code-1", "code-2"]);
        Assert.True(user.IsTotpEnrolled);

        await Complete();

        Assert.True(user.IsTotpEnrolled);
        Assert.False(user.IsTotpReplacementGranted());
    }

    /// <summary>The confirmation is the only signal to an owner who did not ask for this.</summary>
    [Fact]
    public async Task The_Account_Holder_Is_Told_Their_Password_Changed()
    {
        Staged();

        await Complete();

        _email.Verify(
            e => e.SendAsync("dr@clinic.tn", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>Post-commit and best-effort: the password HAS changed, and a mail failure must not undo it.</summary>
    [Fact]
    public async Task A_Failed_Confirmation_Email_Does_Not_Fail_The_Reset()
    {
        var (user, _) = Staged();
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Complete();

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-HASH", user.PasswordHash);
    }

    // ── Every unusable link, and they must be indistinguishable to the caller ────────────────────────────

    [Fact]
    public async Task An_Unknown_Token_Is_Refused_With_The_Gone_Code()
    {
        _requests
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetRequest?)null);

        var result = await Complete();

        Assert.True(result.IsFailure);
        Assert.Equal(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
    }

    [Fact]
    public async Task An_Expired_Token_Is_Refused_And_Changes_Nothing()
    {
        var (user, _) = Staged(issuedAt: DateTime.UtcNow.AddHours(-2));

        var result = await Complete();

        Assert.True(result.IsFailure);
        Assert.Equal(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
        Assert.Equal("OLD-HASH", user.PasswordHash);
    }

    [Fact]
    public async Task An_Already_Spent_Token_Is_Refused_And_Changes_Nothing()
    {
        var (user, row) = Staged();
        row.Consume(DateTime.UtcNow.AddMinutes(-1));

        var result = await Complete();

        Assert.True(result.IsFailure);
        Assert.Equal(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
        Assert.Equal("OLD-HASH", user.PasswordHash);
    }

    /// <summary>
    /// The same French sentence for « inconnu », « expiré » and « déjà utilisé ». Distinguishing them would tell a
    /// holder of stolen tokens which ones were once real.
    /// </summary>
    [Fact]
    public async Task Every_Unusable_Link_Gets_The_Same_Sentence()
    {
        _requests
            .Setup(r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PasswordResetRequest?)null);
        var unknown = await Complete();

        _requests.Reset();
        var (_, spent) = Staged();
        spent.Consume(DateTime.UtcNow);
        var alreadyUsed = await Complete();

        Assert.Equal(unknown.Error, alreadyUsed.Error);
        Assert.Equal(unknown.Code, alreadyUsed.Code);
    }

    /// <summary>
    /// An account deactivated in the hour since the link was mailed. The row is spent on the way out, so a link that
    /// has become useless cannot be retried indefinitely.
    /// </summary>
    [Fact]
    public async Task A_Link_Naming_A_Deactivated_Account_Is_Refused_And_Spent()
    {
        var (user, row) = Staged();
        user.Deactivate();

        var result = await Complete();

        Assert.True(result.IsFailure);
        Assert.Equal(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
        Assert.NotNull(row.ConsumedAtUtc);
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task A_Blank_Token_Is_Refused()
    {
        var result = await Complete(token: " ");

        Assert.True(result.IsFailure);
        Assert.Equal(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
    }

    // ── The password floor ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The length is checked <b>before</b> the token is looked up, and this asserts that ordering: checking after
    /// would spend a perfectly good token on a password the server then refuses, leaving the person with a dead link
    /// and a sentence telling them to request another.
    /// </summary>
    [Fact]
    public async Task A_Too_Short_Password_Is_Refused_Without_Spending_The_Token()
    {
        var (user, row) = Staged();

        var result = await Complete(password: new string('x', PasswordPolicy.MinLength - 1));

        Assert.True(result.IsFailure);
        Assert.NotEqual(CompletePasswordResetCommandHandler.InvalidTokenCode, result.Code);
        Assert.Null(row.ConsumedAtUtc);
        Assert.Equal("OLD-HASH", user.PasswordHash);
        _requests.Verify(
            r => r.GetByTokenHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Password_At_Exactly_The_Floor_Is_Accepted()
    {
        var (user, _) = Staged();

        var result = await Complete(password: new string('x', PasswordPolicy.MinLength));

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-HASH", user.PasswordHash);
    }
}
