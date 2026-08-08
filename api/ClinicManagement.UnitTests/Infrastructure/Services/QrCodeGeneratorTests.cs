using ClinicManagement.Infrastructure.Services;
using Xunit;

namespace ClinicManagement.UnitTests.Infrastructure.Services;

/// <summary>
/// QR « cachet électronique visible » rendering (FR-7): the payload encodes to a real PNG image (used on
/// the validated note-d'honoraires PDF), and an empty payload is rejected.
/// </summary>
public class QrCodeGeneratorTests
{
    // The 8-byte PNG file signature.
    private static readonly byte[] PngMagic = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    // A payload renders to a non-empty PNG. Its live caller is the LAN trust page's QR (TrustController).
    [Fact]
    public void GeneratePng_Returns_Png_Bytes()
    {
        var png = new QrCodeGenerator().GeneratePng("https://192.168.1.10:5001/trust");

        Assert.NotEmpty(png);
        Assert.True(png.Length > PngMagic.Length);
        Assert.Equal(PngMagic, png.Take(PngMagic.Length).ToArray());
    }

    // [FR-7] An empty payload is rejected.
    [Fact]
    public void GeneratePng_Empty_Payload_Throws()
    {
        Assert.Throws<ArgumentException>(() => new QrCodeGenerator().GeneratePng("  "));
    }
}
