using ClinicManagement.Application.Common.Email;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Application.Features.Auth.Queries;
using ClinicManagement.Application.Features.Users;
using ClinicManagement.Application.Features.Users.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Users;

/// <summary>
/// « Inviter » a colleague by e-mail (<c>guided-first-days</c> C): on the online version the account is created with an
/// e-mailed one-time link where the colleague chooses her own password; a cabinet's own PC keeps the temporary password.
/// The cases are the claims the feature makes — one link, seven days, one use, the old link dead after a resend, no
/// session, and a failed e-mail that never loses the account.
/// </summary>
public class StaffInvitationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Now = new(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>An in-memory invitation table, so the cases test what the rows end up holding.</summary>
    private sealed class FakeInvitations : IStaffInvitationRepository
    {
        public List<StaffInvitation> Rows { get; } = new();

        public Task<StaffInvitation?> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.UserId == userId));

        public Task<StaffInvitation?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.TokenHash == tokenHash));

        public Task<IReadOnlySet<string>> GetPendingUserIdsAsync(
            IReadOnlyCollection<string> userIds, DateTime nowUtc, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlySet<string>>(
                Rows.Where(r => userIds.Contains(r.UserId) && r.IsUsable(nowUtc)).Select(r => r.UserId).ToHashSet());

        public Task AddAsync(StaffInvitation invitation, CancellationToken cancellationToken = default)
        {
            Rows.Add(invitation);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(StaffInvitation invitation, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> PurgeSpentAsync(DateTime nowUtc, TimeSpan consumedRetention, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private readonly FakeInvitations _invitations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IStaffInvitationPolicy> _policy = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();
    private readonly Mock<IPublicAppUrlProvider> _appUrl = new();
    private readonly List<(string To, string Subject, EmailContent Content)> _sent = new();
    private readonly List<User> _added = new();
    private readonly User _admin;

    public StaffInvitationTests()
    {
        _admin = User.CreateLocalUser(ClinicId, User.RoleAdmin, "admin@cabinet.tn", "HASH", "Dr Monica Geller");
        _context.Setup(c => c.GetUserId()).Returns(_admin.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(_admin.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_admin);
        _users.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _users.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .Callback<User, CancellationToken>((u, _) => _added.Add(u))
            .Returns(Task.CompletedTask);
        _users.Setup(r => r.GetByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _added.FirstOrDefault(u => u.Id == id));
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Dr Geller", city: "Tunis"));
        _auth.Setup(a => a.GenerateTemporaryPassword()).Returns("Temp-Pass-42");
        _auth.Setup(a => a.HashPassword(It.IsAny<string>())).Returns<string>(p => $"hash({p})");
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _policy.SetupGet(p => p.InvitesStaffByLink).Returns(true);
        _appUrl.SetupGet(a => a.IsConfigured).Returns(true);
        _appUrl.SetupGet(a => a.BaseUrl).Returns("https://app.apexa.tn");
        _email.SetupGet(e => e.IsConfigured).Returns(true);
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, EmailContent, CancellationToken>((to, subject, content, _) => _sent.Add((to, subject, content)))
            .ReturnsAsync(TransactionalEmailResult.Sent);
    }

    private CreateClinicUserCommandHandler Create() => new(
        _users.Object, new Mock<IDoctorRepository>().Object, _context.Object, _auth.Object, _unitOfWork.Object,
        _policy.Object, _invitations, _clinics.Object, _email.Object, _appUrl.Object,
        NullLogger<CreateClinicUserCommandHandler>.Instance);

    private Task<Application.Common.Models.Result<Application.DTOs.CreatedClinicUserDto>> Invite(string email = "amel@cabinet.tn") =>
        Create().Handle(new CreateClinicUserCommand { Email = email, FullName = "Amel Ben Salah", Role = "secretary" }, CancellationToken.None);

    /// <summary>Moves the current link's issue time back, keeping its token.</summary>
    private void AgeTheLink(TimeSpan by)
    {
        var row = _invitations.Rows.Single();
        row.Rearm(row.TokenHash, row.InvitedByUserId, DateTime.UtcNow - by);
    }

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.IndexOf("#t=", StringComparison.Ordinal) + 3)..]);

    private AcceptStaffInvitationCommandHandler Accept() => new(
        _invitations, _users.Object, _auth.Object, _unitOfWork.Object, NullLogger<AcceptStaffInvitationCommandHandler>.Instance);

    private ResendStaffInvitationCommandHandler Resend() => new(
        _context.Object, _users.Object, _clinics.Object, _invitations, _email.Object, _appUrl.Object, _unitOfWork.Object,
        NullLogger<ResendStaffInvitationCommandHandler>.Instance);

    private PreviewStaffInvitationQueryHandler Preview() => new(
        _invitations, _users.Object, _clinics.Object, NullLogger<PreviewStaffInvitationQueryHandler>.Instance);

    // --- The record ------------------------------------------------------------------------------------------

    [Fact]
    public void An_Invitation_Lasts_Seven_Days_And_Serves_Once()
    {
        var invitation = StaffInvitation.Create("local|u", "local|admin", StaffInvitation.HashToken("t"), Now);

        Assert.Equal(Now.AddDays(7), invitation.ExpiresAtUtc);
        Assert.True(invitation.IsUsable(Now.AddDays(7).AddSeconds(-1)));
        Assert.False(invitation.IsUsable(Now.AddDays(7)));

        invitation.Consume(Now);
        Assert.False(invitation.IsUsable(Now));
    }

    [Fact]
    public void Rearming_Gives_A_New_Week_And_A_New_Hash()
    {
        var invitation = StaffInvitation.Create("local|u", "local|admin", StaffInvitation.HashToken("old"), Now);
        invitation.Consume(Now);

        invitation.Rearm(StaffInvitation.HashToken("new"), "local|admin", Now.AddDays(3));

        Assert.True(invitation.IsUsable(Now.AddDays(3)));
        Assert.Equal(Now.AddDays(10), invitation.ExpiresAtUtc);
        Assert.Equal(StaffInvitation.HashToken("new"), invitation.TokenHash);
    }

    // --- Creating the account ----------------------------------------------------------------------------------

    [Fact]
    public async Task Online_An_Account_Is_Invited_By_Email_And_Carries_No_Temporary_Password()
    {
        var result = await Invite();

        Assert.True(result.IsSuccess, result.Error);
        var dto = result.Value!;
        Assert.Null(dto.TemporaryPassword);
        Assert.True(dto.InvitationEmailed);
        Assert.StartsWith("https://app.apexa.tn/invitation#t=", dto.InvitationUrl);

        var user = Assert.Single(_added);
        Assert.False(user.MustChangePassword);
        var row = Assert.Single(_invitations.Rows);
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(StaffInvitation.HashToken(TokenOf(dto.InvitationUrl!)), row.TokenHash);

        var mail = Assert.Single(_sent);
        Assert.Equal("amel@cabinet.tn", mail.To);
        Assert.Equal("Dr Monica Geller vous invite sur APEXA", mail.Subject);
        Assert.Equal(dto.InvitationUrl, mail.Content.Action!.Url);
        Assert.Contains("Cabinet Dr Geller", EmailLayout.PlainText(mail.Content));
    }

    [Fact]
    public async Task On_A_Cabinets_Own_Pc_The_Temporary_Password_Is_Unchanged()
    {
        _policy.SetupGet(p => p.InvitesStaffByLink).Returns(false);

        var result = await Invite();

        Assert.Equal("Temp-Pass-42", result.Value!.TemporaryPassword);
        Assert.Null(result.Value.InvitationUrl);
        Assert.True(Assert.Single(_added).MustChangePassword);
        Assert.Empty(_invitations.Rows);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Without_A_Public_Address_The_Temporary_Password_Stays_Rather_Than_A_Link_To_Nowhere()
    {
        _appUrl.SetupGet(a => a.IsConfigured).Returns(false);

        var result = await Invite();

        Assert.Equal("Temp-Pass-42", result.Value!.TemporaryPassword);
        Assert.Empty(_invitations.Rows);
    }

    [Fact]
    public async Task A_Failed_Email_Never_Loses_The_Account_And_The_Link_Is_Still_Returned()
    {
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EmailContent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Invite();

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Value!.InvitationEmailed);
        Assert.NotNull(result.Value.InvitationUrl);
        Assert.Single(_added);
    }

    [Fact]
    public void The_Subject_Drops_Line_Breaks_From_The_Inviters_Name() =>
        Assert.Equal("Dr Geller Bcc: x vous invite sur APEXA", StaffInvitations.Subject("Dr Geller\r\nBcc: x"));

    // --- Accepting ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Accepting_Sets_The_Password_And_Spends_The_Link_Once()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);
        var user = _added.Single();

        var first = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);
        var second = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-autre-mot-de-passe-2026" }, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error);
        Assert.Equal("hash(Un-bon-mot-de-passe-2026)", user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.True(second.IsFailure);
        Assert.Equal(StaffInvitations.InvalidCode, second.Code);
        Assert.Equal("hash(Un-bon-mot-de-passe-2026)", user.PasswordHash);
    }

    [Theory]
    [InlineData("not-the-token")]
    [InlineData("")]
    public async Task A_Wrong_Token_Meets_The_One_Refusal(string token)
    {
        await Invite();

        var result = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);

        Assert.Equal(StaffInvitations.InvalidCode, result.Code);
    }

    [Fact]
    public async Task A_Short_Password_Is_Refused_Before_The_Link_Is_Spent()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);

        var result = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "court" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.True(_invitations.Rows.Single().IsUsable(DateTime.UtcNow));
    }

    [Fact]
    public async Task An_Expired_Link_Is_Refused()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);
        AgeTheLink(StaffInvitation.Lifetime + TimeSpan.FromMinutes(1));

        var accepted = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);
        var preview = await Preview().Handle(new PreviewStaffInvitationQuery { Token = token }, CancellationToken.None);

        Assert.Equal(StaffInvitations.InvalidCode, accepted.Code);
        Assert.Equal(StaffInvitations.InvalidCode, preview.Code);
        Assert.NotEqual("hash(Un-bon-mot-de-passe-2026)", _added.Single().PasswordHash);
    }

    [Fact]
    public async Task A_Deactivated_Account_Cannot_Take_Its_Link()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);
        _added.Single().Deactivate();

        var accepted = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);

        Assert.Equal(StaffInvitations.InvalidCode, accepted.Code);
    }

    [Fact]
    public async Task An_Account_That_Already_Signed_In_Cannot_Take_Its_Link()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);
        _added.Single().RecordSuccessfulLogin();

        var accepted = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);

        Assert.Equal(StaffInvitations.InvalidCode, accepted.Code);
        Assert.NotEqual("hash(Un-bon-mot-de-passe-2026)", _added.Single().PasswordHash);
    }

    // --- Resending ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Resending_Kills_The_Previous_Link()
    {
        var oldToken = TokenOf((await Invite()).Value!.InvitationUrl!);
        var user = _added.Single();
        AgeTheLink(StaffInvitation.ResendCooldown);

        var resent = await Resend().Handle(new ResendStaffInvitationCommand { UserId = user.Id }, CancellationToken.None);
        var withOld = await Accept().Handle(new AcceptStaffInvitationCommand { Token = oldToken, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);
        var withNew = await Accept().Handle(
            new AcceptStaffInvitationCommand { Token = TokenOf(resent.Value!.InvitationUrl), Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);

        Assert.True(resent.IsSuccess, resent.Error);
        Assert.Equal(StaffInvitations.InvalidCode, withOld.Code);
        Assert.True(withNew.IsSuccess, withNew.Error);
        Assert.Single(_invitations.Rows);
    }

    [Fact]
    public async Task Resending_Within_Two_Minutes_Is_Refused_And_The_Link_Survives()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);
        var user = _added.Single();
        _sent.Clear();

        var result = await Resend().Handle(new ResendStaffInvitationCommand { UserId = user.Id }, CancellationToken.None);
        var accepted = await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);

        Assert.Equal(ResendStaffInvitationCommandHandler.TooSoonCode, result.Code);
        Assert.Empty(_sent);
        Assert.True(accepted.IsSuccess, accepted.Error);
    }

    [Fact]
    public async Task Resending_Is_Refused_Once_The_Colleague_Has_Signed_In()
    {
        await Invite();
        var user = _added.Single();
        user.RecordSuccessfulLogin();

        var result = await Resend().Handle(new ResendStaffInvitationCommand { UserId = user.Id }, CancellationToken.None);

        Assert.Equal(ResendStaffInvitationCommandHandler.AlreadySignedInCode, result.Code);
    }

    [Fact]
    public async Task Another_Cabinets_Account_Reads_As_Not_Found()
    {
        var stranger = User.CreateLocalUser(OtherClinicId, User.RoleSecretary, "x@ailleurs.tn", "HASH", "X");
        _added.Add(stranger);

        var result = await Resend().Handle(new ResendStaffInvitationCommand { UserId = stranger.Id }, CancellationToken.None);

        Assert.Equal("Utilisateur introuvable.", result.Error);
        Assert.Empty(_invitations.Rows);
    }

    // --- The page's greeting -----------------------------------------------------------------------------------

    [Fact]
    public async Task The_Preview_Names_The_Person_And_The_Cabinet_And_Never_A_Spent_Link()
    {
        var token = TokenOf((await Invite()).Value!.InvitationUrl!);

        var before = await Preview().Handle(new PreviewStaffInvitationQuery { Token = token }, CancellationToken.None);
        await Accept().Handle(new AcceptStaffInvitationCommand { Token = token, Password = "Un-bon-mot-de-passe-2026" }, CancellationToken.None);
        var after = await Preview().Handle(new PreviewStaffInvitationQuery { Token = token }, CancellationToken.None);

        Assert.Equal(new StaffInvitationPreviewDto("Amel Ben Salah", "amel@cabinet.tn", "Cabinet Dr Geller"), before.Value);
        Assert.Equal(StaffInvitations.InvalidCode, after.Code);
    }
}
