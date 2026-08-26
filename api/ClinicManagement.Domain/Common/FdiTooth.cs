namespace ClinicManagement.Domain.Common;

/// <summary>
/// FDI tooth-notation helpers shared by the odontogram and treatment-plan items.
/// Adult teeth: 11–18, 21–28, 31–38, 41–48. Child teeth: 51–55, 61–65, 71–75, 81–85.
/// (Mirrors the validation historically embedded in <c>DentalRecordTooth</c>.)
/// </summary>
public static class FdiTooth
{
    /// <summary>
    /// The French refusal for a number that is not an FDI tooth, stated once.
    ///
    /// <para>⚠️ A bon de prothèse used to accept <c>99</c> and print it on the bon, while <c>ab</c> was parsed to
    /// null and dropped in silence — on the one screen whose whole question is « which tooth is this crown for ».
    /// <see cref="IsValid"/> already existed and was wired to the odontogram, the fiche and the devis; the lab
    /// order was the one door that never asked.</para>
    /// </summary>
    public const string NotAToothNumber =
        "Numéro de dent invalide. Utilisez la notation FDI : 11–18, 21–28, 31–38, 41–48 (adulte) ou 51–55, 61–65, 71–75, 81–85 (enfant).";

    /// <summary>The French refusal, or null when the tooth is acceptable. An absent tooth is acceptable.</summary>
    public static string? Refuse(int? toothNumber) =>
        toothNumber.HasValue && !IsValid(toothNumber.Value) ? NotAToothNumber : null;

    public static bool IsValid(int toothNumber) =>
        (toothNumber >= 11 && toothNumber <= 18) ||
        (toothNumber >= 21 && toothNumber <= 28) ||
        (toothNumber >= 31 && toothNumber <= 38) ||
        (toothNumber >= 41 && toothNumber <= 48) ||
        (toothNumber >= 51 && toothNumber <= 55) ||
        (toothNumber >= 61 && toothNumber <= 65) ||
        (toothNumber >= 71 && toothNumber <= 75) ||
        (toothNumber >= 81 && toothNumber <= 85);

    public static bool IsAdult(int toothNumber) =>
        (toothNumber >= 11 && toothNumber <= 18) ||
        (toothNumber >= 21 && toothNumber <= 28) ||
        (toothNumber >= 31 && toothNumber <= 38) ||
        (toothNumber >= 41 && toothNumber <= 48);
}
