using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Clinics.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The admin-only GET reminder-settings handler (spec AC-1, AC-3): returns the secret-masked settings
/// (configured flags only, never the secret values); a clinic with no row yet returns an all-inherit DTO;
/// non-admins are rejected.
/// </summary>
public class GetClinicReminderSettingsQueryHandlerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicReminderSettingsRepository> _settings = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<IClinicContext> _context = new();

    private GetClinicReminderSettingsQueryHandler Handler() =>
        new(_settings.Object, _users.Object, _context.Object);

    private static User Local(string role) =>
        User.CreateLocalUser(ClinicId, role, $"{role}@clinic.com", "HASH", $"{role} name");

    private void CallerIs(User user)
    {
        _context.Setup(c => c.GetUserId()).Returns(user.Id);
        _users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
    }

    // [AC-1] A non-admin cannot read reminder settings.
    [Fact]
    public async Task Handle_Should_Reject_Non_Admin()
    {
        CallerIs(Local("secretary"));

        var result = await Handler().Handle(new GetClinicReminderSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // [AC-1] No settings row → an all-inherit DTO (null toggles, both secrets not configured).
    [Fact]
    public async Task Handle_Should_Return_Inherit_Defaults_When_No_Row()
    {
        CallerIs(Local("admin"));
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);

        var result = await Handler().Handle(new GetClinicReminderSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.SmsEnabled);
        Assert.Null(result.Value.WhatsAppEnabled);
        Assert.False(result.Value.SmsApiKeyConfigured);
        Assert.False(result.Value.WhatsAppAccessTokenConfigured);
    }

    // [AC-1, AC-3] The DTO is secret-masked: identity + configured flags are surfaced, secret values are not
    // (the DTO type carries no secret field, so a stored secret shows only as a configured=true flag).
    [Fact]
    public async Task Handle_Should_Return_Masked_Settings_For_Admin()
    {
        CallerIs(Local("admin"));
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyNonSecretSettings(true, false, "MaClinique", "PN123", "tpl", "fr");
        settings.SetSmsApiKeyEncrypted("enc-sms");
        _settings.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var result = await Handler().Handle(new GetClinicReminderSettingsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = result.Value!;
        Assert.True(dto.SmsEnabled);
        Assert.False(dto.WhatsAppEnabled);
        Assert.Equal("MaClinique", dto.SmsSenderId);
        Assert.Equal("PN123", dto.WhatsAppPhoneNumberId);
        Assert.True(dto.SmsApiKeyConfigured);              // secret present → flag only
        Assert.False(dto.WhatsAppAccessTokenConfigured);   // not set
    }
}
