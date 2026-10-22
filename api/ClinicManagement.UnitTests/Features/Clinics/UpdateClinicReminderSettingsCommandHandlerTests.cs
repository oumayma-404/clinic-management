using ClinicManagement.Application.Features.Messaging;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Clinics.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The admin-only PUT reminder-settings handler (spec AC-2, AC-3): non-admins are rejected; secrets are
/// write-only (blank ⇒ stored value unchanged, a value ⇒ encrypted &amp; replaced); a missing row is created.
/// </summary>
public class UpdateClinicReminderSettingsCommandHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicReminderSettingsRepository> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();
    private readonly Mock<IReminderSecretProtector> _protector = new();
    private readonly Mock<IReminderSettingsProvider> _provider = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    // Hosted default: a clinic may NOT aim an endpoint at a private address. Tests that need the LAN
    // behaviour set this up explicitly.
    private readonly Mock<IOutboundEndpointPolicy> _endpointPolicy = new();

    /// <summary>
    /// AC-1.7. A default mock answers <c>SellsVendorMessaging = false</c>, so every case below keeps the manual
    /// credential path it was written against; the refusal has its own cases in <c>VendorManagedWhatsAppTests</c>.
    /// </summary>
    private readonly Mock<IVendorMessagingAvailability> _vendorMessaging = new();

    public UpdateClinicReminderSettingsCommandHandlerTests()
    {
        // Post-save effectiveStatus resolution — a permissive default (no channels/creds → not_configured).
        // The per-clinic-override / effectiveStatus scenarios are covered in /test-small-feature.
        _provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = Array.Empty<NotificationType>() });
    }

    private UpdateClinicReminderSettingsCommandHandler Handler() =>
        new(_settings.Object, _users.Object, Mock.Of<IClinicRepository>(), _context.Object, _protector.Object, _provider.Object,
            _endpointPolicy.Object, _vendorMessaging.Object, _uow.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void CallerIs(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    private static UpdateClinicReminderSettingsCommand Command(UpdateReminderSettingsRequest request) =>
        new() { Settings = request };

    // [AC-2] A non-admin cannot update reminder settings; nothing is persisted.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        CallerIs(Local("doctor"));

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest()), CancellationToken.None);

        Assert.True(result.IsFailure);
        _settings.Verify(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _settings.Verify(r => r.UpdateAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-2] No existing row → a new one is created; a provided secret is encrypted and stored.
    [Fact]
    public async Task Handle_Should_Create_Row_And_Encrypt_Provided_Secret()
    {
        CallerIs(Local("admin"));
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);
        _protector.Setup(p => p.Protect("plain-key")).Returns("enc-key");
        ClinicReminderSettings? added = null;
        _settings.Setup(r => r.AddAsync(It.IsAny<ClinicReminderSettings>(), It.IsAny<CancellationToken>()))
            .Callback<ClinicReminderSettings, CancellationToken>((s, _) => added = s)
            .Returns(Task.CompletedTask);

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            SmsEnabled = true,
            SmsSenderId = "MaClinique",
            SmsApiKey = "plain-key",
        }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(ClinicId, added!.Id);
        Assert.Equal("enc-key", added.SmsApiKeyEncrypted);
        Assert.True(result.Value!.SmsApiKeyConfigured);
        _protector.Verify(p => p.Protect("plain-key"), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // [AC-2] A blank/omitted secret leaves the stored ciphertext unchanged (write-only semantics).
    [Fact]
    public async Task Handle_Should_Keep_Existing_Secret_When_Blank()
    {
        CallerIs(Local("admin"));
        var existing = new ClinicReminderSettings(ClinicId);
        existing.SetSmsApiKeyEncrypted("old-cipher");
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            SmsEnabled = false,
            SmsApiKey = "   ", // blank ⇒ unchanged
        }), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("old-cipher", existing.SmsApiKeyEncrypted); // untouched
        Assert.True(result.Value!.SmsApiKeyConfigured);
        _protector.Verify(p => p.Protect(It.IsAny<string>()), Times.Never);
        _settings.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---- sms-rappels-tunisie-sms ---------------------------------------------------------------------

    /// <summary>Where the vendor sells messaging the SMS gateway is its own account: a typed one is refused by code.</summary>
    [Theory]
    [InlineData("https://sms.example/send", null, null)]
    [InlineData(null, "MaClinique", null)]
    [InlineData(null, null, "a-key")]
    public async Task A_Typed_Sms_Gateway_Is_Refused_Where_The_Vendor_Provides_It(string? url, string? sender, string? key)
    {
        CallerIs(Local("admin"));
        _vendorMessaging.SetupGet(v => v.SellsVendorMessaging).Returns(true);

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            SmsApiUrl = url,
            SmsSenderId = sender,
            SmsApiKey = key,
        }), CancellationToken.None);

        Assert.Equal(MessagingRefusals.ManualSmsCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Sms_Can_Still_Be_Switched_On_Where_The_Vendor_Provides_It()
    {
        CallerIs(Local("admin"));
        _vendorMessaging.SetupGet(v => v.SellsVendorMessaging).Returns(true);
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            SmsEnabled = true,
            LeadTimeHours = [24],
            MessageTemplateBody = "Rappel le {date}.\nMerci de venir 10 min en avance.",
        }), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error);
        Assert.True(result.Value!.SmsVendorManaged);
        Assert.Equal(157, result.Value.SmsMaxLength);
    }

    [Fact]
    public async Task A_Wording_Over_One_Sms_Is_Refused_Before_Anything_Is_Saved()
    {
        CallerIs(Local("admin"));

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            MessageTemplateBody = "Rappel le {date}. " + new string('x', 160),
        }), CancellationToken.None);

        Assert.Equal(ReminderTemplateRules.TooLongCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Send_Time_Outside_The_Choices_Is_Refused()
    {
        CallerIs(Local("admin"));

        var result = await Handler().Handle(Command(new UpdateReminderSettingsRequest
        {
            LeadTimeHours = [24, 12, 6],
        }), CancellationToken.None);

        Assert.Equal(ReminderTemplateRules.LeadTimeCode, result.Code);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
