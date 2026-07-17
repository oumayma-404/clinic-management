namespace ClinicManagement.Application.Common.Interfaces;

/// <summary>Renders a QR « cachet électronique visible » payload to a PNG image (FR-7).</summary>
public interface IQrCodeGenerator
{
    byte[] GeneratePng(string payload, int pixelsPerModule = 10);
}
