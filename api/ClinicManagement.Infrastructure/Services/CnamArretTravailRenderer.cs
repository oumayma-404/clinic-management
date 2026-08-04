using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ClinicManagement.Infrastructure.Services;

/// <summary>
/// Stamps an <c>arret-travail</c> document onto the genuine CNAM **P 061** form
/// (<c>Assets/P61.pdf</c>) at calibrated coordinates — the second overlay renderer, on
/// <see cref="CnamBs1BulletinRenderer"/>'s pattern (<c>adoption-qa-l</c> L11).
///
/// <para><b>What was missing.</b> « arrêt de travail » appeared <b>once</b> in the whole repository — as a
/// description string on the generic certificat tile — and <c>features/cnam-arret-travail-overlay/</c> contained
/// only its three bundled PDFs, no spec and no code. A dentist signing a patient off work either hand-wrote it or
/// printed a free-text certificat that the caisse does not accept.</para>
///
/// <para><b>Which form, and how that was settled.</b> The feature folder bundles three scans and the spec flags the
/// choice as unverified. <c>P61_2024.pdf</c> is the one: it is titled « P 061 — DEMANDE D'INDEMNITÉ DE MALADIE »
/// over « CERTIFICAT MÉDICAL D'ARRÊT DE TRAVAIL », which is exactly this document. <c>CMIATMP.pdf</c> is a
/// *different* form — the AT/MP « certificat médical » for accidents du travail and maladies professionnelles — and
/// P 061 says so itself in its own header (« Ce formulaire ne concerne pas les accidents de travail et les maladies
/// professionnelles. Dans ces situations, il y a lieu d'utiliser le formulaire du certificat médical prévu à cet
/// effet »), so the two are not alternatives. <c>p61.pdf</c> is the same P 061 in an older printing (it still
/// carries « INDEMNITÉ DE COUCHES » and « Période initiale / Prolongation » boxes the 2024 revision drops).
/// ⚠️ <b>Still a judgement, not a verified fact</b>: no official CNAM publication was retrieved, so an operator who
/// finds the caisse using another revision must recalibrate rather than assume this is authoritative.</para>
///
/// <para><b>The asset is a normalised copy, deliberately.</b> The bundled scan is an A4 <i>portrait</i> page whose
/// form content runs sideways; <c>Assets/P61.pdf</c> is that file with the rotation <b>baked into the content
/// stream</b> as A4 landscape (841.89 × 595.276). Rotating at draw time instead would mean every coordinate below
/// was expressed in a frame nobody can read off a printout, and relying on the page's <c>/Rotate</c> entry would
/// mean trusting PdfSharp's handling of it. One transform, done once, and the numbers here match what a ruler on
/// the paper measures.</para>
///
/// <para>⚠️ <b>Coordinates are calibrated by eye and no test in this repository can assert them.</b> They were
/// derived by measuring the scan's own rules, dotted baselines and checkbox strokes, then stamping representative
/// values onto the real asset and rendering it — see the L11 section of the feature's <c>progress.md</c>. A unit
/// test can only prove this file produces *a* PDF. <b>Print onto the real form and check by eye before relying on
/// it.</b></para>
///
/// <para><b>Both panels are filled.</b> The left half is « À remplir par l'assuré social » and the right half is the
/// practitioner's certificate. Prefilling the left is not overreach: every field on it (identifiant unique, prénom,
/// nom, date de naissance, adresse, code postal, téléphone) is data the product already holds and the patient would
/// otherwise copy out by hand, which is where an identifiant gets a digit wrong. The two <b>signature</b> spaces are
/// left blank — the patient signs theirs and the practitioner stamps and signs theirs, on paper.</para>
/// </summary>
internal sealed class CnamArretTravailRenderer
{
    private const string AssetRelativePath = "Assets/P61.pdf";

    // ---- Fonts ----
    // The same resolver the BS1 renderer installs: one font family for both overlays, so a machine that can print
    // one can print the other. `EnsureInstalled` is idempotent.
    private const string FontFamily = "bs1-sans";
    private static XFont FieldFont => new(FontFamily, 10, XFontStyleEx.Regular);
    private static XFont SmallFont => new(FontFamily, 9, XFontStyleEx.Regular);
    private static XFont DigitFont => new(FontFamily, 11, XFontStyleEx.Regular);
    private static XFont TickFont => new(FontFamily, 10, XFontStyleEx.Bold);
    private static XFont TickFontSmall => new(FontFamily, 8, XFontStyleEx.Bold);

    // ================= LEFT PANEL — « À remplir par l'assuré social » =================

    // Identifiant unique — 10 cells sharing one band. Cell centres are derived from the comb's measured left edge
    // and cell width rather than listed one by one: the comb is genuinely regular, and ten literals would be ten
    // chances to mistype one.
    private const double IduCombLeftX = 110.1;
    private const double IduCellWidth = 23.88;
    private const int IduCellCount = 10;
    private const double IduBaselineY = 227.0;

    private const double AssurePrenomX = 110.0;
    private const double AssurePrenomBaselineY = 262.0;
    private const double AssureNomX = 95.0;
    private const double AssureNomBaselineY = 287.0;
    private const double AssureDobX = 160.0;
    private const double AssureDobBaselineY = 312.0;

    // The address goes on the **continuation** dotted line, not on the label's own trailing dots: those leave
    // ~17 pt of usable width, which fits no Tunisian address. The wide line below gives ~275 pt.
    private const double AddressX = 66.0;
    private const double AddressBaselineY = 357.0;
    private const double AddressWidth = 275.0;

    private const double CpCombLeftX = 178.3;
    private const double CpCellWidth = 22.7;
    private const int CpCellCount = 4;
    private const double CpBaselineY = 388.0;

    private const double PhoneX = 173.0;
    private const double PhoneBaselineY = 419.0;

    // ================= RIGHT PANEL — « Certificat médical d'arrêt de travail » =================
    private const double DoctorNameX = 566.0;
    private const double DoctorNameBaselineY = 152.0;
    private const double DoctorNameWidth = 172.0;
    private const double DoctorQualityX = 626.0;
    private const double DoctorQualityBaselineY = 169.0;
    private const double DoctorQualityWidth = 116.0;
    private const double CityX = 458.0;
    private const double CityBaselineY = 188.0;
    private const double CodeConventionnelX = 629.0;
    private const double CodeConventionnelBaselineY = 203.0;
    private const double OrdreX = 629.0;
    private const double OrdreBaselineY = 224.0;

    private const double PatientNameX = 449.0;
    private const double PatientNameBaselineY = 260.0;
    private const double PatientNameWidth = 158.0;

    private const double DaysX = 519.0;
    private const double FromDateX = 648.0;
    private const double DurationBaselineY = 301.0;

    private static readonly XRect OutingsBox = new(545.9, 304.4, 17.1, 18.9);
    private const double OutingsFromX = 600.0;
    private const double OutingsToX = 668.0;
    private const double OutingsBaselineY = 319.0;

    // Traumatisme — three exclusive causes. All three boxes share one x band; only the y moves.
    private const double TraumaBoxX = 463.1;
    private const double TraumaBoxWidth = 7.1;
    private const double TraumaBoxHeight = 7.1;
    private const double TraumaAvpBoxY = 407.5;
    private const double TraumaDomesticBoxY = 423.5;
    private const double TraumaViolenceBoxY = 439.6;

    // Hospitalisation — Non / Oui.
    private const double HospBoxX = 635.0;
    private const double HospBoxWidth = 7.4;
    private const double HospBoxHeight = 7.4;
    private const double HospNoBoxY = 406.5;
    private const double HospYesBoxY = 422.6;

    private const double SignPlaceX = 452.0;
    private const double SignDateX = 502.0;
    private const double SignBaselineY = 494.0;

    /// <summary>
    /// Smallest size a shrunk-to-fit field is allowed to reach. Below this the value is illegible on paper, so it is
    /// better to let it run slightly wide than to print something nobody can read at a caisse counter.
    /// </summary>
    private const double MinFontSize = 6.0;

    public byte[] Render(MedicalDocumentPdfData data)
    {
        Bs1FontResolver.EnsureInstalled();

        var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "P61.pdf");
        if (!File.Exists(templatePath))
        {
            throw new InvalidOperationException(
                "Génération de l'arrêt de travail impossible : le formulaire officiel CNAM P 061 est introuvable "
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
                "Génération de l'arrêt de travail impossible : le formulaire officiel CNAM P 061 est illisible. "
                + "Réinstallez l'application ou restaurez le fichier avant de réessayer.", ex);
        }

        var model = ArretTravailModel.From(data);

        using var document = PdfReader.Open(new MemoryStream(templateBytes), PdfDocumentOpenMode.Modify);
        Stamp(document.Pages[0], model);

        using var output = new MemoryStream();
        document.Save(output);
        return output.ToArray();
    }

    private static void Stamp(PdfPage page, ArretTravailModel model)
    {
        using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

        var fieldFont = FieldFont;
        var smallFont = SmallFont;
        var digitFont = DigitFont;
        var tickFont = TickFont;
        var tickFontSmall = TickFontSmall;

        // ---- Left panel: the assuré social's own half, prefilled from the patient's fiche ----
        Comb(gfx, model.IdentifiantUnique, digitFont, IduCombLeftX, IduCellWidth, IduCellCount, IduBaselineY);
        DrawLeft(gfx, model.PatientFirstName, fieldFont, AssurePrenomX, AssurePrenomBaselineY);
        DrawLeft(gfx, model.PatientLastName, fieldFont, AssureNomX, AssureNomBaselineY);
        DrawLeft(gfx, model.PatientDateOfBirth, fieldFont, AssureDobX, AssureDobBaselineY);
        DrawFitted(gfx, model.PatientAddress, smallFont, AddressX, AddressBaselineY, AddressWidth);
        Comb(gfx, model.PostalCode, digitFont, CpCombLeftX, CpCellWidth, CpCellCount, CpBaselineY);
        DrawLeft(gfx, model.PatientPhone, fieldFont, PhoneX, PhoneBaselineY);

        // ---- Right panel: the practitioner's certificate ----
        DrawFitted(gfx, model.DoctorName, fieldFont, DoctorNameX, DoctorNameBaselineY, DoctorNameWidth);
        DrawFitted(gfx, model.DoctorQuality, fieldFont, DoctorQualityX, DoctorQualityBaselineY, DoctorQualityWidth);
        DrawLeft(gfx, model.City, fieldFont, CityX, CityBaselineY);
        DrawLeft(gfx, model.DoctorCodeConventionnel, fieldFont, CodeConventionnelX, CodeConventionnelBaselineY);
        DrawLeft(gfx, model.DoctorOrdreNumber, fieldFont, OrdreX, OrdreBaselineY);
        DrawFitted(gfx, model.PatientFullName, fieldFont, PatientNameX, PatientNameBaselineY, PatientNameWidth);

        DrawLeft(gfx, model.Days, fieldFont, DaysX, DurationBaselineY);
        DrawLeft(gfx, model.FromDate, fieldFont, FromDateX, DurationBaselineY);

        // « Sorties autorisées » — the box is ticked only when hours were actually supplied. A tick with no hours
        // beside it is a claim the form cannot express, and the caisse reads the empty hours as the answer.
        if (model.OutingsAllowed)
        {
            DrawCentered(gfx, "X", tickFont, OutingsBox);
            DrawLeft(gfx, model.OutingsFrom, fieldFont, OutingsFromX, OutingsBaselineY);
            DrawLeft(gfx, model.OutingsTo, fieldFont, OutingsToX, OutingsBaselineY);
        }

        // Traumatisme cause — at most one. The three are mutually exclusive on the form, and the model resolves
        // them to a single value rather than three booleans, so « two causes ticked » is unrepresentable.
        var traumaBoxY = model.TraumaCause switch
        {
            ArretTravailTraumaCause.VoiePublique => TraumaAvpBoxY,
            ArretTravailTraumaCause.Domestique => TraumaDomesticBoxY,
            ArretTravailTraumaCause.Violence => TraumaViolenceBoxY,
            _ => (double?)null
        } ?? 0d;
        if (model.TraumaCause != ArretTravailTraumaCause.None)
        {
            DrawCentered(gfx, "X", tickFontSmall,
                new XRect(TraumaBoxX, traumaBoxY, TraumaBoxWidth, TraumaBoxHeight));
        }

        // Hospitalisation — a tri-state, and the third state is « not answered ». Ticking « Non » by default would
        // be the renderer asserting a clinical fact nobody entered, on a form that decides an indemnity.
        if (model.Hospitalised is { } hospitalised)
        {
            DrawCentered(gfx, "X", tickFontSmall,
                new XRect(HospBoxX, hospitalised ? HospYesBoxY : HospNoBoxY, HospBoxWidth, HospBoxHeight));
        }

        DrawLeft(gfx, model.SignPlace, fieldFont, SignPlaceX, SignBaselineY);
        DrawLeft(gfx, model.SignDate, fieldFont, SignDateX, SignBaselineY);
    }

    /// <summary>One character per fixed cell, left to right, each centred in its own cell.</summary>
    private static void Comb(
        XGraphics gfx, string digits, XFont font, double leftX, double cellWidth, int cellCount, double baselineY)
    {
        if (string.IsNullOrEmpty(digits))
        {
            return;
        }

        // Over-length values are refused at the write (ArretTravailValidation), so the clamp here is the belt to
        // that braces: a legacy row must print the digits it can rather than throw at PDF time.
        var count = Math.Min(digits.Length, cellCount);
        for (var i = 0; i < count; i++)
        {
            var cell = new XRect(leftX + i * cellWidth, baselineY - 11, cellWidth, 14);
            DrawCentered(gfx, digits[i].ToString(), font, cell);
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

    /// <summary>
    /// Left-aligned, shrunk until it fits <paramref name="maxWidth"/>. Every field on this form is followed by
    /// another field on the same printed line, so a long value that overflows does not merely look untidy — it
    /// overwrites the neighbour and makes the form ambiguous.
    /// </summary>
    private static void DrawFitted(
        XGraphics gfx, string? text, XFont font, double x, double baselineY, double maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var fitted = font;
        while (gfx.MeasureString(text, fitted).Width > maxWidth && fitted.Size > MinFontSize)
        {
            fitted = new XFont(FontFamily, fitted.Size - 0.5, fitted.Style);
        }

        gfx.DrawString(text, fitted, XBrushes.Black, new XPoint(x, baselineY), XStringFormats.BaseLineLeft);
    }

    private static void DrawCentered(XGraphics gfx, string? text, XFont font, XRect box)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        gfx.DrawString(text, font, XBrushes.Black, box, XStringFormats.Center);
    }
}

/// <summary>Which traumatisme box the form ticks, if any. Mutually exclusive by construction.</summary>
internal enum ArretTravailTraumaCause
{
    None = 0,
    VoiePublique,
    Domestique,
    Violence
}

/// <summary>
/// The parsed, display-ready view of an arrêt-de-travail document — the same shape and the same reason as
/// <c>Bs1Model</c>: it decouples coordinate stamping from the loosely-typed content dictionary the editor fills.
/// </summary>
internal sealed class ArretTravailModel
{
    public string IdentifiantUnique { get; private init; } = string.Empty;
    public string PatientFirstName { get; private init; } = string.Empty;
    public string PatientLastName { get; private init; } = string.Empty;
    public string PatientFullName { get; private init; } = string.Empty;
    public string PatientDateOfBirth { get; private init; } = string.Empty;
    public string PatientAddress { get; private init; } = string.Empty;
    public string PostalCode { get; private init; } = string.Empty;
    public string PatientPhone { get; private init; } = string.Empty;

    public string DoctorName { get; private init; } = string.Empty;
    public string DoctorQuality { get; private init; } = string.Empty;
    public string City { get; private init; } = string.Empty;
    public string DoctorCodeConventionnel { get; private init; } = string.Empty;
    public string DoctorOrdreNumber { get; private init; } = string.Empty;

    public string Days { get; private init; } = string.Empty;
    public string FromDate { get; private init; } = string.Empty;

    public bool OutingsAllowed { get; private init; }
    public string OutingsFrom { get; private init; } = string.Empty;
    public string OutingsTo { get; private init; } = string.Empty;

    public ArretTravailTraumaCause TraumaCause { get; private init; }

    /// <summary>Null = not answered, which is a real third state on this form. See the renderer.</summary>
    public bool? Hospitalised { get; private init; }

    public string SignPlace { get; private init; } = string.Empty;
    public string SignDate { get; private init; } = string.Empty;

    public static ArretTravailModel From(MedicalDocumentPdfData data)
    {
        string Get(string key) =>
            data.Content.TryGetValue(key, out var value) ? value?.Trim() ?? string.Empty : string.Empty;

        var (fallbackFirst, fallbackLast) = SplitName(data.PatientName);
        var first = Get(ArretTravailKeys.PatientFirstName);
        var last = Get(ArretTravailKeys.PatientLastName);
        first = string.IsNullOrWhiteSpace(first) ? fallbackFirst : first;
        last = string.IsNullOrWhiteSpace(last) ? fallbackLast : last;

        var from = Get(ArretTravailKeys.OutingsFrom);
        var to = Get(ArretTravailKeys.OutingsTo);

        return new ArretTravailModel
        {
            // Combed one digit per fixed cell, so anything that is not a digit is stripped: a space or a dash left
            // in the free-text field would otherwise shift every following digit into the wrong cell.
            IdentifiantUnique = OnlyDigits(Get(ArretTravailKeys.IdentifiantUnique)),
            PatientFirstName = first,
            PatientLastName = last,
            // The certificate's own « Mr (Mme) » line takes the full name — it is one dotted line, not two fields.
            PatientFullName = string.Join(' ', new[] { first, last }.Where(s => !string.IsNullOrWhiteSpace(s))),
            PatientDateOfBirth = Get(ArretTravailKeys.PatientDateOfBirth),
            PatientAddress = Get(ArretTravailKeys.PatientAddress),
            PostalCode = OnlyDigits(Get(ArretTravailKeys.PostalCode)),
            PatientPhone = Get(ArretTravailKeys.PatientPhone),

            DoctorName = Get(ArretTravailKeys.DoctorName) is { Length: > 0 } doctor ? doctor : data.DoctorName ?? string.Empty,
            DoctorQuality = Get(ArretTravailKeys.DoctorQuality),
            City = Get(ArretTravailKeys.City),
            DoctorCodeConventionnel = Get(ArretTravailKeys.DoctorCodeConventionnel),
            DoctorOrdreNumber = Get(ArretTravailKeys.DoctorOrdreNumber),

            Days = Get(ArretTravailKeys.Days),
            FromDate = Get(ArretTravailKeys.FromDate),

            // « Sorties autorisées » is ticked only when both hours are present: the box and the two hour slots are
            // one statement, and half of it is worse than none.
            OutingsAllowed = !string.IsNullOrWhiteSpace(from) && !string.IsNullOrWhiteSpace(to),
            OutingsFrom = from,
            OutingsTo = to,

            TraumaCause = ParseTraumaCause(Get(ArretTravailKeys.TraumaCause)),
            Hospitalised = ParseTriState(Get(ArretTravailKeys.Hospitalised)),

            SignPlace = Get(ArretTravailKeys.SignPlace),
            SignDate = Get(ArretTravailKeys.SignDate)
        };
    }

    /// <summary>
    /// An unrecognised value reads as « none », not as a throw: the renderer's contract is that a bad field costs a
    /// blank box rather than a failed PDF, and the write path refuses the values that matter
    /// (<c>ArretTravailValidation</c>).
    /// </summary>
    private static ArretTravailTraumaCause ParseTraumaCause(string value) => value switch
    {
        ArretTravailKeys.TraumaVoiePublique => ArretTravailTraumaCause.VoiePublique,
        ArretTravailKeys.TraumaDomestique => ArretTravailTraumaCause.Domestique,
        ArretTravailKeys.TraumaViolence => ArretTravailTraumaCause.Violence,
        _ => ArretTravailTraumaCause.None
    };

    private static bool? ParseTriState(string value) => value switch
    {
        "true" or "oui" or "Oui" => true,
        "false" or "non" or "Non" => false,
        _ => null
    };

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
}
