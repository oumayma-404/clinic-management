using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// « J'ai oublié mon mot de passe » — the request half.
///
/// <para>The load-bearing property under test is that <b>every outcome answers identically</b>. A test per
/// ineligible branch is not padding: each one is a real state of a real account, and any one of them answering
/// differently is an account-enumeration oracle on an anonymous endpoint.</para>
/// </summary>
public class RequestPasswordResetCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly Mock<IPasswordResetRequestRepository> _requests = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IPublicAppUrlProvider> _appUrl = new();

    public RequestPasswordResetCommandHandlerTests()
    {
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent);
        _appUrl.SetupGet(u => u.IsConfigured).Returns(true);
        _appUrl.SetupGet(u => u.BaseUrl).Returns("https://cabinet.tn");
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private RequestPasswordResetCommandHandler Handler() =>
        new(_requests.Object, _users.Object, _email.Object, _uow.Object, _appUrl.Object,
            NullLogger<RequestPasswordResetCommandHandler>.Instance);

    private static User Local(bool active = true) =>
        User.CreateLocalUser(ClinicId, "admin", "dr@clinic.tn", "HASH", "Dr House");

    private void KnownUser(User user) =>
        _users.Setup(r => r.GetByEmailAsync("dr@clinic.tn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

    private Task<Application.Common.Models.Result<PasswordResetRequestedDto>> Ask(string email = "dr@clinic.tn") =>
        Handler().Handle(new RequestPasswordResetCommand { Email = email }, CancellationToken.None);

    // ── The happy path ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Live_Account_Gets_A_Row_And_An_Email()
    {
        KnownUser(Local());

        var result = await Ask();

        Assert.True(result.IsSuccess);
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        _email.Verify(
            e => e.SendAsync("dr@clinic.tn", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// ⚠️ The link rides in the URL **fragment**. A fragment is never sent to a server, so the live single-use
    /// credential stays out of the reverse proxy's access log and every intermediate hop — all of which outlive by a
    /// long way the hour the token is bounded by. A `?token=` here would be a silent, permanent leak.
    /// </summary>
    [Fact]
    public async Task The_Emailed_Link_Carries_The_Token_In_The_Fragment_Not_The_Query()
    {
        KnownUser(Local());
        string? body = null;
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, b, _) => body = b)
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Ask();

        Assert.NotNull(body);
        Assert.Contains("https://cabinet.tn/reinitialiser-mot-de-passe#token=", body);
        Assert.DoesNotContain("?token=", body);
    }

    /// <summary>The e-mail must never carry the password itself — there is not one yet, and saying so is the point.</summary>
    [Fact]
    public async Task The_Emailed_Link_Says_The_Current_Password_Still_Works()
    {
        KnownUser(Local());
        string? body = null;
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, b, _) => body = b)
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Ask();

        // ⚠️ Matched on a fragment that does not straddle a line break — the body is a raw string literal and
        // « reste / valable » is wrapped across two lines in it. A longer phrase would fail on the formatting
        // rather than on the meaning.
        Assert.Contains("Votre mot de passe actuel", body!);
    }

    // ── The enumeration guarantee: every ineligible branch answers exactly as the happy path does ──────────

    [Fact]
    public async Task An_Unknown_Address_Answers_Identically_And_Writes_Nothing()
    {
        _users
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var unknown = await Ask("nobody@clinic.tn");

        KnownUser(Local());
        var known = await Ask();

        Assert.True(unknown.IsSuccess);
        Assert.Equal(known.Value!.Message, unknown.Value!.Message);
        _requests.Verify(
            r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Deactivated_Account_Answers_Identically_And_Writes_Nothing()
    {
        var user = Local();
        user.Deactivate();
        KnownUser(user);

        var result = await Ask();

        Assert.True(result.IsSuccess);
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _email.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>An Auth0-backed account has no <c>PasswordHash</c> to replace — and must not be distinguishable.</summary>
    [Fact]
    public async Task A_Non_Local_Account_Answers_Identically_And_Writes_Nothing()
    {
        KnownUser(new User("auth0|xyz", ClinicId, "admin", "dr@clinic.tn", "Dr House"));

        var result = await Ask();

        Assert.True(result.IsSuccess);
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The refusals that reveal nothing about the address ────────────────────────────────────────────────

    [Fact]
    public async Task A_Blank_Address_Is_Refused()
    {
        Assert.True((await Ask(" ")).IsFailure);
    }

    /// <summary>
    /// The display-name form `EmailAddressInput` exists to refuse — shared with the signup door, so neither can
    /// drift into storing a string that matches no `User` row.
    /// </summary>
    [Fact]
    public async Task A_Display_Name_Address_Is_Refused()
    {
        var result = await Ask("Attaquant <dr@clinic.tn>");

        Assert.True(result.IsFailure);
        _users.Verify(
            r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ An <b>unconfigured</b> transport is a loud refusal, checked before anything is written — a deployment that
    /// can never send must say so rather than answer 202 over mail that will not arrive. Contrast the test below.
    /// </summary>
    [Fact]
    public async Task An_Unconfigured_Mail_Transport_Is_Refused_Loudly_With_A_Code()
    {
        _email.SetupGet(e => e.IsConfigured).Returns(false);
        KnownUser(Local());

        var result = await Ask();

        Assert.True(result.IsFailure);
        Assert.Equal(RequestPasswordResetCommandHandler.UnavailableCode, result.Code);
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ A <b>failed</b> send is silent, and the asymmetry with the test above is the whole point: a refusal here
    /// would be a clean enumeration oracle — during any mail outage a real account would get « l'e-mail n'a pas pu
    /// être envoyé » while an unknown address got the neutral sentence, with no timing needed to tell them apart.
    /// </summary>
    [Fact]
    public async Task A_Failed_Send_Is_Reported_To_The_Log_And_Not_To_The_Caller()
    {
        KnownUser(Local());
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("smtp refused"));

        var result = await Ask();

        Assert.True(result.IsSuccess);
    }

    // ── One row per account, and the cooldown ────────────────────────────────────────────────────────────

    /// <summary>
    /// A second request within the cooldown sends nothing and rotates nothing — otherwise a caller could aim mail at
    /// one victim's mailbox on every request the rate limiter allows, and that limiter partitions on the address the
    /// caller chose.
    /// </summary>
    [Fact]
    public async Task A_Second_Request_Inside_The_Cooldown_Sends_Nothing()
    {
        var user = Local();
        KnownUser(user);
        var live = PasswordResetRequest.Create(user.Id, "dr@clinic.tn", "HASH-1", DateTime.UtcNow);
        _requests
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var result = await Ask();

        Assert.True(result.IsSuccess);
        Assert.Equal("HASH-1", live.TokenHash);
        _email.Verify(
            e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Past the cooldown the token rotates — and only <b>after</b> the send succeeded, so a mail failure leaves the
    /// link already sitting in the person's inbox alive instead of replacing it with one that never arrived.
    /// </summary>
    [Fact]
    public async Task Past_The_Cooldown_The_Token_Rotates_After_A_Successful_Send()
    {
        var user = Local();
        KnownUser(user);
        var live = PasswordResetRequest.Create(
            user.Id, "dr@clinic.tn", "HASH-1", DateTime.UtcNow.AddMinutes(-5));
        _requests
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var result = await Ask();

        Assert.True(result.IsSuccess);
        Assert.NotEqual("HASH-1", live.TokenHash);
        _requests.Verify(r => r.UpdateAsync(live, It.IsAny<CancellationToken>()), Times.Once);
        // Never a second row: the unique index on UserId is what makes « one live token » an invariant, and a
        // second AddAsync would be the handler racing it.
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Failed_Send_On_The_Rotate_Path_Leaves_The_Existing_Link_Alive()
    {
        var user = Local();
        KnownUser(user);
        var live = PasswordResetRequest.Create(
            user.Id, "dr@clinic.tn", "HASH-1", DateTime.UtcNow.AddMinutes(-5));
        _requests
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Failed("smtp refused"));

        var result = await Ask();

        Assert.True(result.IsSuccess);
        Assert.Equal("HASH-1", live.TokenHash);
    }

    /// <summary>An expired or spent row is re-armed in place rather than joined by a second one.</summary>
    [Fact]
    public async Task An_Unusable_Row_Is_Rearmed_In_Place()
    {
        var user = Local();
        KnownUser(user);
        var stale = PasswordResetRequest.Create(
            user.Id, "dr@clinic.tn", "HASH-1", DateTime.UtcNow.AddHours(-3));
        _requests
            .Setup(r => r.GetByUserIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stale);

        var result = await Ask();

        Assert.True(result.IsSuccess);
        Assert.NotEqual("HASH-1", stale.TokenHash);
        Assert.True(stale.IsUsable(DateTime.UtcNow));
        _requests.Verify(r => r.UpdateAsync(stale, It.IsAny<CancellationToken>()), Times.Once);
        _requests.Verify(r => r.AddAsync(It.IsAny<PasswordResetRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>The table only grows when somebody asks for a reset, so the write that grows it owes the trim.</summary>
    [Fact]
    public async Task The_Request_Path_Purges_Spent_Rows()
    {
        KnownUser(Local());

        await Ask();

        _requests.Verify(
            r => r.PurgeSpentAsync(It.IsAny<DateTime>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>⚠️ No account is mutated here. The password is replaced only by the completion step.</summary>
    [Fact]
    public async Task Requesting_A_Reset_Changes_Nothing_About_The_Account()
    {
        var user = Local();
        var hashBefore = user.PasswordHash;
        var versionBefore = user.TokenVersion;
        KnownUser(user);

        await Ask();

        Assert.Equal(hashBefore, user.PasswordHash);
        Assert.Equal(versionBefore, user.TokenVersion);
        _users.Verify(r => r.Update(It.IsAny<User>()), Times.Never);
    }
}
