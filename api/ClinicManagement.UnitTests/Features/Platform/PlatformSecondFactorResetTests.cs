using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Services;
using ClinicManagement.Application.Features.Platform;
using ClinicManagement.Application.Features.Platform.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.UnitTests.Features.Subscriptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Platform;

/// <summary>
/// The vendor clears one clinic account's second factor from the console — « way back #3 »
/// (<c>hosted-security-hardening</c> FR-1.4).
///
/// <para><b>What these pin is a pair of opposites.</b> The vendor must be able to do this, because a cabinet with a
/// single administrator who lost their phone and kept no recovery codes has nobody else to vouch for them. And the
/// operation must leave a record naming <i>whose</i> factor was cleared and <i>why</i> — because that record is the
/// only thing standing between this endpoint and a social-engineered telephone call, and because
/// <c>DisableTotp</c> writes no trace anywhere else in the product.</para>
///
/// <para>⚠️ <b>The load-bearing case is <see cref="An_Account_At_Another_Cabinet_Is_Refused"/>.</b> The address
/// resolves across the whole deployment, so an implementation that trusted it would disarm a stranger on a mis-keyed
/// character — and every other assertion here would still pass. The mirror is
/// <see cref="A_Refused_Reset_Writes_No_Journal_Row_And_Clears_Nothing"/>: a refusal that half-applied would leave a
/// cabinet's account disarmed with no row explaining it, which is worse than either outcome on its own.</para>
///
/// <para>⚠️ It runs the real command over the companion's in-memory harness rather than asserting on mocks, for
/// <c>PlatformSuspensionTests</c>' reason: the ACs are about what the <i>ledger row</i> ends up holding and what the
/// <i>account</i> ends up holding, and a verified method call proves neither.</para>
/// </summary>
public class PlatformSecondFactorResetTests
{
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-1111-1111-1111-aaaaaaaaaaaa");
    private const string AccountEmail = "vendeur@editeur.tn";
    private const string ClinicName = "Cabinet Ben Ali";
    private const string TargetEmail = "dr.bensalah@cabinet.tn";
    private const string Motif = "Appel du Dr Ben Salah, téléphone perdu, codes non conservés";

    private readonly SubscriptionVendorHarness _harness = new();
    private readonly FakeAccessLedger _ledger = new();
    private readonly Mock<INotificationGenerator> _notifications = new();
    private readonly Mock<ITransactionalEmailSender> _email = new();

    public PlatformSecondFactorResetTests()
    {
        _harness.Clinics
            .Setup(c => c.GetByIdAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Clinic(SubscriptionVendorHarness.ClinicId, ClinicName, city: "Tunis"));

        _harness.Clinics
            .Setup(c => c.ExistsAsync(SubscriptionVendorHarness.ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    // ------------------------------------------------------------------ harness

    private static ITenantScope SystemWideScope()
    {
        var scope = new TenantScope(NullLogger<TenantScope>.Instance);
        PlatformTenantScope.Declare(scope);
        return scope;
    }

    private ResetClinicUserSecondFactorFromConsoleCommandHandler Handler(
        IPlatformSessionContext? session = null, ITenantScope? scope = null) =>
        new(_harness.Clinics.Object, _harness.Users.Object, _ledger,
            session ?? new FakePlatformSession { AccountId = AccountId, Email = AccountEmail },
            _notifications.Object, _email.Object, _harness.UnitOfWork.Object, scope ?? SystemWideScope(),
            NullLogger<ResetClinicUserSecondFactorFromConsoleCommandHandler>.Instance);

    private static ResetClinicUserSecondFactorFromConsoleCommand Reset(
        string email = TargetEmail, string reason = Motif) =>
        new() { ClinicId = SubscriptionVendorHarness.ClinicId, Email = email, Reason = reason };

    /// <summary>
    /// An enrolled account at the cabinet, left exactly as a real enrolment leaves one: a confirmed secret and eight
    /// recovery codes.
    /// </summary>
    private User GivenAnEnrolledAccount(Guid? clinicId = null, string role = User.RoleAdmin)
    {
        var user = User.CreateLocalUser(
            clinicId ?? SubscriptionVendorHarness.ClinicId, role, TargetEmail, "STORED-HASH", "Salma Ben Salah");

        user.IssueTotpSecret("protected:JBSWY3DPEHPK3PXP");
        user.CompleteTotpEnrolment(
            Enumerable.Range(0, UserRecoveryCode.CountPerEnrolment)
                .Select(_ => UserRecoveryCode.NewCode())
                .ToList());

        _harness.Users
            .Setup(r => r.GetByEmailAsync(TargetEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        return user;
    }

    // ------------------------------------------------------------------ the way back it exists to be

    // The whole point: the factor is gone, its codes with it, and the sessions opened under it end.
    [Fact]
    public async Task Resetting_Clears_The_Factor_Its_Codes_And_The_Sessions()
    {
        var user = GivenAnEnrolledAccount();
        var versionBefore = user.TokenVersion;

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsTotpEnrolled);
        Assert.Equal(0, user.UnusedRecoveryCodeCount);
        // Bumped, so a session established under the stronger rule does not outlive it.
        Assert.True(user.TokenVersion > versionBefore);
    }

    /// <summary>
    /// ⚠️ The response names the <b>person</b>, not « c'est fait ». The vendor typed an address off a telephone
    /// call, and a mis-keyed character matching a colleague at the same cabinet is the one failure still fixable by
    /// ringing back — which requires knowing it happened.
    /// </summary>
    [Fact]
    public async Task The_Outcome_Names_Who_Was_Actually_Disarmed()
    {
        GivenAnEnrolledAccount();

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetEmail, result.Value!.TargetEmail);
        Assert.Equal("Salma Ben Salah", result.Value.TargetName);
        Assert.Equal(User.RoleAdmin, result.Value.TargetRole);
    }

    // ------------------------------------------------------------------ the record, which is the only one there is

    /// <summary>
    /// ⚠️ <b>The row carries the target and the motif</b>, and nothing else in the product does: a suspension writes
    /// its reason onto the entitlement and a cancellation onto the entry it strikes through, but <c>DisableTotp</c>
    /// keeps no trace at all. Without these two fields « qui a désarmé le compte de qui, et pourquoi ? » is
    /// unanswerable.
    /// </summary>
    [Fact]
    public async Task The_Journal_Row_Names_The_Target_And_Quotes_The_Motif()
    {
        var user = GivenAnEnrolledAccount();

        await Handler().Handle(Reset(), CancellationToken.None);

        var row = Assert.Single(_ledger.Rows);
        Assert.Equal(PlatformAccessAction.SecondFactorReset, row.Action);
        Assert.Equal(SubscriptionVendorHarness.ClinicId, row.ClinicId);
        Assert.Equal(ClinicName, row.ClinicName);
        Assert.Equal(AccountId, row.PlatformAccountId);
        Assert.Equal(AccountEmail, row.AccountEmail);
        Assert.Equal(user.Id, row.TargetUserId);
        Assert.Equal(TargetEmail, row.TargetEmail);
        Assert.Equal(Motif, row.Reason);
        // Neither of the two « the vendor was paid for something » columns: no money changed hands.
        Assert.Null(row.SubscriptionPeriodId);
        Assert.Null(row.MessagingAllowanceEntryId);
    }

    // [AC-6.1's sibling] A blank motif is refused in French, and nothing at all happens.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_Blank_Motif_Is_Refused_And_Nothing_Happens(string motif)
    {
        var user = GivenAnEnrolledAccount();

        var result = await Handler().Handle(Reset(reason: motif), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ResetClinicUserSecondFactorFromConsoleCommandHandler.ReasonRequiredError, result.Error);
        Assert.True(user.IsTotpEnrolled);
        Assert.Empty(_ledger.Rows);
    }

    // A missing address is refused before anything is looked up.
    [Fact]
    public async Task A_Blank_Address_Is_Refused()
    {
        var result = await Handler().Handle(Reset(email: "  "), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ResetClinicUserSecondFactorFromConsoleCommandHandler.EmailRequiredError, result.Error);
        Assert.Empty(_ledger.Rows);
    }

    // ------------------------------------------------------------------ the mis-keyed address

    /// <summary>
    /// ⚠️ <b>The case with teeth.</b> <c>GetByEmailAsync</c> resolves across the whole deployment, so a command that
    /// trusted the address would disarm somebody at a practice the vendor never opened — on one wrong character,
    /// with every other assertion in this file still green. The cabinet in the URL is what bounds it.
    /// </summary>
    [Fact]
    public async Task An_Account_At_Another_Cabinet_Is_Refused()
    {
        var stranger = GivenAnEnrolledAccount(clinicId: SubscriptionVendorHarness.OtherClinicId);

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ResetClinicUserSecondFactorFromConsoleCommandHandler.UnknownAccountCode, result.Code);
        Assert.True(stranger.IsTotpEnrolled);
        Assert.Empty(_ledger.Rows);
    }

    /// <summary>
    /// ⚠️ An unknown address and an address at another cabinet answer with the <b>same sentence</b>. Distinguishing
    /// them would make this endpoint a way of asking « does this person work at that practice? » about any address
    /// the vendor cares to type — a question about a cabinet's staff the console is not entitled to answer.
    /// </summary>
    [Fact]
    public async Task An_Unknown_Address_Is_Refused_In_The_Same_Words_As_A_Stranger()
    {
        _harness.Users
            .Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var unknown = await Handler().Handle(Reset(), CancellationToken.None);

        GivenAnEnrolledAccount(clinicId: SubscriptionVendorHarness.OtherClinicId);
        var stranger = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(unknown.IsFailure);
        Assert.True(stranger.IsFailure);
        Assert.Equal(stranger.Error, unknown.Error);
        Assert.Equal(stranger.Code, unknown.Code);
    }

    // ------------------------------------------------------------------ refusals that must not half-apply

    /// <summary>
    /// An account with no factor is a refusal, not a silent success — the suspension command's own reasoning: « c'est
    /// fait » would write a row for an action that never happened, and would tell the vendor the caller can now sign
    /// in while whatever is really blocking them is untouched.
    /// </summary>
    [Fact]
    public async Task An_Account_With_No_Factor_Is_Refused()
    {
        var user = User.CreateLocalUser(
            SubscriptionVendorHarness.ClinicId, User.RoleDoctor, TargetEmail, "STORED-HASH", "Sans facteur");
        _harness.Users
            .Setup(r => r.GetByEmailAsync(TargetEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ResetClinicUserSecondFactorFromConsoleCommandHandler.NotEnrolledCode, result.Code);
        Assert.Empty(_ledger.Rows);
    }

    /// <summary>
    /// The mirror of the journal test: a refusal leaves the account enrolled AND writes no row. Either half alone
    /// would be a worse outcome than the refusal — a disarmed account with no record, or a record of something that
    /// did not happen.
    /// </summary>
    [Fact]
    public async Task A_Refused_Reset_Writes_No_Journal_Row_And_Clears_Nothing()
    {
        var user = GivenAnEnrolledAccount();

        await Handler().Handle(Reset(reason: ""), CancellationToken.None);

        Assert.True(user.IsTotpEnrolled);
        Assert.Equal(UserRecoveryCode.CountPerEnrolment, user.UnusedRecoveryCodeCount);
        Assert.Empty(_ledger.Rows);
        _harness.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// An unattributable action must not aboutir — <c>PlatformAccessLedger.RequireAccountId</c>'s contract, reached
    /// here rather than swallowed. It throws, so the write never commits and no factor is cleared.
    /// </summary>
    [Fact]
    public async Task An_Unattributable_Reset_Throws_Rather_Than_Half_Applying()
    {
        GivenAnEnrolledAccount();

        var result = await Handler(session: new FakePlatformSession { AccountId = null })
            .Handle(Reset(), CancellationToken.None);

        // Caught by the handler's own catch-all and reported as a failure, but nothing was recorded either way.
        Assert.True(result.IsFailure);
        Assert.Empty(_ledger.Rows);
    }

    // EC-12: an undeclared cross-cabinet scope reads zero rows with no error, which would report every account in
    // the deployment as unknown. It throws instead, as every console path does.
    [Fact]
    public async Task An_Undeclared_Tenant_Scope_Throws()
    {
        GivenAnEnrolledAccount();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Handler(scope: new TenantScope(NullLogger<TenantScope>.Instance))
                .Handle(Reset(), CancellationToken.None));
    }

    // ------------------------------------------------------------------ telling the person

    /// <summary>
    /// ⚠️ <b>The affected person is told, and told that the VENDOR did it.</b> The clinic-administrator wording would
    /// send somebody who did not request this to warn an administrator with no record of it and no power over it —
    /// i.e. to the one person who can do nothing. This notice is also the only mechanism by which a
    /// social-engineered reset becomes visible to somebody able to recognise it.
    /// </summary>
    [Fact]
    public async Task The_Person_Is_Told_That_The_Vendor_Did_It()
    {
        var user = GivenAnEnrolledAccount();

        await Handler().Handle(Reset(), CancellationToken.None);

        _notifications.Verify(
            n => n.SecondFactorResetAsync(
                SubscriptionVendorHarness.ClinicId, user.Id, SecondFactorResetBy.Vendor,
                It.IsAny<CancellationToken>()),
            Times.Once);

        _email.Verify(
            e => e.SendAsync(
                TargetEmail,
                SecondFactorResetNotice.EmailSubject,
                It.Is<string>(body => body == SecondFactorResetNotice.EmailBody(SecondFactorResetBy.Vendor)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Both notice channels are best-effort and post-commit: the reset has already happened, and a mail server that
    /// is down must not undo it or hide it. This is the same contract every notification in this codebase has.
    /// </summary>
    [Fact]
    public async Task A_Failing_Notice_Does_Not_Undo_The_Reset()
    {
        var user = GivenAnEnrolledAccount();
        _notifications
            .Setup(n => n.SecondFactorResetAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<SecondFactorResetBy>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("feed down"));
        _email
            .Setup(e => e.SendAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var result = await Handler().Handle(Reset(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(user.IsTotpEnrolled);
        Assert.Single(_ledger.Rows);
    }

    /// <summary>
    /// The vendor's wording and the administrator's differ, and in the place that matters: where an unexpected reset
    /// gets reported. Asserted on the notice itself, so the two sentences cannot converge without this failing —
    /// « prévenez votre administrateur » on a vendor action is advice to tell the one person who cannot help.
    /// </summary>
    [Fact]
    public void The_Vendor_Notice_Does_Not_Send_People_To_Their_Administrator_Alone()
    {
        var vendor = SecondFactorResetNotice.EmailBody(SecondFactorResetBy.Vendor);
        var admin = SecondFactorResetNotice.EmailBody(SecondFactorResetBy.ClinicAdministrator);

        Assert.NotEqual(vendor, admin);
        Assert.Contains("support", vendor, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("support", admin, StringComparison.OrdinalIgnoreCase);
    }
}
