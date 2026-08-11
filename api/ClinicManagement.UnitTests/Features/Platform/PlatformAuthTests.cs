using ClinicManagement.API.Controllers.Platform;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Platform.Auth;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The console's sign-in surface (<c>platform-console</c> AC-1.2, AC-1.3, AC-1.3a, AC-1.3b, AC-1.5, EC-1–EC-3).
///
/// <para><b>Most of this class is about what a refusal must NOT reveal.</b> The console's account population is
/// two or three addresses, so an endpoint that distinguishes « no such account » from « wrong password » from
/// « wrong code » is an enumeration oracle over a set small enough to walk — and one that says which half of a
/// two-factor credential was right is worse. The assertions therefore compare refusal <b>codes</b>, which is also
/// what the controller maps to a status: recovering that from the French sentence would mean a reword silently
/// changed an HTTP status.</para>
/// </summary>
public class PlatformAuthTests
{
    private const string Password = "un-mot-de-passe";
    private const string Secret = "JBSWY3DPEHPK3PXP";
    private const string GoodCode = "123456";

    /// <summary>
    /// The four seams a console sign-in touches, wired to a real <see cref="PlatformAccount"/>.
    ///
    /// <para>The TOTP service and the protector are mocked rather than real: <c>TotpServiceTests</c> owns whether
    /// the algorithm is right, and mixing a live clock into these cases would make them depend on when the suite
    /// runs — the fixture defect <c>ClinicClockTests</c> exists to warn about.</para>
    /// </summary>
    private sealed class Harness
    {
        public Mock<IPlatformAccountRepository> Accounts { get; } = new();
        public Mock<IPlatformAuthService> Auth { get; } = new();
        public Mock<IPlatformSecretProtector> Protector { get; } = new();
        public Mock<ITotpService> Totp { get; } = new();
        public Mock<IUnitOfWork> UnitOfWork { get; } = new();
        public PlatformAccount Account { get; }

        public Harness(bool enrolled = true, bool active = true)
        {
            Account = PlatformAccount.Create("ops@editeur.tn", "Ops", "stored-hash");
            Account.SetPassword("stored-hash", mustChangePassword: false);

            if (enrolled)
            {
                Account.IssueTotpSecret("protected-secret");
                Account.CompleteTotpEnrolment(RecoveryCodes);
            }

            if (!active)
            {
                Account.Deactivate();
            }

            Accounts.Setup(a => a.GetByEmailAsync("ops@editeur.tn", It.IsAny<CancellationToken>()))
                .ReturnsAsync(Account);

            Auth.Setup(a => a.VerifyPassword("stored-hash", Password))
                .Returns(PasswordVerificationOutcome.Success);
            Auth.Setup(a => a.VerifyPassword(It.IsAny<string>(), It.Is<string>(p => p != Password)))
                .Returns(PasswordVerificationOutcome.Failed);
            Auth.Setup(a => a.GenerateToken(It.IsAny<PlatformAccount>()))
                .Returns(new PlatformAuthToken("a-token", DateTime.UtcNow.AddHours(4)));

            var secret = Secret;
            Protector.Setup(p => p.TryUnprotect("protected-secret", out secret)).Returns(true);
            Totp.Setup(t => t.VerifyCode(Secret, GoodCode)).Returns(true);
            Totp.Setup(t => t.VerifyCode(It.IsAny<string>(), It.Is<string>(c => c != GoodCode))).Returns(false);
        }

        public static List<string> RecoveryCodes { get; } = new() { "AAAABBBBCCCCDDDDEEEE", "FFFFGGGGHHHHJJJJKKKK" };

        public PlatformLoginCommandHandler Login() => new(
            Accounts.Object, Auth.Object, Protector.Object, Totp.Object, UnitOfWork.Object,
            NullLogger<PlatformLoginCommandHandler>.Instance);

        public EnrolPlatformTotpCommandHandler Enrol() => new(
            Accounts.Object, Auth.Object, Protector.Object, Totp.Object, UnitOfWork.Object);

        public RedeemPlatformRecoveryCodeCommandHandler Recovery() => new(
            Accounts.Object, Auth.Object, UnitOfWork.Object);
    }

    // ---------------------------------------------------------------- sign-in

    [Fact]
    public async Task Password_and_a_valid_code_sign_in()
    {
        var harness = new Harness();

        var result = await harness.Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, GoodCode), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("a-token", result.Value!.Token);
        // An ordinary sign-in does not report the recovery-code count — see PlatformSessionDto.
        Assert.Null(result.Value.RecoveryCodesRemaining);
    }

    // [AC-1.3 / EC-2] THE case this endpoint exists to get right: a leaked password alone yields no secret, no
    // recovery codes and no session — including on an account that has never signed in, which is the state a
    // freshly-bootstrapped one is in. The 403 carries nothing but its code.
    [Fact]
    public async Task A_password_alone_on_an_unenrolled_account_yields_no_secret_and_no_session()
    {
        var harness = new Harness(enrolled: false);

        var result = await harness.Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, null), default);

        Assert.True(result.IsFailure);
        Assert.Equal(PlatformAuthRefusals.TotpEnrolmentRequired, result.Code);
        Assert.Null(result.Value);
        // Nothing in the message may carry the secret — that is the whole of EC-2.
        Assert.DoesNotContain(Secret, result.Error);
        Assert.Equal(StatusCodes.Status403Forbidden, PlatformAuthController.StatusFor(result.Code));
    }

    // A missing code is a client mistake with no attacker value, so it gets its own refusal…
    [Fact]
    public async Task An_omitted_code_is_refused_as_totp_required()
    {
        var result = await new Harness().Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, null), default);

        Assert.Equal(PlatformAuthRefusals.TotpRequired, result.Code);
    }

    // …while a code that is PRESENT AND WRONG collapses into the same refusal as a wrong password, or the
    // endpoint reports which half of the credential was correct.
    [Fact]
    public async Task A_wrong_code_and_a_wrong_password_are_indistinguishable()
    {
        var wrongCode = await new Harness().Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, "000000"), default);
        var wrongPassword = await new Harness().Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", "pas-le-bon", GoodCode), default);

        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, wrongCode.Code);
        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, wrongPassword.Code);
        Assert.Equal(wrongCode.Error, wrongPassword.Error);
    }

    // [EC-1] An unknown address is the same refusal again — with a population of two or three accounts, anything
    // else is an enumeration oracle over a set small enough to walk.
    [Fact]
    public async Task An_unknown_address_is_refused_identically()
    {
        var harness = new Harness();
        harness.Accounts.Setup(a => a.GetByEmailAsync("inconnu@ailleurs.tn", It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlatformAccount?)null);

        var result = await harness.Login().Handle(
            new PlatformLoginCommand("inconnu@ailleurs.tn", Password, GoodCode), default);

        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, result.Code);
    }

    // A deactivated account is disclosed only AFTER the password is known correct — the same line the clinic
    // login draws, so the refusal is useful to its owner without being an oracle to anyone else.
    [Fact]
    public async Task A_deactivated_account_is_refused_after_the_password_verifies()
    {
        var harness = new Harness(active: false);

        var correct = await harness.Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, GoodCode), default);
        var wrong = await new Harness(active: false).Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", "pas-le-bon", GoodCode), default);

        Assert.Equal(PlatformAuthRefusals.AccountDisabled, correct.Code);
        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, wrong.Code);
    }

    // [AC-1.5] The durable lockout backstop is checked BEFORE the password, so a locked account is not a
    // password oracle either.
    [Fact]
    public async Task A_locked_out_account_is_refused_before_the_password_is_read()
    {
        var harness = new Harness();
        for (var i = 0; i < PlatformAccount.MaxFailedLoginAttempts; i++)
        {
            harness.Account.RecordFailedLogin();
        }

        var result = await harness.Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, GoodCode), default);

        Assert.Equal(PlatformAuthRefusals.TooManyAttempts, result.Code);
        harness.Auth.Verify(a => a.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // An undecryptable secret refuses the sign-in. ⚠️ The tempting degradation — treating a lost key ring as
    // « no second factor required » — would turn a storage failure into a security hole, silently.
    [Fact]
    public async Task An_undecryptable_secret_refuses_rather_than_skipping_the_factor()
    {
        var harness = new Harness();
        var none = string.Empty;
        harness.Protector.Setup(p => p.TryUnprotect("protected-secret", out none)).Returns(false);

        var result = await harness.Login().Handle(
            new PlatformLoginCommand("ops@editeur.tn", Password, GoodCode), default);

        Assert.True(result.IsFailure);
        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, result.Code);
    }

    // ---------------------------------------------------------------- enrolment

    [Fact]
    public async Task Enrolment_binds_the_factor_and_returns_the_codes_once()
    {
        var harness = new Harness(enrolled: false);
        harness.Account.IssueTotpSecret("protected-secret");

        var result = await harness.Enrol().Handle(
            new EnrolPlatformTotpCommand("ops@editeur.tn", Password, GoodCode), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlatformRecoveryCode.CountPerEnrolment, result.Value!.RecoveryCodes.Count);
        Assert.True(harness.Account.IsTotpEnrolled);

        // Stored so they can be CHECKED, never read back: no plaintext code appears on any persisted row.
        var plaintext = result.Value.RecoveryCodes.ToHashSet();
        Assert.All(harness.Account.RecoveryCodes, row => Assert.DoesNotContain(row.CodeHash, plaintext));
    }

    // Nothing is bound on a wrong code — the account is left exactly as it was and the attempt can be retried.
    [Fact]
    public async Task A_wrong_code_binds_nothing()
    {
        var harness = new Harness(enrolled: false);
        harness.Account.IssueTotpSecret("protected-secret");

        var result = await harness.Enrol().Handle(
            new EnrolPlatformTotpCommand("ops@editeur.tn", Password, "000000"), default);

        Assert.Equal(PlatformAuthRefusals.TotpInvalid, result.Code);
        Assert.False(harness.Account.IsTotpEnrolled);
        Assert.Empty(harness.Account.RecoveryCodes);
        Assert.Equal(StatusCodes.Status400BadRequest, PlatformAuthController.StatusFor(result.Code));
    }

    [Fact]
    public async Task Enrolling_twice_is_a_conflict()
    {
        var result = await new Harness().Enrol().Handle(
            new EnrolPlatformTotpCommand("ops@editeur.tn", Password, GoodCode), default);

        Assert.Equal(PlatformAuthRefusals.TotpAlreadyEnrolled, result.Code);
        Assert.Equal(StatusCodes.Status409Conflict, PlatformAuthController.StatusFor(result.Code));
    }

    // ---------------------------------------------------------------- recovery

    [Fact]
    public async Task A_recovery_code_signs_in_once_and_reports_what_is_left()
    {
        var harness = new Harness();

        var first = await harness.Recovery().Handle(
            new RedeemPlatformRecoveryCodeCommand("ops@editeur.tn", Password, Harness.RecoveryCodes[0]), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(1, first.Value!.RecoveryCodesRemaining);

        var reuse = await harness.Recovery().Handle(
            new RedeemPlatformRecoveryCodeCommand("ops@editeur.tn", Password, Harness.RecoveryCodes[0]), default);

        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, reuse.Code);
    }

    // [AC-1.3b] THE case: the code is consumed even when the sign-in it accompanied does not complete. A code
    // that has been transmitted has been exposed, so treating it as unspent because a LATER check refused would
    // make a single-use credential replayable — and the consumption is saved before those checks run.
    [Fact]
    public async Task A_recovery_code_is_consumed_even_when_the_sign_in_fails()
    {
        var harness = new Harness(active: false);

        var result = await harness.Recovery().Handle(
            new RedeemPlatformRecoveryCodeCommand("ops@editeur.tn", Password, Harness.RecoveryCodes[0]), default);

        Assert.True(result.IsFailure);
        Assert.Equal(PlatformAuthRefusals.AccountDisabled, result.Code);
        Assert.True(harness.Account.RecoveryCodes.Single(c => c.CodeHash == PlatformRecoveryCode.Hash(Harness.RecoveryCodes[0])).IsUsed);
        Assert.Equal(1, harness.Account.UnusedRecoveryCodeCount);
    }

    // …but a WRONG PASSWORD must not spend one, or anyone who knows the address can burn all eight and destroy
    // the recovery path AC-8.2 guarantees. The two orderings look alike and are opposite trades.
    [Fact]
    public async Task A_wrong_password_spends_no_recovery_code()
    {
        var harness = new Harness();

        var result = await harness.Recovery().Handle(
            new RedeemPlatformRecoveryCodeCommand("ops@editeur.tn", "pas-le-bon", Harness.RecoveryCodes[0]), default);

        Assert.Equal(PlatformAuthRefusals.InvalidCredentials, result.Code);
        Assert.Equal(2, harness.Account.UnusedRecoveryCodeCount);
    }

    // A code read aloud and typed back with spacing still matches — the formatting is a display choice, and the
    // stored form is what Normalize says it is.
    [Fact]
    public async Task A_recovery_code_matches_regardless_of_spacing_and_case()
    {
        var harness = new Harness();

        var result = await harness.Recovery().Handle(
            new RedeemPlatformRecoveryCodeCommand(
                "ops@editeur.tn", Password, "aaaa bbbb-cccc dddd eeee"), default);

        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------- the refusal vocabulary

    // Every code this feature declares resolves to a French sentence AND to an explicit status. The controller's
    // switch has a 400 fallback, so without this a new code would silently take it — which is how a 409 or a 429
    // quietly becomes a 400 nobody notices.
    [Fact]
    public void Every_declared_refusal_has_a_message_and_an_explicit_status()
    {
        foreach (var code in PlatformAuthRefusals.AllCodes)
        {
            Assert.False(string.IsNullOrWhiteSpace(PlatformAuthRefusals.MessageFor(code)), code);

            // Not the fallback: assert against the codes the spec gives a non-400 status, and that the rest are
            // deliberate 400s rather than unmapped ones.
            var status = PlatformAuthController.StatusFor(code);
            Assert.True(status is StatusCodes.Status400BadRequest or StatusCodes.Status401Unauthorized
                or StatusCodes.Status403Forbidden or StatusCodes.Status409Conflict
                or StatusCodes.Status429TooManyRequests, $"{code} → {status}");
        }
    }

    [Fact]
    public void An_unknown_code_has_no_message_so_a_typo_cannot_masquerade_as_a_refusal()
    {
        Assert.Null(PlatformAuthRefusals.MessageFor("not_a_real_code"));
    }
}
