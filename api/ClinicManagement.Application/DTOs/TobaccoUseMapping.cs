using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// Wire ⇄ domain for « Tabac ». The single place a <see cref="TobaccoUseDto"/> becomes a
/// <see cref="TobaccoUse"/>, so the create path and the update path cannot disagree about what an unanswered
/// block means — which is exactly how the address ended up dropping silently on one and throwing on the other.
/// </summary>
public static class TobaccoUseMapping
{
    /// <summary>
    /// The value object the block describes, or <c>null</c> for « nobody has asked ».
    ///
    /// <para>⚠️ An <b>unrecognised</b> status also yields null rather than a guess. The alternative — defaulting
    /// to <see cref="SmokingStatus.NonSmoker"/> — would turn a client typo into a clinical assertion nobody made,
    /// and no later read could tell it from an answer.</para>
    /// </summary>
    public static TobaccoUse? ToDomain(TobaccoUseDto? dto)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Status))
        {
            return null;
        }

        if (!Enum.TryParse<SmokingStatus>(dto.Status, ignoreCase: true, out var status)
            || !Enum.IsDefined(status))
        {
            return null;
        }

        TobaccoUnit? unit = null;
        if (!string.IsNullOrWhiteSpace(dto.Unit)
            && Enum.TryParse<TobaccoUnit>(dto.Unit, ignoreCase: true, out var parsedUnit)
            && Enum.IsDefined(parsedUnit))
        {
            unit = parsedUnit;
        }

        // The value object itself drops the quantity for a non-smoker and refuses an out-of-range one.
        return new TobaccoUse(status, dto.PerDay, unit);
    }
}
