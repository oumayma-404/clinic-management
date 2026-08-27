using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The admin-only, Cloud-only WhatsApp Embedded-Signup connect handler (spec AC-2, AC-3, AC-7): the Graph
/// steps (exchange → subscribe → register) run first and only then are the encrypted token + WABA/phone ids
/// persisted. On any step failure NOTHING is stored and a distinct French message is returned (atomic).
/// </summary>
public class ConnectClinicWhatsAppCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicReminderSettingsRepository> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IReminderSecretProtector> _protector = new();
    private readonly Mock<IWhatsAppOnboardingService> _onboarding = new();
    private readonly Mock<IWhatsAppTemplateService> _templates = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    /// <summary>
    /// Part 4 § 33's template submission. A default mock answers <c>SellsVendorMessaging = false</c>, so these
    /// scenarios stay byte-identical — the submission does not run on a deployment that does not sell vendor
    /// messaging (EC-16), and its own cases live in <c>WhatsAppTemplateSubmissionTests</c>.
    /// </summary>
    private readonly Mock<IVendorMessagingAvailability> _vendorMessaging = new();

    private ConnectClinicWhatsAppCommandHandler Handler() =>
        new(_settings.Object, _users.Object, _context.Object, _protector.Object, _onboarding.Object,
            _templates.Object, _vendorMessaging.Object, _uow.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void CallerIs(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private static ConnectClinicWhatsAppCommand Command() => new()
    {
        Request = new ConnectWhatsAppRequest { Code = "the-code", WabaId = "WABA-1", PhoneNumberId = "PN-99" },
    };

    private void VerifyNothingPersisted()
    {
        _settings.Verify(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _settings.Verify(r => r.UpdateAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _protector.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
    }

    // [AC-6] A non-admin cannot connect; no Graph calls, nothing persisted.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        CallerIs(Local("doctor"));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        _onboarding.Verify(o => o.ExchangeCodeForTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingPersisted();
    }

    // [AC-2, AC-7] Happy path: exchange → subscribe → register, then persist encrypted token + Connected state.
    [Fact]
    public async Task Handle_Should_Provision_Then_Store_Encrypted_Connection()
    {
        CallerIs(Local("admin"));
        _onboarding.Setup(o => o.ExchangeCodeForTokenAsync("the-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync("biz-token");
        _protector.Setup(p => p.Protect("biz-token")).Returns("enc-token");
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);
        ClinicReminderSettings? added = null;
        _settings.Setup(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicReminderSettings, CancellationToken>((s, _) => added = s)
            .Returns(Task.CompletedTask);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal("WABA-1", added!.WhatsAppBusinessAccountId);
        Assert.Equal("PN-99", added.WhatsAppPhoneNumberId);
        Assert.Equal("enc-token", added.WhatsAppAccessTokenEncrypted);
        Assert.Equal(WhatsAppConnectionStatus.Connected, added.WhatsAppConnectionStatus);
        Assert.True(added.WhatsAppEnabled);
        // Returned DTO is secret-masked with the connection metadata.
        Assert.Equal("Connected", result.Value!.WhatsAppConnectionStatus);
        Assert.True(result.Value.WhatsAppAccessTokenConfigured);
        Assert.NotNull(result.Value.WhatsAppConnectedAt);

        _onboarding.Verify(o => o.SubscribeAppAsync("WABA-1", "biz-token", It.IsAny<CancellationToken>()), Times.Once);
        _onboarding.Verify(o => o.RegisterPhoneAsync("PN-99", "biz-token", It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-3] Code→token exchange fails → nothing persisted, later steps not attempted, specific French message.
    [Fact]
    public async Task Handle_Should_Be_Atomic_When_Exchange_Fails()
    {
        CallerIs(Local("admin"));
        _onboarding.Setup(o => o.ExchangeCodeForTokenAsync("the-code", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppOnboardingException(WhatsAppOnboardingError.CodeExchangeFailed, "boom"));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Échec de la connexion à Meta, réessayez.", result.Error);
        _onboarding.Verify(o => o.SubscribeAppAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _onboarding.Verify(o => o.RegisterPhoneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingPersisted();
    }

    // [AC-3] Subscribe fails (WABA ineligible) → nothing persisted, register not attempted, specific French message.
    [Fact]
    public async Task Handle_Should_Be_Atomic_When_Subscribe_Fails()
    {
        CallerIs(Local("admin"));
        _onboarding.Setup(o => o.ExchangeCodeForTokenAsync("the-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync("biz-token");
        _onboarding.Setup(o => o.SubscribeAppAsync("WABA-1", "biz-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppOnboardingException(WhatsAppOnboardingError.WabaNotEligible, "boom"));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Compte WhatsApp Business non éligible : la vérification de l'entreprise est requise.", result.Error);
        _onboarding.Verify(o => o.RegisterPhoneAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNothingPersisted();
    }

    // [AC-3] Register fails (number already registered) → nothing persisted, specific French message.
    [Fact]
    public async Task Handle_Should_Be_Atomic_When_Register_Fails()
    {
        CallerIs(Local("admin"));
        _onboarding.Setup(o => o.ExchangeCodeForTokenAsync("the-code", It.IsAny<CancellationToken>()))
            .ReturnsAsync("biz-token");
        _onboarding.Setup(o => o.RegisterPhoneAsync("PN-99", "biz-token", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WhatsAppOnboardingException(WhatsAppOnboardingError.NumberAlreadyRegistered, "boom"));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Ce numéro WhatsApp est déjà enregistré ailleurs ou nécessite une migration.", result.Error);
        VerifyNothingPersisted();
    }
}
