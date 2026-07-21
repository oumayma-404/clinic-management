using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The admin-only, Cloud-only WhatsApp disconnect handler (spec AC-5): clears the stored connection + disables
/// the channel; the Meta app-unsubscribe is best-effort (a failure there still disconnects locally); and it is
/// idempotent when the clinic is not connected.
/// </summary>
public class DisconnectClinicWhatsAppCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicReminderSettingsRepository> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IReminderSecretProtector> _protector = new();
    private readonly Mock<IWhatsAppOnboardingService> _onboarding = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private DisconnectClinicWhatsAppCommandHandler Handler() =>
        new(_settings.Object, _users.Object, _context.Object, _protector.Object, _onboarding.Object, _uow.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void CallerIs(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private static ClinicReminderSettings Connected()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyWhatsAppConnection("WABA-1", "PN-99");
        settings.SetWhatsAppAccessTokenEncrypted("enc-token");
        return settings;
    }

    // [AC-6] A non-admin cannot disconnect.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        CallerIs(Local("secretary"));

        var result = await Handler().Handle(new DisconnectClinicWhatsAppCommand(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-5] A connected clinic is cleared to NotConnected; unsubscribe is attempted with the decrypted token.
    [Fact]
    public async Task Handle_Should_Clear_Connection_And_Unsubscribe()
    {
        CallerIs(Local("admin"));
        var settings = Connected();
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _protector.Setup(p => p.Unprotect("enc-token")).Returns("biz-token");

        var result = await Handler().Handle(new DisconnectClinicWhatsAppCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppConnectionStatus.NotConnected, settings.WhatsAppConnectionStatus);
        Assert.False(settings.WhatsAppEnabled);
        Assert.Null(settings.WhatsAppBusinessAccountId);
        Assert.Null(settings.WhatsAppAccessTokenEncrypted);
        Assert.Equal("NotConnected", result.Value!.WhatsAppConnectionStatus);
        _onboarding.Verify(o => o.UnsubscribeAppAsync("WABA-1", "biz-token", It.IsAny<CancellationToken>()), Times.Once);
        _settings.Verify(r => r.UpdateAsync(settings, It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5] A failing Meta unsubscribe is swallowed — the local disconnect still succeeds and is persisted.
    [Fact]
    public async Task Handle_Should_Disconnect_Even_When_Unsubscribe_Throws()
    {
        CallerIs(Local("admin"));
        var settings = Connected();
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        _protector.Setup(p => p.Unprotect("enc-token")).Returns("biz-token");
        _onboarding.Setup(o => o.UnsubscribeAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppOnboardingException(WhatsAppOnboardingError.Unknown, "meta down"));

        var result = await Handler().Handle(new DisconnectClinicWhatsAppCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppConnectionStatus.NotConnected, settings.WhatsAppConnectionStatus);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-5] Not connected (no row) → no-op success; no unsubscribe, no write.
    [Fact]
    public async Task Handle_Should_Be_NoOp_When_Not_Connected()
    {
        CallerIs(Local("admin"));
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);

        var result = await Handler().Handle(new DisconnectClinicWhatsAppCommand(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("NotConnected", result.Value!.WhatsAppConnectionStatus);
        _onboarding.Verify(o => o.UnsubscribeAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _settings.Verify(r => r.UpdateAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
