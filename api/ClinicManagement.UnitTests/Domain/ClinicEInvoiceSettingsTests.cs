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
}
