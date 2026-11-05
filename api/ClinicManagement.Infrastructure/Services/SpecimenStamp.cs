using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>The demo's « SPÉCIMEN » mark and footer line on every page of a finished PDF (<c>demo-cabinet</c> AC-5.5).</summary>
// On the finished bytes, not in DocumentPage: the CNAM overlay forms (P 061, BS1) never go through it.
public static class SpecimenStamp
{
    public const string Mark = "SPÉCIMEN — DÉMONSTRATION";

    public const string FooterLine = "Document de démonstration, sans valeur";

    private const string FontFamily = "bs1-sans";

    // Under the demo letterhead's pied de page, and its text still ~5 mm above the edge, which printers reach.
    private const double FooterBottomGap = 8;

    private static readonly XBrush MarkInk = new XSolidBrush(XColor.FromArgb(40, 190, 30, 30));
    private static readonly XBrush FooterInk = new XSolidBrush(XColor.FromArgb(255, 110, 110, 110));
    private static readonly XBrush FooterBackground = new XSolidBrush(XColor.FromArgb(220, 255, 255, 255));

    public static byte[] Apply(byte[] pdf)
    {
        Bs1FontResolver.EnsureInstalled();

        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Modify);
        foreach (var page in document.Pages)
        {
            Stamp(page);
        }

        document.Info.Subject = $"{Mark} · {FooterLine}";
        document.Info.Keywords = Mark;

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void Stamp(PdfPage page)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        var width = gfx.PageSize.Width;
        var height = gfx.PageSize.Height;

        // Sized so the mark spans three quarters of the page's diagonal, whatever the paper.
        var probe = new XFont(FontFamily, 100, XFontStyleEx.Bold);
        var diagonal = Math.Sqrt(width * width + height * height);
        var font = new XFont(FontFamily, 100 * diagonal * 0.75 / gfx.MeasureString(Mark, probe).Width, XFontStyleEx.Bold);

        var state = gfx.Save();
        gfx.RotateAtTransform(-Math.Atan2(height, width) * 180 / Math.PI, new XPoint(width / 2, height / 2));
        gfx.DrawString(Mark, font, MarkInk, new XRect(0, 0, width, height), XStringFormats.Center);
        gfx.Restore(state);

        var footerFont = new XFont(FontFamily, 8, XFontStyleEx.Regular);
        var textWidth = gfx.MeasureString(FooterLine, footerFont).Width;
        var box = new XRect((width - textWidth) / 2 - 6, height - FooterBottomGap - 12, textWidth + 12, 12);
        gfx.DrawRectangle(FooterBackground, box);
        gfx.DrawString(FooterLine, footerFont, FooterInk, box, XStringFormats.Center);
    }
}
