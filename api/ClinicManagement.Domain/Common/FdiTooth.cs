namespace ClinicManagement.Domain.Common;

/// <summary>
/// FDI tooth-notation helpers shared by the odontogram and treatment-plan items.
/// Adult teeth: 11–18, 21–28, 31–38, 41–48. Child teeth: 51–55, 61–65, 71–75, 81–85.
/// (Mirrors the validation historically embedded in <c>DentalRecordTooth</c>.)
/// </summary>
public static class FdiTooth
{
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
