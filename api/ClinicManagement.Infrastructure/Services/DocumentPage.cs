using ClinicManagement.Application.Common.Files;
using ClinicManagement.Application.Common.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// The one owner of a QuestPDF page's geometry — size, margins, and where the cabinet's letterhead goes.
/// <para>
/// Without a letterhead the page is exactly what every document has always been: A4, 2 cm margins, the document's
/// own text header at the top of its content. With one, the bands are drawn edge to edge at true scale — the image's
/// width is the page's 210 mm, so each band lands where it sits on the cabinet's real paper — on EVERY page, and the
/// content keeps its 2 cm side margins between them. « Page entière » adds the strip between the bands behind the text.
/// </para>
/// <para>⚠️ Every <c>container.Page(…)</c> in <see cref="PdfGenerationService"/> goes through here; a sixth document
/// type that sets its own margins is a document that silently ignores the letterhead.</para>
/// </summary>
internal static class DocumentPage
{
    private const float SideMarginCm = 2;

    // The gap between the header band and the first line of content; the band carries its own white space too.
    private const float GapBelowHeaderCm = 0.6f;

    public static void Compose(
        PageDescriptor page, LetterheadImages? letterhead, Action<IContainer> content, Action<IContainer> footer)
    {
        page.Size(PageSizes.A4);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontSize(11).FontFamily("Helvetica"));

        if (letterhead == null)
        {
            page.Margin(SideMarginCm, Unit.Centimetre);
            page.Content().Element(content);
            page.Footer().Element(footer);
            return;
        }

        page.Margin(0);
        if (letterhead.Body != null)
        {
            // « Page entière »: the strip between the bands, behind the text, stretched to exactly the space they
            // leave — so a frame runs unbroken from the entête to the pied de page on every page. Only this strip is
            // stretched; the bands keep their true scale, so a paper that is not quite A4 never distorts its text.
            page.Background()
                .PaddingTop(PrintedHeight(letterhead.Header))
                .PaddingBottom(letterhead.Footer != null ? PrintedHeight(letterhead.Footer) : 0)
                .Image(letterhead.Body)
                .FitUnproportionally()
                .WithRasterDpi(300)
                .WithCompressionQuality(ImageCompressionQuality.Best);
        }

        page.Header().Element(band => Band(band, letterhead.Header));
        page.Content()
            .PaddingHorizontal(SideMarginCm, Unit.Centimetre)
            .PaddingTop(GapBelowHeaderCm, Unit.Centimetre)
            .Element(content);
        page.Footer().Column(column =>
        {
            column.Item().PaddingHorizontal(SideMarginCm, Unit.Centimetre).Element(footer);
            if (letterhead.Footer != null)
            {
                column.Item().Element(band => Band(band, letterhead.Footer));
            }
            else
            {
                // No footer band: keep the bottom margin the page always had.
                column.Item().Height(SideMarginCm, Unit.Centimetre);
            }
        });
    }

    // A band's height on the page, in points, once its width is the page's: what FitWidth gives it in its own slot.
    private static float PrintedHeight(byte[] png) =>
        PngDimensions.Read(png) is { } size ? PageSizes.A4.Width * size.Height / size.Width : 0;

    // 300 dpi and no lossy re-encode: a band is mostly printed text, and JPEG ringing around it is what looks amateur.
    private static void Band(IContainer container, byte[] png) =>
        container.Image(png)
            .FitWidth()
            .WithRasterDpi(300)
            .WithCompressionQuality(ImageCompressionQuality.Best);
}

/// <summary>
/// The money documents' identity header (note d'honoraires, devis, reçu, avoir) — it was four identical inline copies.
/// <para>With a letterhead the paper already says who the cabinet is, so only the matricule fiscal is printed: a
/// fiscal mention letterheads rarely carry, and one a note d'honoraires must.</para>
/// </summary>
internal static class ClinicIdentityHeader
{
    public static Action<IContainer> Compose(
        string clinicName, string? address, string? phone, string? matriculeFiscal, bool onLetterhead) =>
        container => container.Column(header =>
        {
            header.Spacing(3);
            if (!onLetterhead)
            {
                header.Item().Text(clinicName).FontSize(14).Bold().FontColor(Colors.Blue.Darken2).FontFamily("Helvetica");
                if (!string.IsNullOrWhiteSpace(address))
                    header.Item().Text(address).FontSize(10).FontFamily("Helvetica");
                if (!string.IsNullOrWhiteSpace(phone))
                    header.Item().Text($"Tél : {phone}").FontSize(10).FontFamily("Helvetica");
            }

            if (!string.IsNullOrWhiteSpace(matriculeFiscal))
                header.Item().Text($"Matricule fiscal : {matriculeFiscal}").FontSize(10).FontFamily("Helvetica");
        });
}
