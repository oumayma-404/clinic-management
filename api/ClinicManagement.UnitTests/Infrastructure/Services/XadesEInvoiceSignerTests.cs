using ClinicManagement.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// XAdES/XMLDSig signer guards (FR-2, edge: certificate missing/invalid). The positive signing path needs
/// a real qualified PFX (integration/manual); these pin the fail-fast behavior — a missing certificate must
/// surface a clear operator error and never corrupt state, and empty input is rejected.
/// </summary>
public class XadesEInvoiceSignerTests
{
    private static XadesEInvoiceSigner SignerWithCertPath(string certPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ttn:CertPath"] = certPath })
            .Build();
        return new XadesEInvoiceSigner(configuration, NullLogger<XadesEInvoiceSigner>.Instance);
    }

    // [FR-2][edge] A missing certificate fails fast with a clear operator message.
    [Fact]
    public void Sign_Without_Certificate_Throws_InvalidOperation()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"teif-missing-{Guid.NewGuid():N}.pfx");
        var signer = SignerWithCertPath(missingPath);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            signer.Sign("<TEIF version=\"1.8.8\"><InvoiceBody/></TEIF>"));

        Assert.Contains("Certificat", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // [FR-2] Empty TEIF input is rejected before any certificate work.
    [Fact]
    public void Sign_Empty_Xml_Throws_Argument()
    {
        var signer = SignerWithCertPath(Path.Combine(Path.GetTempPath(), "unused.pfx"));

        Assert.Throws<ArgumentException>(() => signer.Sign("   "));
    }
}
