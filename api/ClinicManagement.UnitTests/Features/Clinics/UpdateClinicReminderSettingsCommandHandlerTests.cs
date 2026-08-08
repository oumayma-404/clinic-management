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

    public UpdateClinicReminderSettingsCommandHandlerTests()
    {
        // Post-save effectiveStatus resolution — a permissive default (no channels/creds → not_configured).
        // The per-clinic-override / effectiveStatus scenarios are covered in /test-small-feature.
        _provider.Setup(p => p.ResolveAsync(It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResolvedReminderSettings { EnabledChannels = Array.Empty<NotificationType>() });
    }

    private UpdateClinicReminderSettingsCommandHandler Handler() =>
        new(_settings.Object, _users.Object, _context.Object, _protector.Object, _provider.Object,
            _endpointPolicy.Object, _uow.Object);

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
}
