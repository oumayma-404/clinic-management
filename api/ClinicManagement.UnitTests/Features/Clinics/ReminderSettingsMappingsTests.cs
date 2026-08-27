using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Clinics;

/// <summary>
/// The secret-masked <see cref="ClinicReminderSettings"/> → <see cref="ReminderSettingsDto"/> mapping, focused
/// on the WhatsApp connection metadata: the status maps to the enum NAME (stable string for the FE badge), the
/// token is never surfaced, and a clinic with no settings row maps to a NotConnected default.
/// </summary>
public class ReminderSettingsMappingsTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void ToDto_Null_Settings_Maps_To_NotConnected_Default()
    {
        ClinicReminderSettings? settings = null;

        var dto = settings.ToDto(vendorManagedWhatsApp: false);

        Assert.Equal("NotConnected", dto.WhatsAppConnectionStatus);
        Assert.Null(dto.WhatsAppBusinessAccountId);
        Assert.Null(dto.WhatsAppConnectedAt);
        Assert.Null(dto.WhatsAppLastError);
        Assert.False(dto.WhatsAppAccessTokenConfigured);
    }

    [Fact]
    public void ToDto_Connected_Settings_Maps_Status_Name_And_Metadata_Without_Token()
    {
        var settings = new ClinicReminderSettings(ClinicId);
        settings.ApplyWhatsAppConnection("WABA-1", "PN-99");
        settings.SetWhatsAppAccessTokenEncrypted("enc-token");

        var dto = settings.ToDto(vendorManagedWhatsApp: false);

        Assert.Equal("Connected", dto.WhatsAppConnectionStatus);
        Assert.Equal("WABA-1", dto.WhatsAppBusinessAccountId);
        Assert.Equal("PN-99", dto.WhatsAppPhoneNumberId);
        Assert.NotNull(dto.WhatsAppConnectedAt);
        Assert.True(dto.WhatsAppAccessTokenConfigured); // only a flag — the token itself is never returned
    }

    // [AC-1.7] The flag is the DTO's own, not the entity's: it is a fact about the DEPLOYMENT, so the same stored row
    // reads both ways and every producer has to state which it is.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToDto_Reports_Whether_WhatsApp_Is_Vendor_Managed(bool vendorManaged)
    {
        var settings = new ClinicReminderSettings(ClinicId);

        Assert.Equal(vendorManaged, settings.ToDto(vendorManaged).WhatsAppVendorManaged);
    }
}
