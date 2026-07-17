using ClinicManagement.Application.Common.Interfaces;
using QRCoder;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Renders a QR « cachet électronique visible » payload to a PNG (FR-7) using QRCoder's
/// <see cref="PngByteQRCode"/> — a pure managed encoder (no System.Drawing / native dependency), safe to
/// run headless on a Windows service or Linux container.
/// </summary>
public class QrCodeGenerator : IQrCodeGenerator
{
    public byte[] GeneratePng(string payload, int pixelsPerModule = 10)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Le contenu du QR est requis.", nameof(payload));
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(data);
        return pngQr.GetGraphic(pixelsPerModule);
    }
}
