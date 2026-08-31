using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// The clinic second factor: the login ladder, enrolment, and the recovery code
/// (<c>hosted-security-hardening</c> FR-1.1 – FR-1.4).
///
/// <para><b>Most of what matters here is what the ladder must NOT do.</b> It stands in front of every clinic
/// sign-in on the hosted deployment, so a wrong « refuse » does not degrade a feature — it locks a practice out
/// of its own records mid-consultation. Hence the cases asserting that a secretary, a doctor with no factor, and
/// every account on a deployment that does not require one all sign in exactly as before.</para>
/// </summary>
public class ClinicTotpAuthTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicRepository> _clinics = new();
    private readonly Mock<ILocalAuthService> _auth = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ILoginAttemptTracker> _attempts = new();
    private readonly Mock<ITotpService> _totp = new();
    private readonly Mock<IUserSecretProtector> _secrets = new();
    private readonly Mock<ISecondFactorPolicy> _policy = new();
    private readonly Mock<IQrCodeGenerator> _qr = new();
    private readonly Mock<ISessionFamilyRepository> _sessionFamilies = new();

    public ClinicTotpAuthTests()
    {
        _auth.Setup(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationOutcome.Success);
        _auth.Setup(a => a.GenerateToken(It.IsAny<User>()))
            .Returns(new LocalAuthToken("access-jwt", DateTime.UtcNow.AddMinutes(30)));
        _auth.Setup(a => a.GenerateRefreshToken(It.IsAny<User>(), It.IsAny<Guid?>()))
            .Returns(new LocalAuthToken("refresh-jwt", DateTime.UtcNow.AddHours(12)));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(ClinicId, "Cabinet Ben Salah", null, null, null, "ABC123"));

        // The protector round-trips by default, so a test that cares about a broken key ring says so explicitly.
        _secrets.Setup(p => p.Protect(It.IsAny<string>())).Returns((string s) => "protected:" + s);
        _secrets.Setup(p => p.TryUnprotect(It.IsAny<string>(), out It.Ref<string>.IsAny))
            .Returns((string protectedSecret, out string secret) =>
            {
                secret = protectedSecret.StartsWith("protected:", StringComparison.Ordinal)
                    ? protectedSecret["protected:".Length..]
                    : string.Empty;
                return secret.Length > 0;
            });
    }

    /// <summary>
    /// Permissive by default — every scenario here is about the factor itself, not about re-presenting a code, so
    /// a first presentation must behave exactly as it did before the guard existed.
    /// </summary>
    private readonly Mock<ITotpReplayGuard> _replay = new();

    private readonly Mock<IAuditActorProvider> _auditActor = new();

    private LoginCommandHandler LoginHandler()
    {
        _replay.Setup(g => g.TryConsume(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        return new(
            _users.Object, _auth.Object, _uow.Object, _attempts.Object,
            _totp.Object, _replay.Object, _secrets.Object, _policy.Object,
            _sessionFamilies.Object, _auditActor.Object);
    }

    // ⚠️ The attempt tracker is now a dependency, and it is permissive here by default: every scenario in this
    // class is about the enrolment flow, not about the brake. `An_enrolment_attempt_is_rate_limited_like_a_login`
    // below is where the brake itself is asserted.
    private EnrolTotpCommandHandler EnrolHandler() => new(
        _users.Object, _clinics.Object, _auth.Object, _totp.Object, _secrets.Object, _qr.Object,
        _attempts.Object, _uow.Object);

    private RedeemRecoveryCodeCommandHandler RecoveryHandler() => new(
        _users.Object, _auth.Object, _attempts.Object, _uow.Object, _sessionFamilies.Object);

    private User Account(string role)
    {
        var user = User.CreateLocalUser(ClinicId, role, "someone@clinic.com", "STORED-HASH", "Someone");
        _users.Setup(r => r.GetByEmailAsync("someone@clinic.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return user;
    }

    /// <summary>Enrols the account with a known secret, the way the real enrolment leaves it.</summary>
    private static User Enrolled(User user, string secret = "JBSWY3DPEHPK3PXP")
    {
        user.IssueTotpSecret("protected:" + secret);
        user.CompleteTotpEnrolment(Enumerable.Range(0, 8).Select(_ => UserRecoveryCode.NewCode()).ToList());
        return user;
    }

    private static LoginCommand Login(string? code = null) => new()
    {
        Email = "someone@clinic.com",
        Password = "un-mot-de-passe-long",
        TotpCode = code
    };

    // ── The refusal this feature exists for ──────────────────────────────────────────────────────────────

    // [FR-1.1] An administrator with the CORRECT password and no factor cannot obtain a token.
    [Fact]
    public async Task An_Admin_With_No_Factor_Cannot_Obtain_A_Token()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Account(User.RoleAdmin);

        var result = await LoginHandler().Handle(Login(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpEnrolmentRequired, result.Code);
        // The whole point: no session of any kind comes back with it.
        Assert.Null(result.Value);
    }

    // [FR-1.2] Enrolled, no code offered yet — the ordinary first half of a two-step sign-in, and NOT a failed
    // attempt: the password was right and nobody has asked for the code yet.
    [Fact]
    public async Task An_Enrolled_Account_With_No_Code_Is_Asked_For_One_And_Spends_No_Attempt()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Enrolled(Account(User.RoleAdmin));

        var result = await LoginHandler().Handle(Login(), CancellationToken.None);

        Assert.Equal(ClinicAuthRefusals.TotpRequired, result.Code);
        _attempts.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Never);
    }

    // [FR-1.2] A present-but-WRONG code is indistinguishable from a wrong password, and spends an attempt.
    [Fact]
    public async Task A_Wrong_Code_Reads_As_Invalid_Credentials_And_Spends_An_Attempt()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Enrolled(Account(User.RoleAdmin));
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), "000000")).Returns(false);

        var result = await LoginHandler().Handle(Login("000000"), CancellationToken.None);

        Assert.Equal(ClinicAuthRefusals.InvalidCredentials, result.Code);
        _attempts.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Once);
    }

    // [FR-1.3] A correct code signs in.
    [Fact]
    public async Task A_Correct_Code_Signs_In()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Enrolled(Account(User.RoleAdmin));
        _totp.Setup(t => t.VerifyCode("JBSWY3DPEHPK3PXP", "123456")).Returns(true);

        var result = await LoginHandler().Handle(Login("123456"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-jwt", result.Value!.AccessToken);
    }

    /// <summary>
    /// [FR-1.3] ⚠️ <b>An undecryptable secret REFUSES — it never falls through to « no factor required ».</b>
    /// This is the single highest-value case in the file: the failure it guards is silent, deployment-wide and
    /// in the wrong direction. A key ring lost or rotated would otherwise disarm the second factor for every
    /// administrator at once, with every layer still reporting the feature present.
    /// </summary>
    [Fact]
    public async Task An_Undecryptable_Secret_Refuses_Rather_Than_Bypassing()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Enrolled(Account(User.RoleAdmin));
        var unreadable = string.Empty;
        _secrets.Setup(p => p.TryUnprotect(It.IsAny<string>(), out unreadable)).Returns(false);

        var result = await LoginHandler().Handle(Login("123456"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Null(result.Value);
        // And it never asks the TOTP service, so it cannot accidentally succeed on an empty secret.
        _totp.Verify(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ── What must NOT change ────────────────────────────────────────────────────────────────────────────

    // [AC-6] A secretary on a requiring deployment signs in with a password alone — the requirement is about
    // ADMINISTRATORS, and widening it here would lock reception out of the agenda.
    [Fact]
    public async Task A_Secretary_On_A_Requiring_Deployment_Signs_In_With_A_Password_Alone()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        Account(User.RoleSecretary);

        var result = await LoginHandler().Handle(Login(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // [AC-6] And an administrator on a deployment that does NOT require one is untouched — this is what keeps
    // SelfHostedLan and CloudBrowser byte-for-byte unchanged.
    [Fact]
    public async Task An_Admin_On_A_Non_Requiring_Deployment_Signs_In_With_A_Password_Alone()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(false);
        Account(User.RoleAdmin);

        var result = await LoginHandler().Handle(Login(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// [FR-1.5] A doctor who enrolled <b>voluntarily</b> is asked for their code even where the deployment
    /// requires none. Offering enrolment and then not checking it would be worse than never offering it.
    /// </summary>
    [Fact]
    public async Task A_Voluntarily_Enrolled_Doctor_Is_Still_Asked_For_A_Code()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(false);
        Enrolled(Account(User.RoleDoctor));

        var result = await LoginHandler().Handle(Login(), CancellationToken.None);

        Assert.Equal(ClinicAuthRefusals.TotpRequired, result.Code);
    }

    // ── Enrolment ───────────────────────────────────────────────────────────────────────────────────────

    // [FR-1.3] Step one hands back something to scan and mints NO recovery codes.
    [Fact]
    public async Task Enrolment_Step_One_Issues_A_Secret_And_No_Codes()
    {
        var user = Account(User.RoleAdmin);
        _totp.Setup(t => t.GenerateSecret()).Returns("JBSWY3DPEHPK3PXP");

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand { Email = "someone@clinic.com", Password = "un-mot-de-passe-long" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RecoveryCodes);
        Assert.Contains("otpauth://totp/", result.Value.SecretUri);
        // The practice's name AND the address, so two practices are tellable apart in one authenticator.
        Assert.Contains("Cabinet%20Ben%20Salah", result.Value.SecretUri);
        Assert.Contains("someone%40clinic.com", result.Value.SecretUri);
        // Issued but unconfirmed — the state the ladder refuses on.
        Assert.False(user.IsTotpEnrolled);
    }

    // This endpoint verifies a password and branches distinguishably on the result, so without the two lockout
    // tiers it is a password oracle — and an UNAUTHENTICATED one, beside a login path that has both. The rule
    // was written for RedeemRecoveryCodeCommand (« or this endpoint would be the unrated door beside a
    // rate-limited one ») and never reached here.
    [Fact]
    public async Task An_enrolment_attempt_is_rate_limited_like_a_login()
    {
        Account(User.RoleAdmin);
        _attempts.Setup(a => a.IsLockedOutForCurrentSource(It.IsAny<string>())).Returns(true);

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand { Email = "someone@clinic.com", Password = "un-mot-de-passe-long" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TooManyAttempts, result.Code);
        // Refused BEFORE the password is looked at, so a locked-out caller learns nothing from the branch.
        _auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // And the counter half: a wrong password here must cost an attempt, or the lockout above can never trip.
    [Fact]
    public async Task A_wrong_password_at_enrolment_spends_an_attempt()
    {
        Account(User.RoleAdmin);
        _auth.Setup(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationOutcome.Failed);

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand { Email = "someone@clinic.com", Password = "mauvais-mot-de-passe" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.InvalidCredentials, result.Code);
        _attempts.Verify(a => a.RecordFailure(It.IsAny<string>()), Times.Once);
    }

    // [FR-1.3] Step two mints exactly eight codes, once.
    [Fact]
    public async Task Enrolment_Step_Two_Mints_Eight_Codes_Once()
    {
        var user = Account(User.RoleAdmin);
        user.IssueTotpSecret("protected:JBSWY3DPEHPK3PXP");
        _totp.Setup(t => t.VerifyCode("JBSWY3DPEHPK3PXP", "123456")).Returns(true);

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand
            {
                Email = "someone@clinic.com",
                Password = "un-mot-de-passe-long",
                TotpCode = "123456"
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, result.Value!.RecoveryCodes!.Count);
        Assert.True(user.IsTotpEnrolled);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, user.UnusedRecoveryCodeCount);
    }

    /// <summary>
    /// [FR-1.3] ⚠️ A <b>wrong password</b> mints nothing and — critically — does not clear an existing
    /// enrolment. <c>IssueTotpSecret</c> wipes the secret and every recovery code, so an unauthenticated step
    /// one would be a denial-of-service on a colleague's sign-in.
    /// </summary>
    [Fact]
    public async Task A_Wrong_Password_Cannot_Reset_Somebody_Elses_Enrolment()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        _auth.Setup(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationOutcome.Failed);

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand { Email = "someone@clinic.com", Password = "wrong" },
            CancellationToken.None);

        Assert.Equal(ClinicAuthRefusals.InvalidCredentials, result.Code);
        Assert.True(user.IsTotpEnrolled);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, user.UnusedRecoveryCodeCount);
        _totp.Verify(t => t.GenerateSecret(), Times.Never);
    }

    // [FR-1.3] Re-enrolling over a live factor is refused, not silently accepted.
    [Fact]
    public async Task Enrolling_Twice_Is_Refused()
    {
        Enrolled(Account(User.RoleAdmin));

        var result = await EnrolHandler().Handle(
            new EnrolTotpCommand { Email = "someone@clinic.com", Password = "un-mot-de-passe-long" },
            CancellationToken.None);

        Assert.Equal(ClinicAuthRefusals.TotpAlreadyEnrolled, result.Code);
    }

    // ── Recovery codes ──────────────────────────────────────────────────────────────────────────────────

    // [FR-1.4] A correct code signs in and is spent.
    [Fact]
    public async Task A_Recovery_Code_Signs_In_And_Is_Spent()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        var code = UserRecoveryCode.NewCode();
        user.ReplaceRecoveryCodes(new[] { code });

        var result = await RecoveryHandler().Handle(
            new RedeemRecoveryCodeCommand
            {
                Email = "someone@clinic.com",
                Password = "un-mot-de-passe-long",
                RecoveryCode = code
            },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, user.UnusedRecoveryCodeCount);
    }

    /// <summary>
    /// [FR-1.4] ⚠️ <b>A wrong password burns NO code.</b> Otherwise anybody who learned an address could spend
    /// all eight by guessing, and the account's own way back would be gone before its owner needed it.
    /// </summary>
    [Fact]
    public async Task A_Wrong_Password_Burns_No_Recovery_Code()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        var code = UserRecoveryCode.NewCode();
        user.ReplaceRecoveryCodes(new[] { code });
        _auth.Setup(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(PasswordVerificationOutcome.Failed);

        var result = await RecoveryHandler().Handle(
            new RedeemRecoveryCodeCommand
            {
                Email = "someone@clinic.com",
                Password = "wrong",
                RecoveryCode = code
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(1, user.UnusedRecoveryCodeCount);
    }

    /// <summary>
    /// [FR-1.4] ⚠️ <b>A code is spent even when the sign-in it accompanied then FAILS.</b> It has been
    /// transmitted, so treating it as unspent would make a single-use credential replayable. Here the account is
    /// deactivated, which is refused after the code is consumed and saved.
    /// </summary>
    [Fact]
    public async Task A_Recovery_Code_Is_Spent_Even_When_The_Sign_In_Then_Fails()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        var code = UserRecoveryCode.NewCode();
        user.ReplaceRecoveryCodes(new[] { code });
        user.Deactivate();

        var result = await RecoveryHandler().Handle(
            new RedeemRecoveryCodeCommand
            {
                Email = "someone@clinic.com",
                Password = "un-mot-de-passe-long",
                RecoveryCode = code
            },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.AccountDisabled, result.Code);
        // Spent regardless — and persisted before the refusal above was reached.
        Assert.Equal(0, user.UnusedRecoveryCodeCount);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // [FR-1.4] A code cannot be spent twice.
    [Fact]
    public async Task A_Recovery_Code_Cannot_Be_Spent_Twice()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        var code = UserRecoveryCode.NewCode();
        user.ReplaceRecoveryCodes(new[] { code });

        var command = new RedeemRecoveryCodeCommand
        {
            Email = "someone@clinic.com",
            Password = "un-mot-de-passe-long",
            RecoveryCode = code
        };

        Assert.True((await RecoveryHandler().Handle(command, CancellationToken.None)).IsSuccess);
        var second = await RecoveryHandler().Handle(command, CancellationToken.None);

        Assert.True(second.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpInvalid, second.Code);
    }

    // ── Replacing a lost factor with a recovery code (the sole-administrator way back) ──────────────────
    //
    // ⚠️ What these pin is a pair of opposites that must BOTH hold. Signing in with a recovery code has to make
    // the factor replaceable — a cabinet with one administrator has nobody to reset it for them, so without this
    // they could enter eight times and be locked out for good on the ninth. And nothing else may make it
    // replaceable, because a factor a password alone can move is worth exactly what the password is worth.

    private static EnrolTotpCommand Enrol(string? code = null) => new()
    {
        Email = "someone@clinic.com",
        Password = "un-mot-de-passe-long",
        TotpCode = code
    };

    private RedeemRecoveryCodeCommand Redeem(string code) => new()
    {
        Email = "someone@clinic.com",
        Password = "un-mot-de-passe-long",
        RecoveryCode = code
    };

    /// <summary>Enrols the account and leaves it holding exactly one known recovery code.</summary>
    private User EnrolledWithOneCode(out string code, string role = User.RoleAdmin)
    {
        var user = Enrolled(Account(role));
        code = UserRecoveryCode.NewCode();
        user.ReplaceRecoveryCodes(new[] { code });
        return user;
    }

    // A redeemed code opens the window, and says so in the result the screen reads.
    [Fact]
    public async Task Redeeming_A_Recovery_Code_Allows_The_Factor_To_Be_Replaced()
    {
        var user = EnrolledWithOneCode(out var code);

        var result = await RecoveryHandler().Handle(Redeem(code), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(user.IsTotpReplacementGranted());
        Assert.True(result.Value!.MayReplaceSecondFactor);
    }

    /// <summary>
    /// The point of the whole grant: enrolment step one is accepted on an account that is <b>already enrolled</b>,
    /// which is otherwise <c>totp_already_enrolled</c> and the reason a lost phone could not be replaced.
    /// </summary>
    [Fact]
    public async Task With_The_Window_Open_An_Enrolled_Account_May_Start_A_New_Enrolment()
    {
        var user = EnrolledWithOneCode(out var code);
        await RecoveryHandler().Handle(Redeem(code), CancellationToken.None);
        _totp.Setup(t => t.GenerateSecret()).Returns("NEWSECRET234567");

        var result = await EnrolHandler().Handle(Enrol(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.SecretUri);
        // Step one clears the old enrolment and every code it was proven with.
        Assert.False(user.IsTotpEnrolled);
        Assert.Equal(0, user.UnusedRecoveryCodeCount);
    }

    /// <summary>
    /// ⚠️ The other half, and the one that must never regress: <b>without</b> a redeemed code the refusal stands.
    /// A caller holding only the password is exactly who this refusal exists for.
    /// </summary>
    [Fact]
    public async Task Without_The_Window_An_Enrolled_Account_Still_Cannot_Re_Enrol()
    {
        var user = Enrolled(Account(User.RoleAdmin));

        var result = await EnrolHandler().Handle(Enrol(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpAlreadyEnrolled, result.Code);
        // Untouched: a refused step one must not have cleared the factor it refused to replace.
        Assert.True(user.IsTotpEnrolled);
    }

    // The window is a deadline, not a flag: once it passes, the refusal is back.
    [Fact]
    public async Task An_Expired_Window_Does_Not_Allow_A_Replacement()
    {
        var user = Enrolled(Account(User.RoleAdmin));
        user.GrantTotpReplacement(TimeSpan.FromMinutes(15), DateTime.UtcNow.AddHours(-2));

        Assert.False(user.IsTotpReplacementGranted());

        var result = await EnrolHandler().Handle(Enrol(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ClinicAuthRefusals.TotpAlreadyEnrolled, result.Code);
    }

    /// <summary>
    /// ⚠️ <b>Single-use, like the code that bought it.</b> A completed enrolment spends the grant, so the window
    /// cannot be reused later by whoever next presents the password.
    /// </summary>
    [Fact]
    public async Task Completing_The_New_Enrolment_Spends_The_Window()
    {
        var user = EnrolledWithOneCode(out var code);
        await RecoveryHandler().Handle(Redeem(code), CancellationToken.None);
        _totp.Setup(t => t.GenerateSecret()).Returns("NEWSECRET234567");
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        await EnrolHandler().Handle(Enrol(), CancellationToken.None);
        var confirmed = await EnrolHandler().Handle(Enrol("123456"), CancellationToken.None);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, confirmed.Value!.RecoveryCodes!.Count);
        Assert.True(user.IsTotpEnrolled);
        Assert.False(user.IsTotpReplacementGranted());
    }

    // A wrong code is refused, and refusing must not hand out the right to replace the factor.
    [Fact]
    public async Task A_Refused_Recovery_Code_Opens_No_Window()
    {
        var user = Enrolled(Account(User.RoleAdmin));

        var result = await RecoveryHandler().Handle(
            Redeem(UserRecoveryCode.NewCode()), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.False(user.IsTotpReplacementGranted());
    }

    /// <summary>
    /// A deactivated account spends its code (that rule is pinned above) but earns nothing: the sign-in was
    /// refused, and a window opened here would outlive the refusal by fifteen minutes.
    /// </summary>
    [Fact]
    public async Task A_Disabled_Account_Spends_Its_Code_But_Earns_No_Window()
    {
        var user = EnrolledWithOneCode(out var code);
        user.Deactivate();

        var result = await RecoveryHandler().Handle(Redeem(code), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, user.UnusedRecoveryCodeCount);
        Assert.False(user.IsTotpReplacementGranted());
    }

    // An administrator's or the vendor's reset settles the factor, so any window it had is moot.
    [Fact]
    public async Task Resetting_The_Factor_Closes_An_Open_Window()
    {
        var user = EnrolledWithOneCode(out var code);
        await RecoveryHandler().Handle(Redeem(code), CancellationToken.None);
        Assert.True(user.IsTotpReplacementGranted());

        user.DisableTotp();

        Assert.False(user.IsTotpReplacementGranted());
    }

    // The ordinary ladder hands out no window — only a redeemed code does.
    [Fact]
    public async Task An_Ordinary_Sign_In_Opens_No_Window()
    {
        _policy.SetupGet(p => p.RequiresAdminSecondFactor).Returns(true);
        var user = Enrolled(Account(User.RoleAdmin));
        _totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

        var result = await LoginHandler().Handle(Login("123456"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsTotpReplacementGranted());
        Assert.False(result.Value!.MayReplaceSecondFactor);
    }

    // ── The vocabulary ──────────────────────────────────────────────────────────────────────────────────

    // Every declared code resolves to a French sentence. Derived from the constants, so a new code cannot be
    // added without one — the failure this file's own « code and sentence in the same file » rule prevents.
    [Fact]
    public void Every_Declared_Refusal_Has_A_French_Sentence()
    {
        Assert.NotEmpty(ClinicAuthRefusals.AllCodes);

        var missing = ClinicAuthRefusals.AllCodes
            .Where(c => string.IsNullOrWhiteSpace(ClinicAuthRefusals.MessageFor(c)))
            .ToList();

        Assert.True(missing.Count == 0, "Refusal code(s) with no sentence: " + string.Join(", ", missing));
    }

    // An unknown code resolves to null rather than a fallback sentence, so the test above can actually fail.
    [Fact]
    public void An_Unknown_Code_Has_No_Sentence()
    {
        Assert.Null(ClinicAuthRefusals.MessageFor("not_one_of_ours"));
    }
}
