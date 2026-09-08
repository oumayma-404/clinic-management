using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// French labels for « Tabac » on the server's two text outputs — the patients CSV and the patient dossier.
///
/// <para>The English storage key / French display map convention, applied on the one side that cannot use the
/// client's copy: a CSV and a dossier are read by a human in French, while the stored value stays the enum
/// member's own name. <see cref="ParseStatus"/> is the inverse, so what the export writes the import reads —
/// the round trip <c>PatientImportFields</c> exists to hold.</para>
///
/// <para>⚠️ An unanswered block is an <b>empty string</b>, never « Non-fumeur ». A placeholder in an export
/// re-imports as data, which is precisely why the four contact sentinels were retired.</para>
/// </summary>
public static class TobaccoLabels
{
    public const string NonSmoker = "Non-fumeur";
    public const string Smoker = "Fumeur";
    public const string FormerSmoker = "Ancien fumeur";

    public const string Cigarettes = "cigarettes";
    public const string Packs = "paquets";

    /// <summary>« Fumeur » for a stored <c>Smoker</c>; empty when nobody has answered.</summary>
    public static string Status(string? storedStatus) =>
        Enum.TryParse<SmokingStatus>(storedStatus, ignoreCase: true, out var status) && Enum.IsDefined(status)
            ? Status(status)
            : string.Empty;

    public static string Status(SmokingStatus status) => status switch
    {
        SmokingStatus.NonSmoker => NonSmoker,
        SmokingStatus.Smoker => Smoker,
        SmokingStatus.FormerSmoker => FormerSmoker,
        _ => string.Empty,
    };

    /// <summary>« 20 cigarettes », or empty when there is no figure.</summary>
    public static string PerDay(TobaccoUseDto? tobacco)
    {
        var unit = Enum.TryParse<TobaccoUnit>(tobacco?.Unit, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : (TobaccoUnit?)null;

        return PerDay(tobacco?.PerDay, unit);
    }

    /// <inheritdoc cref="PerDay(TobaccoUseDto?)"/>
    public static string PerDay(TobaccoUse? tobacco) => PerDay(tobacco?.PerDay, tobacco?.Unit);

    private static string PerDay(int? perDay, TobaccoUnit? unit) =>
        perDay is { } quantity
            ? $"{quantity} {(unit == TobaccoUnit.Packs ? Packs : Cigarettes)}"
            : string.Empty;

    /// <summary>
    /// The inverse of <see cref="Status(SmokingStatus)"/>, accepting both the French label an export wrote and
    /// the English member name, so a file edited by hand and one round-tripped both read.
    /// </summary>
    public static SmokingStatus? ParseStatus(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (Enum.TryParse<SmokingStatus>(trimmed, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        // Accent- and case-tolerant on the French side: « ancien fumeur » and « Ancien Fumeur » are the same answer.
        return trimmed.ToLowerInvariant() switch
        {
            "non-fumeur" or "non fumeur" or "nonfumeur" => SmokingStatus.NonSmoker,
            "fumeur" => SmokingStatus.Smoker,
            "ancien fumeur" or "ancien-fumeur" or "ex-fumeur" or "ex fumeur" => SmokingStatus.FormerSmoker,
            _ => null,
        };
    }

    /// <summary>
    /// « 20 cigarettes », « 1 paquet », « 20 » — the quantity and, when named, the unit. Returns nulls when the
    /// cell holds no number, because « fumeur, quantité non dite » is a real answer and not a bad row.
    /// </summary>
    public static (int? PerDay, TobaccoUnit? Unit) ParsePerDay(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return (null, null);
        }

        var digits = new string(trimmed.TakeWhile(char.IsDigit).ToArray());
        if (digits.Length == 0 || !int.TryParse(digits, out var quantity))
        {
            return (null, null);
        }

        var lowered = trimmed.ToLowerInvariant();
        TobaccoUnit? unit = lowered.Contains("paquet") ? TobaccoUnit.Packs
            : lowered.Contains("cigarette") ? TobaccoUnit.Cigarettes
            : null;

        return (quantity, unit);
    }
}
