using System.Globalization;
using System.Text.Json;
using ClinicManagement.Application.Common.Models;
using Microsoft.Extensions.Logging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ClinicManagement.Infrastructure.Services;

// Renders a "bulletin-cnam" document by stamping its data onto the genuine CNAM BS1 form
// (Assets/BS1.pdf) at calibrated coordinates, so the printed output is an acceptable official
// Bulletin de soins rather than a custom-drawn table.
//
// Coordinates are PDF points in PdfSharp's top-left-origin space, calibrated to the bundled 2-page
// A4-landscape BS1 (841.89 × 595.276). Only the dentist-relevant regions are filled; every other
// section of the form is left blank (physical stamp/signature included). When acts exceed the six
// rows of the dental table, extra copies of the identity+acts page are appended so nothing is dropped.
internal sealed class CnamBs1BulletinRenderer
{
    private const int ActsPerPage = 6;
    private const string AssetRelativePath = "Assets/BS1.pdf";

    // Optional: when supplied, malformed acts JSON (silently rendered as an act-less form) is surfaced
    // as a Warning so an operator can discover why a bulletin came out with no acts.
    private readonly ILogger? _logger;

    public CnamBs1BulletinRenderer()
    {
    }

    public CnamBs1BulletinRenderer(ILogger logger)
    {
        _logger = logger;
    }

    // ---- Fonts ----
    private const string FontFamily = "bs1-sans";
    private static XFont FieldFont => new(FontFamily, 10, XFontStyleEx.Regular);
    private static XFont TableFont => new(FontFamily, 8, XFontStyleEx.Regular);
    private static XFont TickFont => new(FontFamily, 10, XFontStyleEx.Bold);
    private static XFont TickFontLarge => new(FontFamily, 12, XFontStyleEx.Bold);
    private static XFont DigitFont => new(FontFamily, 11, XFontStyleEx.Regular);

    // ================= PAGE 0 (identity + assuré/malade + dental acts) =================

    // Identifiant Unique — 10 digit cells (centres) sharing one vertical band.
    private static readonly double[] IduCellCentersX = { 555, 571, 587, 603, 618, 634, 650, 665, 681, 697 };
    private const double IduCellTopY = 175.5;
    private const double IduCellHeight = 20.5;
    private const double IduCellWidth = 14;

    // Régime checkboxes (draw an X centred in the box).
    private static readonly XRect RegimeCnss = new(548.8, 205.1, 14.0, 8.7);
    private static readonly XRect RegimeCnrps = new(625.2, 205.1, 14.0, 8.7);
    private static readonly XRect RegimeConvention = new(755.6, 205.1, 13.4, 8.7);

    // Assuré social dotted-line baselines.
    private const double AssureFieldX = 508;
    private const double AssurePrenomBaselineY = 250;
    private const double AssureNomBaselineY = 269;
    private const double AssureAdresseBaselineY = 287;
    private const double AssureCodePostalX = 520;
    private const double AssureCodePostalBaselineY = 324;

    // "Le malade" lien option cells (draw an X near the left inside each cell).
    private static readonly XRect LienAscendant = new(483.4, 363.6, 67.0, 16.2);
    private static readonly XRect LienEnfant = new(562.9, 363.6, 55.0, 16.2);
    private static readonly XRect LienConjoint = new(630.5, 363.6, 53.0, 16.2);
    private static readonly XRect LienAssureSocial = new(696.9, 363.6, 63.0, 16.2);

    // Malade identity dotted-line baselines.
    private const double MaladePrenomX = 509;
    private const double MaladePrenomBaselineY = 410;
    private const double MaladeNomX = 500;
    private const double MaladeNomBaselineY = 428;
    private const double MaladeNaissanceX = 548;
    private const double MaladeNaissanceBaselineY = 447;
    private const double MaladeTelX = 606;
    private const double MaladeTelBaselineY = 464;

    // Dental acts table (Consultations et actes de soins dentaires) — 6 rows.
    private const double ActsFirstRowTopY = 94.0;
    private const double ActsRowHeight = 23.66;
    private const double ActRowBaselineOffsetY = 15; // text baseline offset within a table row.
    private const double ActColDateX = 62;
    private const double ActColDentX = 107;
    private const double ActColCodeActeX = 134;
    private const double ActColCotationX = 219;
    private const double ActColHonorairesRightX = 286;
    private const double ActColCodeProfX = 291;

    // Usable text width per column (to the next column, minus a small gutter). A cell's text is shrunk to
    // fit its column so a wide value — e.g. a multi-tooth "35, 38, 44" in the narrow DENT column — never
    // overflows into the neighbouring column.
    private const double ActColDateWidth = 42;
    private const double ActColDentWidth = 25;
    private const double ActColCodeActeWidth = 82;
    private const double ActColCotationWidth = 20;
    private const double ActColHonorairesWidth = 44;
    private const double ActColCodeProfWidth = 46;
    private const double TableFontMinSize = 5;

    // ================= PAGE 1 (cadre de soins) =================
    private static readonly XRect CareApci = new(105.5, 80.0, 17.8, 17.5);
    private static readonly XRect CareMo = new(164.4, 80.0, 17.8, 17.5);
    private static readonly XRect CareHospitalisation = new(263.9, 80.0, 18.5, 17.5);
    private static readonly XRect CareSuiviGrossesse = new(365.0, 80.0, 16.2, 17.5);
    private const double ApciCodeX = 110;
    private const double ApciCodeBaselineY = 116;

    public byte[] Render(MedicalDocumentPdfData data)
    {
        Bs1FontResolver.EnsureInstalled();

        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "BS1.pdf");
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException(
                $"Génération du bulletin CNAM impossible : le formulaire officiel BS1 est introuvable "
                + $"({AssetRelativePath}). Réinstallez l'application ou restaurez le fichier avant de réessayer.");
        }

        byte[] templateBytes;
        try
        {
            templateBytes = File.ReadAllBytes(templatePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Génération du bulletin CNAM impossible : le formulaire officiel BS1 est illisible. "
                + "Réinstallez l'application ou restaurez le fichier avant de réessayer.", ex);
        }

        var model = Bs1Model.From(data);
        if (model.ActsMalformed)
        {
            _logger?.LogWarning(
                "Bulletin CNAM : le contenu « acts » est mal formé et a été ignoré — le formulaire est rendu sans actes.");
        }

        var actPages = ChunkActs(model.Acts);

        using var document = OpenTemplate(templateBytes);

        StampAssureAndMalade(document.Pages[0], model);
        StampCadreDeSoins(document.Pages[1], model);
        StampActs(document.Pages[0], actPages.Count > 0 ? actPages[0] : new List<Bs1Act>(), model.DoctorCodeProfessionnel);

        // AC-4: overflow acts get appended copies of the identity+acts page so no act is dropped.
        // The import source must be opened in Import mode (PdfSharp forbids importing a page from a
        // Modify-mode document); a single Import-mode document can serve every AddPage, so it is opened
        // once before the loop rather than re-parsed per extra page. AddPage returns the imported page
        // now owned by `document`, which is Modify-mode and can therefore be stamped in place.
        if (actPages.Count > 1)
        {
            using var importSource = PdfReader.Open(new MemoryStream(templateBytes), PdfDocumentOpenMode.Import);
            for (var page = 1; page < actPages.Count; page++)
            {
                var appended = document.AddPage(importSource.Pages[0]);
                StampAssureAndMalade(appended, model);
                StampActs(appended, actPages[page], model.DoctorCodeProfessionnel);
            }
        }

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static PdfDocument OpenTemplate(byte[] templateBytes)
        => PdfReader.Open(new MemoryStream(templateBytes), PdfDocumentOpenMode.Modify);

    private static List<List<Bs1Act>> ChunkActs(IReadOnlyList<Bs1Act> acts)
    {
        var pages = new List<List<Bs1Act>>();
        for (var i = 0; i < acts.Count; i += ActsPerPage)
        {
            pages.Add(acts.Skip(i).Take(ActsPerPage).ToList());
        }

        return pages;
    }

    private static void StampAssureAndMalade(PdfPage page, Bs1Model model)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        // Build each font once per page rather than re-constructing on every cell/field access.
        var digitFont = DigitFont;
        var tickFont = TickFont;
        var fieldFont = FieldFont;
        var tableFont = TableFont;

        // Identifiant Unique — one character per cell, left to right.
        var idu = model.IdentifiantUnique;
        for (var i = 0; i < idu.Length && i < IduCellCentersX.Length; i++)
        {
            var cell = new XRect(IduCellCentersX[i] - IduCellWidth / 2, IduCellTopY, IduCellWidth, IduCellHeight);
            DrawCentered(gfx, idu[i].ToString(), digitFont, cell);
        }

        // Régime — tick exactly the selected option (unknown value → nothing ticked).
        switch (model.Regime)
        {
            case "CNSS":
                DrawCentered(gfx, "X", tickFont, RegimeCnss);
                break;
            case "CNRPS":
                DrawCentered(gfx, "X", tickFont, RegimeCnrps);
                break;
            case "Convention bilatérale":
                DrawCentered(gfx, "X", tickFont, RegimeConvention);
                break;
        }

        // L'assuré social.
        DrawLeft(gfx, model.AssureFirstName, fieldFont, AssureFieldX, AssurePrenomBaselineY);
        DrawLeft(gfx, model.AssureLastName, fieldFont, AssureFieldX, AssureNomBaselineY);
        DrawLeft(gfx, model.AssureAddress, fieldFont, AssureFieldX, AssureAdresseBaselineY);
        DrawLeft(gfx, model.AssurePostalCode, fieldFont, AssureCodePostalX, AssureCodePostalBaselineY);

        // Le malade — lien + rang.
        var lienCell = model.MaladeLien switch
        {
            "Assuré lui-même" => (XRect?)LienAssureSocial,
            "Conjoint" => LienConjoint,
            "Enfant" => LienEnfant,
            "Ascendant" => LienAscendant,
            _ => null,
        };
        if (lienCell.HasValue)
        {
            var cell = lienCell.Value;
            DrawCentered(gfx, "X", tickFont, new XRect(cell.X + 1.5, cell.Y + 3.5, 9, 9));
            if (!string.IsNullOrWhiteSpace(model.MaladeLienRang))
            {
                DrawCentered(gfx, model.MaladeLienRang, tableFont, new XRect(cell.X + cell.Width - 14, cell.Y + 3.5, 12, 9));
            }
        }

        DrawLeft(gfx, model.MaladeFirstName, fieldFont, MaladePrenomX, MaladePrenomBaselineY);
        DrawLeft(gfx, model.MaladeLastName, fieldFont, MaladeNomX, MaladeNomBaselineY);
        DrawLeft(gfx, model.MaladeDateOfBirth, fieldFont, MaladeNaissanceX, MaladeNaissanceBaselineY);
        DrawLeft(gfx, model.MaladePhone, fieldFont, MaladeTelX, MaladeTelBaselineY);
    }

    private static void StampActs(PdfPage page, IReadOnlyList<Bs1Act> acts, string doctorCodeProfessionnel)
    {
        if (acts.Count == 0)
        {
            return;
        }

        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        var tableFont = TableFont; // built once; reused for any cell whose text already fits its column.

        // Return the shared 8pt font when the text fits the column, else a smaller font shrunk to fit
        // (down to a readable floor) so no cell overflows into the next column.
        XFont Fit(string? text, double maxWidth)
        {
            if (string.IsNullOrEmpty(text) || gfx.MeasureString(text, tableFont).Width <= maxWidth)
            {
                return tableFont;
            }

            var size = tableFont.Size;
            while (size > TableFontMinSize
                && gfx.MeasureString(text, new XFont(FontFamily, size, XFontStyleEx.Regular)).Width > maxWidth)
            {
                size -= 0.5;
            }

            return new XFont(FontFamily, size, XFontStyleEx.Regular);
        }

        for (var row = 0; row < acts.Count && row < ActsPerPage; row++)
        {
            var act = acts[row];
            var baselineY = ActsFirstRowTopY + row * ActsRowHeight + ActRowBaselineOffsetY;

            DrawLeft(gfx, act.Date, Fit(act.Date, ActColDateWidth), ActColDateX, baselineY);
            DrawLeft(gfx, act.Teeth, Fit(act.Teeth, ActColDentWidth), ActColDentX, baselineY);
            DrawLeft(gfx, act.CodeActe, Fit(act.CodeActe, ActColCodeActeWidth), ActColCodeActeX, baselineY);
            DrawLeft(gfx, act.Cotation, Fit(act.Cotation, ActColCotationWidth), ActColCotationX, baselineY);
            DrawRight(gfx, act.Honoraires, Fit(act.Honoraires, ActColHonorairesWidth), ActColHonorairesRightX, baselineY);
            DrawLeft(gfx, doctorCodeProfessionnel, Fit(doctorCodeProfessionnel, ActColCodeProfWidth), ActColCodeProfX, baselineY);
        }
    }

    private static void StampCadreDeSoins(PdfPage page, Bs1Model model)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        switch (model.CareType)
        {
            case "APCI":
                DrawCentered(gfx, "X", TickFontLarge, CareApci);
                DrawLeft(gfx, model.ApciCode, FieldFont, ApciCodeX, ApciCodeBaselineY);
                break;
            case "MO":
                DrawCentered(gfx, "X", TickFontLarge, CareMo);
                break;
            case "Hospitalisation":
                DrawCentered(gfx, "X", TickFontLarge, CareHospitalisation);
                break;
            case "Suivi de grossesse":
                DrawCentered(gfx, "X", TickFontLarge, CareSuiviGrossesse);
                break;
        }
    }

    private static void DrawLeft(XGraphics gfx, string? text, XFont font, double x, double baselineY)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        gfx.DrawString(text, font, XBrushes.Black, new XPoint(x, baselineY), XStringFormats.BaseLineLeft);
    }

    private static void DrawRight(XGraphics gfx, string? text, XFont font, double rightX, double baselineY)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        gfx.DrawString(text, font, XBrushes.Black, new XPoint(rightX, baselineY), XStringFormats.BaseLineRight);
    }

    private static void DrawCentered(XGraphics gfx, string? text, XFont font, XRect box)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        gfx.DrawString(text, font, XBrushes.Black, box, XStringFormats.Center);
    }

    // Parsed, display-ready view of the bulletin content — decouples coordinate stamping from the
    // loosely-typed Content dictionary the frontend/editor fills.
    private sealed class Bs1Model
    {
        public string IdentifiantUnique { get; private init; } = string.Empty;
        public string Regime { get; private init; } = string.Empty;
        public string AssureFirstName { get; private init; } = string.Empty;
        public string AssureLastName { get; private init; } = string.Empty;
        public string AssureAddress { get; private init; } = string.Empty;
        public string AssurePostalCode { get; private init; } = string.Empty;
        public string MaladeLien { get; private init; } = string.Empty;
        public string MaladeLienRang { get; private init; } = string.Empty;
        public string MaladeFirstName { get; private init; } = string.Empty;
        public string MaladeLastName { get; private init; } = string.Empty;
        public string MaladeDateOfBirth { get; private init; } = string.Empty;
        public string MaladePhone { get; private init; } = string.Empty;
        public string CareType { get; private init; } = string.Empty;
        public string ApciCode { get; private init; } = string.Empty;
        public string DoctorCodeProfessionnel { get; private init; } = string.Empty;
        public IReadOnlyList<Bs1Act> Acts { get; private init; } = new List<Bs1Act>();

        // True when the "acts" content was present but could not be parsed, so the form renders with no
        // acts — surfaced by the renderer as a Warning rather than silently swallowed.
        public bool ActsMalformed { get; private init; }

        public static Bs1Model From(MedicalDocumentPdfData data)
        {
            string Get(string key) => data.Content.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

            var (fallbackFirst, fallbackLast) = SplitName(data.PatientName);
            var maladeFirst = Get("maladeFirstName");
            var maladeLast = Get("maladeLastName");
            var acts = ParseActs(Get("acts"), out var actsMalformed);

            return new Bs1Model
            {
                // The IDU is combed one digit per fixed cell, so strip any spaces/dashes/letters the
                // free-text field may contain — otherwise a non-digit shifts every following digit.
                IdentifiantUnique = OnlyDigits(Get("identifiantUnique")),
                Regime = Get("regime"),
                AssureFirstName = Get("assureFirstName"),
                AssureLastName = Get("assureLastName"),
                AssureAddress = Get("assureAddress"),
                AssurePostalCode = Get("assurePostalCode"),
                MaladeLien = Get("maladeLien"),
                MaladeLienRang = Get("maladeLienRang"),
                MaladeFirstName = string.IsNullOrWhiteSpace(maladeFirst) ? fallbackFirst : maladeFirst,
                MaladeLastName = string.IsNullOrWhiteSpace(maladeLast) ? fallbackLast : maladeLast,
                // The malade's date of birth — carried in the content (persisted in ContentJson) so both the
                // browser-download and the background-job PDF paths get the real DOB. Falls back to the
                // legacy PatientAge field only for documents saved before the DOB was added to the content.
                MaladeDateOfBirth = Get("patientDateOfBirth") is { Length: > 0 } dob
                    ? dob
                    : data.PatientAge?.Trim() ?? string.Empty,
                MaladePhone = Get("patientPhone"),
                CareType = Get("careType"),
                ApciCode = Get("apciCode"),
                DoctorCodeProfessionnel = Get("doctorCodeProfessionnel"),
                Acts = acts,
                ActsMalformed = actsMalformed,
            };
        }

        private static string OnlyDigits(string value)
            => string.IsNullOrEmpty(value) ? value : new string(value.Where(char.IsDigit).ToArray());

        private static (string First, string Last) SplitName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return (string.Empty, string.Empty);
            }

            var trimmed = fullName.Trim();
            var firstSpace = trimmed.IndexOf(' ');
            return firstSpace < 0
                ? (trimmed, string.Empty)
                : (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
        }

        private static IReadOnlyList<Bs1Act> ParseActs(string actsJson, out bool malformed)
        {
            malformed = false;
            if (string.IsNullOrWhiteSpace(actsJson))
            {
                return new List<Bs1Act>();
            }

            try
            {
                using var parsed = JsonDocument.Parse(actsJson);
                if (parsed.RootElement.ValueKind != JsonValueKind.Array)
                {
                    malformed = true;
                    return new List<Bs1Act>();
                }

                var acts = new List<Bs1Act>();
                foreach (var element in parsed.RootElement.EnumerateArray())
                {
                    acts.Add(Bs1Act.From(element));
                }

                return acts;
            }
            catch (JsonException)
            {
                // Malformed acts JSON — render the form without acts rather than failing generation.
                malformed = true;
                return new List<Bs1Act>();
            }
        }
    }

    private sealed class Bs1Act
    {
        public string Date { get; private init; } = string.Empty;
        public string Teeth { get; private init; } = string.Empty;
        public string CodeActe { get; private init; } = string.Empty;
        public string Cotation { get; private init; } = string.Empty;
        public string Honoraires { get; private init; } = string.Empty;

        public static Bs1Act From(JsonElement element)
        {
            string Get(string prop) => element.TryGetProperty(prop, out var value)
                ? (value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString())
                : string.Empty;

            return new Bs1Act
            {
                Date = FormatDate(Get("date")),
                Teeth = Get("teeth").Trim(),
                CodeActe = Get("codeActe").Trim(),
                Cotation = Get("cotation").Trim(),
                Honoraires = FormatHonoraires(Get("honoraires")),
            };
        }

        // Acts come from the editor as ISO "yyyy-MM-dd"; the form shows French "dd/MM/yyyy".
        private static string FormatDate(string raw)
        {
            var value = raw.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : value;
        }

        // AC-3: TND with 3 decimals (millimes), no currency symbol, consistent with the recorded cost.
        private static string FormatHonoraires(string raw)
        {
            var value = raw.Trim();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)
                ? amount.ToString("0.000", CultureInfo.InvariantCulture)
                : value;
        }
    }
}
