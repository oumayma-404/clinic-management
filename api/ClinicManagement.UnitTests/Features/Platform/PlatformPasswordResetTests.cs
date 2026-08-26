using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The vendor replaces one clinic account's forgotten password from the console — the sibling of
/// <see cref="PlatformSecondFactorResetTests"/>, for the credential beside the factor.
///
/// <para><b>What these pin is the same pair of opposites.</b> The vendor must be able to do this, because the three
/// ordinary ways back all fail together: a sole administrator has no colleague to reset it, an unreachable mailbox
/// defeats the self-service link, and <c>reset-admin-password</c> needs a shell. And the operation must leave a
/// record naming <i>whose</i> password was replaced and <i>why</i> — because that record is the only thing standing
/// between this endpoint and a social-engineered telephone call, and because <c>User.SetPassword</c> writes no trace
/// anywhere else in the product.</para>
///
/// <para>⚠️ <b>The load-bearing case is <see cref="An_Account_At_Another_Cabinet_Is_Refused"/></b>, and it bites
/// harder here than on the factor: the address resolves across the whole deployment, and an implementation that
/// trusted it would hand a working credential to whoever the mis-keyed character happened to match — while every
/// other assertion in this file still passed. Its mirror is
/// <see cref="A_Refused_Reset_Writes_No_Journal_Row_And_Changes_No_Password"/>.</para>
///
/// <para>⚠️ <b>The second is <see cref="The_Second_Factor_Is_Left_Untouched"/>.</b> Clearing the factor here would
/// be one line and would look like helpfulness — and it would collapse two independent proofs into a single
/// telephone call, which is the whole reason the two resets are separate endpoints with separate journal rows.</para>
/// </summary>
public class PlatformPasswordResetTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Ben Ali";
    private const string TargetEmail = "dr.bensalah@cabinet.tn";
    private const string Motif = "Appel du Dr Ben Salah, mot de passe oublié, e-mail du cabinet inaccessible";
    private const string TempPassword = "Temp-9f3KzQ2wLm";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<INotificationGenerator> _notifications = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();

    public PlatformPasswordResetTests()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(SubscriptionVendorHarness.ClinicId, ClinicName, city: "Tunis"));

        _harness.Clinics
            .Setup(c => c.ExistsAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _auth.Setup(a => a.GenerateTemporaryPassword()).Returns(TempPassword);
        _auth.Setup(a => a.HashPassword(TempPassword)).Returns("TEMP-HASH");

        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(TransactionalEmailResult.Sent);
    }

    // ------------------------------------------------------------------ harness

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private ResetClinicUserPasswordFromConsoleCommandHandler Handler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _ledger,
            session ?? new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _auth.Object, _notifications.Object, _email.Object, _harness.UnitOfWork.Object,
            scope ?? SystemWideScope(),
            NullLogger<ResetClinicUserPasswordFromConsoleCommandHandler>.Instance);

    private static ResetClinicUserPasswordFromConsoleCommand Reset(
        string email = TargetEmail, string reason = Motif) =>
        new() { ClinicId = SubscriptionVendorHarness.ClinicId, Email = email, Reason = reason };

    private User GivenALocalAccount(Guid? clinicId = null, string role = User.RoleAdmin)
    {
        var user = User.CreateLocalUser(
            clinicId ?? SubscriptionVendorHarness.ClinicId, role, TargetEmail, "STORED-HASH", "Salma Ben Salah");

        _harness.Users
            .Setup(r => r.GetByEmailAsync(TargetEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        return user;
    }

    // ------------------------------------------------------------------ the way back it exists to be

    [Fact]
    public async Task Resetting_Replaces_The_Password_Forces_A_Change_And_Ends_The_Sessions()
    {
        var user = GivenALocalAccount();
        var versionBefore = user.TokenVersion;

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("TEMP-HASH", user.PasswordHash);
        // A handover token, not a password: the vendor has seen it and read it down a telephone.
        Assert.True(user.MustChangePassword);
        // Bumped, so a session opened under the forgotten password does not outlive it.
        Assert.True(user.TokenVersion > versionBefore);
    }

    /// <summary>
    /// The credential reaches the vendor's screen and nowhere else — it is read out by voice, which is why the
    /// response carries it and the notification e-mail does not.
    /// </summary>
    [Fact]
    public async Task The_Outcome_Carries_The_One_Time_Password_And_Names_Who_Was_Reset()
    {
        GivenALocalAccount();

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TempPassword, result.Value!.OneTimePassword);
        Assert.Equal(TargetEmail, result.Value.TargetEmail);
        Assert.Equal("Salma Ben Salah", result.Value.TargetName);
        Assert.Equal(User.RoleAdmin, result.Value.TargetRole);
    }

    /// <summary>
    /// ⚠️ Clearing the factor here would be one line and would look like helpfulness. It would also mean a single
    /// telephone call defeating both proofs — so the split is asserted rather than merely intended.
    /// </summary>
    [Fact]
    public async Task The_Second_Factor_Is_Left_Untouched()
    {
        var user = GivenALocalAccount();
        user.IssueTotpSecret("protected:JBSWY3DPEHPK3PXP");
        user.CompleteTotpEnrolment(
            Enumerable.Range(0, UserRecoveryCode.CountPerEnrolment)
                .Select(_ => UserRecoveryCode.NewCode())
                .ToList());

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(user.IsTotpEnrolled);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, user.UnusedRecoveryCodeCount);
    }

    // ------------------------------------------------------------------ the record, which is the only one there is

    [Fact]
    public async Task The_Journal_Row_Names_The_Cabinet_The_Person_And_The_Motif()
    {
        GivenALocalAccount();

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(_ledger.Rows);
        Assert.Equal(PlatformAccessAction.PasswordReset, row.Action);
        Assert.Equal(SubscriptionVendorHarness.ClinicId, row.ClinicId);
        Assert.Equal(ClinicName, row.ClinicName);
        Assert.Equal(TargetEmail, row.TargetEmail);
        Assert.Equal(Motif, row.Reason);
        Assert.Equal(AccountId, row.PlatformAccountId);
    }

    [Fact]
    public async Task A_Missing_Motif_Is_Refused_And_Nothing_Is_Written()
    {
        var user = GivenALocalAccount();

        var result = await Handler().Handle(Reset(reason: "  "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("STORED-HASH", user.PasswordHash);
        Assert.Empty(_ledger.Rows);
    }

    [Fact]
    public async Task A_Missing_Address_Is_Refused_And_Nothing_Is_Written()
    {
        var result = await Handler().Handle(Reset(email: " "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_ledger.Rows);
    }

    /// <summary>
    /// A refusal that half-applied would leave a cabinet's account re-credentialled with no row explaining it —
    /// worse than either outcome on its own.
    /// </summary>
    [Fact]
    public async Task A_Refused_Reset_Writes_No_Journal_Row_And_Changes_No_Password()
    {
        var user = GivenALocalAccount();
        _harness.Users
            .Setup(r => r.GetByEmailAsync("inconnu@cabinet.tn", It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await Handler().Handle(Reset(email: "inconnu@cabinet.tn"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ResetClinicUserPasswordFromConsoleCommandHandler.UnknownAccountCode, result.Code);
        Assert.Equal("STORED-HASH", user.PasswordHash);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ the bounds on a mis-keyed address

    /// <summary>
    /// ⚠️ <b>The load-bearing case.</b> The address resolves across the deployment; only the cabinet in the URL
    /// bounds a typo. Without that comparison a mis-keyed character hands a working credential to a stranger at a
    /// practice the vendor never opened — and every other assertion here still passes.
    /// </summary>
    [Fact]
    public async Task An_Account_At_Another_Cabinet_Is_Refused()
    {
        var stranger = GivenALocalAccount(clinicId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ResetClinicUserPasswordFromConsoleCommandHandler.UnknownAccountCode, result.Code);
        Assert.Equal("STORED-HASH", stranger.PasswordHash);
        Assert.Empty(_ledger.Rows);
    }

    /// <summary>
    /// The same sentence whether the address is unknown to the deployment or belongs to another cabinet.
    /// Distinguishing them would make this endpoint a way of asking « does this person work at that practice? »
    /// about any address the vendor cares to type.
    /// </summary>
    [Fact]
    public async Task An_Unknown_Address_And_A_Foreign_One_Are_Indistinguishable()
    {
        GivenALocalAccount(clinicId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));
        var foreign = await Handler().Handle(Reset(), CancellationToken.None);

        _harness.Users
            .Setup(r => r.GetByEmailAsync(TargetEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        var unknown = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.Equal(foreign.Error, unknown.Error);
        Assert.Equal(foreign.Code, unknown.Code);
    }

    /// <summary>
    /// An Auth0-backed account has no <c>PasswordHash</c> to replace. Answering « c'est fait » would tell the vendor
    /// the caller can now sign in while whatever is really blocking them is untouched.
    /// </summary>
    [Fact]
    public async Task An_Account_With_No_Local_Password_Is_Refused_As_A_State_Of_The_World()
    {
        _harness.Users
            .Setup(r => r.GetByEmailAsync(TargetEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new User(
                "auth0|xyz", SubscriptionVendorHarness.ClinicId, User.RoleAdmin, TargetEmail, "Salma Ben Salah"));

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ResetClinicUserPasswordFromConsoleCommandHandler.NotLocalAccountCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ telling the person

    /// <summary>
    /// The notification is what makes a social-engineered reset visible: the person who did not ask for it is the
    /// only one placed to notice. It says the <b>vendor</b> did it, never « votre administrateur » — which would send
    /// them to warn somebody with no record of the action and no power over it.
    /// </summary>
    [Fact]
    public async Task The_Affected_Person_Is_Told_And_Told_It_Was_The_Vendor()
    {
        var user = GivenALocalAccount();

        await Handler().Handle(Reset(), CancellationToken.None);

        _notifications.Verify(
            n => n.PasswordResetAsync(
                SubscriptionVendorHarness.ClinicId, user.Id, PasswordResetBy.Vendor, It.IsAny<CancellationToken>()),
            Times.Once);
        _email.Verify(
            e => e.SendAsync(TargetEmail, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// ⚠️ <b>The temporary password must never be mailed.</b> The mailbox is either unreachable — the reason this
    /// path exists — or in somebody else's hands, the reason the notice exists; mailing the credential would make
    /// that notice the delivery mechanism for the takeover it is meant to reveal.
    /// </summary>
    [Fact]
    public async Task The_Notification_Email_Never_Carries_The_Temporary_Password()
    {
        GivenALocalAccount();
        var bodies = new List<string>();
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, b, _) => bodies.Add(b))
            .ReturnsAsync(TransactionalEmailResult.Sent);

        await Handler().Handle(Reset(), CancellationToken.None);

        Assert.NotEmpty(bodies);
        Assert.All(bodies, body => Assert.DoesNotContain(TempPassword, body));
    }

    /// <summary>Post-commit and best-effort: the reset HAS happened, and telling them must not undo it.</summary>
    [Fact]
    public async Task A_Failed_Notification_Does_Not_Fail_The_Reset()
    {
        var user = GivenALocalAccount();
        _notifications
            .Setup(n => n.PasswordResetAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<PasswordResetBy>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("feed down"));
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("TEMP-HASH", user.PasswordHash);
        Assert.Single(_ledger.Rows);
    }

    // ------------------------------------------------------------------ EC-12

    /// <summary>
    /// An undeclared cross-cabinet scope reads zero rows with no error, which here would report every account in the
    /// deployment as unknown — « je n'ai pas pu lire » wearing the face of « ce compte n'existe pas ».
    /// </summary>
    [Fact]
    public async Task An_Undeclared_Tenant_Scope_Throws_Rather_Than_Reading_Nothing()
    {
        GivenALocalAccount();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Handler(scope: new TenantScope(NullLogger<TenantScope>.Instance))
                .Handle(Reset(), CancellationToken.None));
    }
}
