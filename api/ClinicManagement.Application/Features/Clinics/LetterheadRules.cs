namespace ClinicManagement.Application.Features.Clinics;

/// <summary>
/// What a letterhead band must be to print well, and the refusal for each way it can fail.
/// <para>
/// A band is the top (or bottom) of the cabinet's real paper, mapped at true scale: its pixel width becomes the
/// page's 210 mm. So its sharpness is its width, and its height is its share of the page.
/// </para>
/// <para>⚠️ The browser tool applies the same numbers before uploading; the server repeats them because it is the
/// only check a hand-crafted request cannot skip. The sentence and its code live together, as in
/// <c>SupplierRefusals</c>.</para>
/// </summary>
public static class LetterheadRules
{
    public const double A4WidthMm = 210;
    public const double A4HeightMm = 297;

    /// <summary>
    /// 150 dpi across 210 mm. Below this a stored band prints pixelated. The browser tool always sends 2 480 px —
    /// it enlarges a small source smoothly and warns rather than refusing — so only a hand-made request meets this.
    /// </summary>
    public const int MinWidthPx = 1240;

    /// <summary>The entête may take at most a third of the page, or the ordonnance has nowhere to go.</summary>
    public const double MaxHeaderHeightMm = A4HeightMm / 3;

    /// <summary>The pied de page may take at most a sixth.</summary>
    public const double MaxFooterHeightMm = A4HeightMm / 6;

    public const string UnreadableCode = "letterhead_unreadable";
    public const string TooSmallCode = "letterhead_too_small";
    public const string HeaderTooTallCode = "letterhead_header_too_tall";
    public const string FooterTooTallCode = "letterhead_footer_too_tall";

    public const string AdminOnly = "Seul un administrateur peut modifier l'en-tête des documents.";

    public const string Unreadable = "Ce fichier n'est pas une image PNG lisible.";

    public static string TooSmall(int widthPx) =>
        $"Image trop petite ({widthPx} px de large) : elle serait floue à l'impression. " +
        "Importez le PDF de votre imprimeur ou un scan à 300 dpi.";

    public const string HeaderTooTall =
        "L'en-tête occupe plus d'un tiers de la page. Remontez la ligne « Fin de l'en-tête ».";

    public const string FooterTooTall =
        "Le pied de page occupe plus d'un sixième de la page. Descendez la ligne « Début du pied de page ».";

    /// <summary>The printed height of a band, in millimetres, once its width is mapped to the page's.</summary>
    public static double PrintedHeightMm(int widthPx, int heightPx) => heightPx * A4WidthMm / widthPx;
}
