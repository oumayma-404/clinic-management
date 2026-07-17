using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// The per-clinic reminder settings entity: the Id is the owning clinic id (shared PK), non-secret setters
/// normalize blank strings to null (= inherit), and secrets are stored write-only as opaque ciphertext.
/// </summary>
public class ClinicReminderSettingsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void Ctor_Sets_Id_To_The_Clinic_Id()
    {
        var settings = new ClinicReminderSettings(ClinicId);

        Assert.Equal(ClinicId, settings.Id);
        Assert.Null(settings.SmsEnabled);
        Assert.Null(settings.WhatsAppEnabled);
        Assert.Null(settings.SmsApiKeyEncrypted);
        Assert.Null(settings.WhatsAppAccessTokenEncrypted);
    }

    [Fact]
    public void ApplyNonSecretSettings_Sets_Toggles_And_Trims_Identity()
    {
        var settings = new ClinicReminderSettings(ClinicId);

        settings.ApplyNonSecretSettings(
            smsEnabled: true,
            whatsAppEnabled: false,
            smsSenderId: "  MaClinique  ",
            whatsAppPhoneNumberId: "PN123",
            whatsAppTemplateName: "appointment_reminder",
            whatsAppTemplateLanguage: "fr");

        Assert.True(settings.SmsEnabled);
        Assert.False(settings.WhatsAppEnabled);
        Assert.Equal("MaClinique", settings.SmsSenderId); // trimmed
        Assert.Equal("PN123", settings.WhatsAppPhoneNumberId);
        Assert.Equal("appointment_reminder", settings.WhatsAppTemplateName);
        Assert.Equal("fr", settings.WhatsAppTemplateLanguage);
        Assert.NotNull(settings.UpdatedAt);
    }

    [Fact]
    public void ApplyNonSecretSettings_Normalizes_Blank_Identity_To_Null()
    {
        var settings = new ClinicReminderSettings(ClinicId);

        settings.ApplyNonSecretSettings(null, null, "   ", "", null, "  ");

        Assert.Null(settings.SmsSenderId);
        Assert.Null(settings.WhatsAppPhoneNumberId);
        Assert.Null(settings.WhatsAppTemplateName);
        Assert.Null(settings.WhatsAppTemplateLanguage);
    }

    [Fact]
    public void Secret_Setters_Store_The_Supplied_Ciphertext()
    {
        var settings = new ClinicReminderSettings(ClinicId);

        settings.SetSmsApiKeyEncrypted("enc-sms");
        settings.SetWhatsAppAccessTokenEncrypted("enc-wa");

        Assert.Equal("enc-sms", settings.SmsApiKeyEncrypted);
        Assert.Equal("enc-wa", settings.WhatsAppAccessTokenEncrypted);
    }
}
