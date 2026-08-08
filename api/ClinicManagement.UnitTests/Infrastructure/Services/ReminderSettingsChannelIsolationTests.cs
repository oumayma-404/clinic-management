using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// The channel-ownership boundary in <see cref="ReminderSettingsProvider"/>
/// (<c>SECURITY_REVIEW_2026-08</c>, finding A).
///
/// <para>
/// Resolution used to coalesce <b>per field</b>: a clinic could supply only the endpoint URL and inherit the
/// <i>install's</i> credential, which the dispatcher then presented to that clinic-chosen endpoint as a bearer
/// token or SMTP AUTH. On a hosted backend — where anyone who signs up is an admin of their own clinic — that is
/// remote theft of an install-wide secret by a stranger.
/// </para>
///
/// <para>
/// The rule these tests pin: <b>claiming any part of a channel means owning all of it.</b> Refusing to send is
/// recoverable; leaking the operator's credential is not.
/// </para>
/// </summary>
public class ReminderSettingsChannelIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private const string InstallSmsKey = "INSTALL-SMS-KEY";
    private const string InstallWhatsAppToken = "INSTALL-WA-TOKEN";
    private const string InstallSmtpPassword = "INSTALL-SMTP-PASSWORD";

    private readonly Mock<IClinicReminderSettingsRepository> _repository = new();
    private readonly Mock<IReminderSecretProtector> _protector = new();

    private ReminderSettingsProvider Provider() =>
        new(_repository.Object, _protector.Object, Configuration(), NullLogger<ReminderSettingsProvider>.Instance);

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Reminders:Sms:ApiUrl"] = "https://install-gateway.example.com/send",
            ["Reminders:Sms:SenderId"] = "INSTALL",
            ["Reminders:Sms:ApiKey"] = InstallSmsKey,
            ["Reminders:WhatsApp:ApiUrl"] = "https://graph.facebook.com/v20.0",
            ["Reminders:WhatsApp:PhoneNumberId"] = "1234567890",
            ["Reminders:WhatsApp:AccessToken"] = InstallWhatsAppToken,
            ["Notification:Smtp:Server"] = "smtp-relay.install.example.com",
            ["Notification:Smtp:Username"] = "install-user",
            ["Notification:Smtp:Password"] = InstallSmtpPassword,
        }).Build();

    private void ClinicHas(ClinicReminderSettings? settings) =>
        _repository
            .Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

    private static ClinicReminderSettings Settings(
        string? smsApiUrl = null, string? whatsAppApiUrl = null, string? smtpHost = null)
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyNonSecretSettings(
            smsEnabled: true, whatsAppEnabled: true,
            smsSenderId: null, whatsAppPhoneNumberId: null,
            whatsAppTemplateName: null, whatsAppTemplateLanguage: null,
            smsApiUrl: smsApiUrl, whatsAppApiUrl: whatsAppApiUrl,
            leadTimeHours: null, messageTemplateBody: null);
        settings.ApplySmtpSettings(
            smtpHost: smtpHost, smtpPort: null, smtpUseTls: null,
            smtpUsername: null, smtpFromAddress: null, smtpFromName: null);
        return settings;
    }

    // ---- The attack these tests exist for -------------------------------------------------------

    [Fact]
    public async Task A_Clinic_Supplying_Only_An_Sms_Url_Does_Not_Inherit_The_Install_Key()
    {
        ClinicHas(Settings(smsApiUrl: "https://attacker.example.com/collect"));

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Equal("https://attacker.example.com/collect", resolved.SmsApiUrl);
        Assert.Null(resolved.SmsApiKey);
        Assert.NotEqual(InstallSmsKey, resolved.SmsApiKey);
    }

    [Fact]
    public async Task A_Clinic_Supplying_Only_A_WhatsApp_Url_Does_Not_Inherit_The_Install_Token()
    {
        ClinicHas(Settings(whatsAppApiUrl: "https://attacker.example.com"));

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Equal("https://attacker.example.com", resolved.WhatsAppApiUrl);
        Assert.Null(resolved.WhatsAppAccessToken);
        Assert.NotEqual(InstallWhatsAppToken, resolved.WhatsAppAccessToken);
    }

    [Fact]
    public async Task A_Clinic_Supplying_Only_An_Smtp_Host_Does_Not_Inherit_The_Install_Password()
    {
        ClinicHas(Settings(smtpHost: "smtp.attacker.example.com"));

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Equal("smtp.attacker.example.com", resolved.SmtpHost);
        Assert.Null(resolved.SmtpPassword);
        Assert.Null(resolved.SmtpUsername);
        Assert.NotEqual(InstallSmtpPassword, resolved.SmtpPassword);
    }

    /// <summary>
    /// The channel is then <b>not configured</b>, so the dispatcher parks the row instead of sending. That is the
    /// intended outcome: a clinic that names an endpoint and no credential has not finished configuring it.
    /// </summary>
    [Fact]
    public async Task A_Half_Configured_Channel_Is_Not_Sendable()
    {
        ClinicHas(Settings(smsApiUrl: "https://attacker.example.com/collect"));

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.False(resolved.SmsConfigured);
    }

    // ---- The behaviour that must survive the fix ------------------------------------------------

    [Fact]
    public async Task A_Clinic_That_Overrides_Nothing_Still_Inherits_The_Install_Settings()
    {
        ClinicHas(null);

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Equal("https://install-gateway.example.com/send", resolved.SmsApiUrl);
        Assert.Equal(InstallSmsKey, resolved.SmsApiKey);
        Assert.Equal(InstallWhatsAppToken, resolved.WhatsAppAccessToken);
        Assert.Equal("smtp-relay.install.example.com", resolved.SmtpHost);
        Assert.Equal(InstallSmtpPassword, resolved.SmtpPassword);
    }

    /// <summary>
    /// Claiming one channel must not disown the others — a clinic with its own SMS gateway still sends email
    /// through the install's relay.
    /// </summary>
    [Fact]
    public async Task Ownership_Is_Per_Channel_Not_Global()
    {
        ClinicHas(Settings(smsApiUrl: "https://clinic-gateway.example.com/send"));

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Null(resolved.SmsApiKey);
        Assert.Equal(InstallSmtpPassword, resolved.SmtpPassword);
        Assert.Equal(InstallWhatsAppToken, resolved.WhatsAppAccessToken);
    }

    [Fact]
    public async Task A_Clinic_Supplying_Its_Own_Url_And_Secret_Uses_Both()
    {
        var settings = Settings(smsApiUrl: "https://clinic-gateway.example.com/send");
        settings.SetSmsApiKeyEncrypted("CIPHERTEXT");
        _protector.Setup(p => p.Unprotect("CIPHERTEXT")).Returns("CLINIC-OWN-KEY");
        ClinicHas(settings);

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Equal("https://clinic-gateway.example.com/send", resolved.SmsApiUrl);
        Assert.Equal("CLINIC-OWN-KEY", resolved.SmsApiKey);
    }

    /// <summary>
    /// A ciphertext that no longer decrypts (rotated or unavailable key) must park the channel, never fall back
    /// to the install secret — the clinic chose its own identity and the install's is not a substitute for it.
    /// </summary>
    [Fact]
    public async Task An_Undecryptable_Clinic_Secret_Does_Not_Fall_Back_To_The_Install()
    {
        var settings = Settings(smsApiUrl: "https://clinic-gateway.example.com/send");
        settings.SetSmsApiKeyEncrypted("BROKEN");
        _protector.Setup(p => p.Unprotect("BROKEN")).Throws(new InvalidOperationException("key rotated"));
        ClinicHas(settings);

        var resolved = await Provider().ResolveAsync(ClinicId);

        Assert.Null(resolved.SmsApiKey);
        Assert.False(resolved.SmsConfigured);
    }
}
