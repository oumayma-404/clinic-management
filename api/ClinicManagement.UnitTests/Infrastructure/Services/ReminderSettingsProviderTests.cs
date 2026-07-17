using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using ClinicManagement.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The effective-settings resolver (spec AC-3..AC-6): per-clinic overrides where set, else the per-install
/// <c>Reminders</c> config. Channel toggles are tri-state (null = inherit); identity/secret fields fall back
/// to install; a per-clinic secret whose ciphertext can't be decrypted is treated as not configured for that
/// channel (no fallback to the install secret).
/// </summary>
public class ReminderSettingsProviderTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly Mock<IClinicReminderSettingsRepository> _repository = new();
    private readonly Mock<IReminderSecretProtector> _protector = new();

    // A per-install config with both channels enabled and full SMS + WhatsApp credentials.
    private static IConfiguration InstallConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reminders:Channels:0"] = "Sms",
            ["Reminders:Channels:1"] = "WhatsApp",
            ["Reminders:Sms:ApiUrl"] = "https://install-sms/send",
            ["Reminders:Sms:SenderId"] = "InstallSms",
            ["Reminders:Sms:ApiKey"] = "install-sms-key",
            ["Reminders:WhatsApp:ApiUrl"] = "https://install-wa",
            ["Reminders:WhatsApp:PhoneNumberId"] = "install-pn",
            ["Reminders:WhatsApp:TemplateName"] = "install_tpl",
            ["Reminders:WhatsApp:TemplateLanguage"] = "en",
            ["Reminders:WhatsApp:AccessToken"] = "install-wa-token",
        }).Build();

    private ReminderSettingsProvider Provider(IConfiguration configuration) =>
        new(_repository.Object, _protector.Object, configuration, NullLogger<ReminderSettingsProvider>.Instance);

    private void HasClinicSettings(ClinicReminderSettings settings) =>
        _repository.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(settings);

    // [AC-6] A clinic with no settings row resolves entirely to the per-install config (channels + identity + secrets).
    [Fact]
    public async Task ResolveAsync_Falls_Back_To_Install_When_No_Clinic_Settings()
    {
        _repository.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ClinicReminderSettings?)null);

        var result = await Provider(InstallConfig()).ResolveAsync(ClinicId);

        Assert.Equal(new[] { NotificationType.SMS, NotificationType.WhatsApp }, result.EnabledChannels);
        Assert.Equal("InstallSms", result.SmsSenderId);
        Assert.Equal("install-sms-key", result.SmsApiKey);
        Assert.Equal("install-pn", result.WhatsAppPhoneNumberId);
        Assert.Equal("install-wa-token", result.WhatsAppAccessToken);
        _protector.Verify(p => p.Unprotect(It.IsAny<string>()), Times.Never); // nothing to decrypt
    }

    // [AC-5] A null clinic id (legacy/global row) resolves purely to the per-install config.
    [Fact]
    public async Task ResolveAsync_Null_ClinicId_Uses_Install_Config_Without_Repo_Lookup()
    {
        var result = await Provider(InstallConfig()).ResolveAsync(null);

        Assert.Equal(new[] { NotificationType.SMS, NotificationType.WhatsApp }, result.EnabledChannels);
        Assert.Equal("install-sms-key", result.SmsApiKey);
        _repository.Verify(r => r.GetByClinicIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-4] A clinic's explicit channel toggle wins; an explicit false disables a channel the install enables.
    [Fact]
    public async Task ResolveEnabledChannels_Clinic_Toggle_Overrides_Install()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyNonSecretSettings(smsEnabled: true, whatsAppEnabled: false, null, null, null, null);
        HasClinicSettings(settings);

        var channels = await Provider(InstallConfig()).ResolveEnabledChannelsAsync(ClinicId);

        Assert.Equal(new[] { NotificationType.SMS }, channels); // WhatsApp explicitly off despite install enabling it
    }

    // [AC-4] A null toggle inherits the per-install default (here: install has neither channel).
    [Fact]
    public async Task ResolveEnabledChannels_Null_Toggle_Inherits_Install_Default()
    {
        var settings = new ClinicReminderSettings(ClinicId); // both toggles null
        HasClinicSettings(settings);
        var configNoChannels = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

        var channels = await Provider(configNoChannels).ResolveEnabledChannelsAsync(ClinicId);

        Assert.Empty(channels);
    }

    // [AC-5] Per-clinic identity + secret win over install; the clinic secret is decrypted.
    [Fact]
    public async Task ResolveAsync_Uses_Clinic_Identity_And_Decrypts_Clinic_Secret()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyNonSecretSettings(true, null, "ClinicSms", null, null, null);
        settings.SetSmsApiKeyEncrypted("cipher-sms");
        HasClinicSettings(settings);
        _protector.Setup(p => p.Unprotect("cipher-sms")).Returns("clinic-sms-key");

        var result = await Provider(InstallConfig()).ResolveAsync(ClinicId);

        Assert.Equal("ClinicSms", result.SmsSenderId);          // clinic identity wins
        Assert.Equal("clinic-sms-key", result.SmsApiKey);       // clinic secret decrypted
        Assert.Equal("install-wa-token", result.WhatsAppAccessToken); // WhatsApp secret not set → install fallback
    }

    // [AC-3 edge] A clinic secret whose ciphertext can't be decrypted → that channel is treated as not
    // configured (null), and it does NOT fall back to the install secret (the clinic chose its own identity).
    [Fact]
    public async Task ResolveAsync_Decryption_Failure_Yields_Null_Secret_Without_Install_Fallback()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.SetSmsApiKeyEncrypted("corrupt");
        HasClinicSettings(settings);
        _protector.Setup(p => p.Unprotect("corrupt")).Throws(new InvalidOperationException("key rotated"));

        var result = await Provider(InstallConfig()).ResolveAsync(ClinicId);

        Assert.Null(result.SmsApiKey); // NotConfigured, not "install-sms-key"
    }
}
