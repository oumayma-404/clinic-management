using System;
using System.Threading;
using System.Threading.Tasks;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Auth;
using ClinicManagement.Application.Features.Auth.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Auth;

/// <summary>
/// Signing out ends the session on the <b>server</b>.
///
/// <para><b>The gap this closes.</b> There was no revoke endpoint on the API at all —
/// <c>/bff/auth/local-logout</c> cleared the cookies and stopped, and <c>SessionFamily.End</c> was reachable only
/// from replay detection inside <c>RefreshTokenCommand</c>. A refresh credential captured before sign-out stayed
/// valid for its full <b>12 hours</b> and kept rotating itself, so « Se déconnecter » on a shared reception PC
/// revoked nothing.</para>
///
/// <para>⚠️ Most of this class is about what must <b>not</b> fail. Sign-out runs while the session is already
/// being torn down, so every odd input — no credential, an unknown one, one already ended — has to answer
/// success. A refusal there puts a French error toast on a screen that has just signed the user out.</para>
/// </summary>
public class EndSessionCommandTests
{
    private const string Credential = "a-refresh-credential";

    private readonly Mock<ISessionFamilyRepository> _families = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private EndSessionCommandHandler Handler() => new(
        _families.Object, _uow.Object, NullLogger<EndSessionCommandHandler>.Instance);

    private static SessionFamily ALiveFamily() => new(
        "user-1",
        SessionCredential.Hash(Credential),
        DateTime.UtcNow.AddHours(12));

    [Fact]
    public async Task Signing_out_ends_the_family_that_credential_belongs_to()
    {
        var family = ALiveFamily();
        _families
            .Setup(r => r.GetByCredentialAsync(SessionCredential.Hash(Credential), It.IsAny<CancellationToken>()))
            .ReturnsAsync(family);

        var result = await Handler().Handle(
            new EndSessionCommand { RefreshToken = Credential }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(family.IsLive);
        Assert.Equal(EndSessionCommandHandler.Reason, family.EndedReason);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ⚠️ ONE family, never the account. A family is one device's chain, so signing out at the reception desk must
    // not sign the dentist's tablet out mid-consultation. TokenVersion stays the account-wide lever and is
    // deliberately untouched here.
    [Fact]
    public async Task Signing_out_does_not_touch_any_other_device()
    {
        var family = ALiveFamily();
        _families
            .Setup(r => r.GetByCredentialAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(family);

        await Handler().Handle(new EndSessionCommand { RefreshToken = Credential }, CancellationToken.None);

        // The only read is the one credential's own family — nothing enumerates the account's sessions.
        _families.Verify(
            r => r.GetLiveForUserAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task An_unknown_credential_is_a_success_and_writes_nothing()
    {
        _families
            .Setup(r => r.GetByCredentialAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SessionFamily?)null);

        var result = await Handler().Handle(
            new EndSessionCommand { RefreshToken = "never-issued" }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // `SessionFamily.End` throws on a family already ended, and a double-submitted sign-out is ordinary rather
    // than exceptional — the handler checks `IsLive` rather than assuming it.
    [Fact]
    public async Task Signing_out_twice_is_not_an_error()
    {
        var family = ALiveFamily();
        family.End("déjà terminée");
        _families
            .Setup(r => r.GetByCredentialAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(family);

        var result = await Handler().Handle(
            new EndSessionCommand { RefreshToken = Credential }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("déjà terminée", family.EndedReason);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_browser_whose_cookie_has_already_gone_still_signs_out_cleanly(string credential)
    {
        var result = await Handler().Handle(
            new EndSessionCommand { RefreshToken = credential }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        _families.Verify(
            r => r.GetByCredentialAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A server-side failure must not strand the user on a screen that has already signed them out. The exposure
    // this leaves is the one that existed before the endpoint was written, not a new one.
    [Fact]
    public async Task A_failure_while_revoking_still_lets_the_user_sign_out()
    {
        _families
            .Setup(r => r.GetByCredentialAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("la base est indisponible"));

        var result = await Handler().Handle(
            new EndSessionCommand { RefreshToken = Credential }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }
}
