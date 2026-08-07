using ClinicManagement.Domain.Entities;
using Xunit;

namespace ClinicManagement.UnitTests.Domain;

/// <summary>
/// Per-clinic TTN « El Fatoora » settings (FR-8): the enable toggle + target environment, with the
/// environment normalized to a safe known value (defaults to Sandbox, never trusts arbitrary input).
/// </summary>
public class ClinicEInvoiceSettingsTests
{
    private static Clinic NewClinic() => new(Guid.NewGuid(), "Cabinet Test");

    // [FR-8] A new clinic defaults to e-invoicing off, sandbox environment.
    [Fact]
    public void New_Clinic_Defaults_To_Disabled_Sandbox()
    {
        var clinic = NewClinic();

        Assert.False(clinic.TtnEInvoicingEnabled);
        Assert.Equal(Clinic.TtnEnvironmentSandbox, clinic.TtnEnvironment);
    }

    // [FR-8] Enabling with the Production environment is stored as-is.
    [Fact]
    public void SetElFatooraSettings_Enables_Production()
    {
        var clinic = NewClinic();

        clinic.SetElFatooraSettings(enabled: true, environment: "Production");

        Assert.True(clinic.TtnEInvoicingEnabled);
        Assert.Equal(Clinic.TtnEnvironmentProduction, clinic.TtnEnvironment);
    }

    // [FR-8] Production is matched case-insensitively.
    [Fact]
    public void SetElFatooraSettings_Normalizes_Production_Case_Insensitively()
    {
        var clinic = NewClinic();

        clinic.SetElFatooraSettings(enabled: true, environment: "production");

        Assert.Equal(Clinic.TtnEnvironmentProduction, clinic.TtnEnvironment);
    }

    // [FR-8] Any unrecognized / null environment falls back to the safe sandbox — never sent to production by accident.
    [Theory]
    [InlineData("")]
    [InlineData("prod")]
    [InlineData("garbage")]
    [InlineData(null)]
    public void SetElFatooraSettings_Falls_Back_To_Sandbox_For_Unknown_Environment(string? environment)
    {
        var clinic = NewClinic();

        clinic.SetElFatooraSettings(enabled: true, environment: environment);

        Assert.Equal(Clinic.TtnEnvironmentSandbox, clinic.TtnEnvironment);
    }

    // ------------------------------------------------------------------ US-4: the clinic's own TTN identity

    // [US-4] A new clinic has no identity of its own — different from « e-invoicing is off », and the state the
    // resolver reads as « fall back, or refuse ».
    [Fact]
    public void New_Clinic_Has_No_Ttn_Identity()
    {
        var clinic = NewClinic();

        Assert.Null(clinic.TtnUsername);
        Assert.Null(clinic.TtnApiSecretEncrypted);
        Assert.Null(clinic.TtnCertificateKey);
        Assert.Null(clinic.TtnCertificatePasswordEncrypted);
    }

    // [US-4] The whole identity round-trips, and only ciphertext is ever handed to this layer.
    [Fact]
    public void SetTtnIdentity_Stores_All_Four_Fields()
    {
        var clinic = NewClinic();

        clinic.SetTtnIdentity("cabinet-a", "cipher-secret", "clinics/a/teif.pfx", "cipher-password");

        Assert.Equal("cabinet-a", clinic.TtnUsername);
        Assert.Equal("cipher-secret", clinic.TtnApiSecretEncrypted);
        Assert.Equal("clinics/a/teif.pfx", clinic.TtnCertificateKey);
        Assert.Equal("cipher-password", clinic.TtnCertificatePasswordEncrypted);
    }

    // [US-4] Blank clears, so a mistyped value can be undone — « cleared » and « never set » are one state.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SetTtnIdentity_Treats_Blank_As_Cleared(string? blank)
    {
        var clinic = NewClinic();
        clinic.SetTtnIdentity("cabinet-a", "cipher-secret", "clinics/a/teif.pfx", "cipher-password");

        clinic.SetTtnIdentity(blank, blank, blank, blank);

        Assert.Null(clinic.TtnUsername);
        Assert.Null(clinic.TtnApiSecretEncrypted);
        Assert.Null(clinic.TtnCertificateKey);
        Assert.Null(clinic.TtnCertificatePasswordEncrypted);
    }

    // [US-4] Half an identity is refused: a secret cannot authenticate without its username, and a certificate
    // password with no certificate is ciphertext nothing can open. verify-schema's `ttn-identity-is-complete`
    // catches the rows that arrive around this method — until an admin surface exists, they all do.
    [Fact]
    public void SetTtnIdentity_Refuses_A_Secret_Without_Its_Username()
    {
        var clinic = NewClinic();

        Assert.Throws<ArgumentException>(() => clinic.SetTtnIdentity(null, "cipher-secret", null, null));
    }

    [Fact]
    public void SetTtnIdentity_Refuses_A_Certificate_Password_Without_Its_Certificate()
    {
        var clinic = NewClinic();

        Assert.Throws<ArgumentException>(() => clinic.SetTtnIdentity(null, null, null, "cipher-password"));
    }

    // [US-4] The two halves are provisioned separately, so each alone is legal — a clinic mid-provisioning has
    // to be storable, and refusing that would push the operator into filling a field with a placeholder.
    [Fact]
    public void SetTtnIdentity_Accepts_Either_Half_Alone()
    {
        var certificateOnly = NewClinic();
        certificateOnly.SetTtnIdentity(null, null, "clinics/a/teif.pfx", "cipher-password");
        Assert.Equal("clinics/a/teif.pfx", certificateOnly.TtnCertificateKey);
        Assert.Null(certificateOnly.TtnUsername);

        var accountOnly = NewClinic();
        accountOnly.SetTtnIdentity("cabinet-a", "cipher-secret", null, null);
        Assert.Equal("cabinet-a", accountOnly.TtnUsername);
        Assert.Null(accountOnly.TtnCertificateKey);
    }
}
