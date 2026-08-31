using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.DentalActs;

/// <summary>
/// Authoritative, pure CNAM reimbursement estimate (FR-5.5). Estimate = coefficient × VLC × rate, with
/// the July-2021 CNAM dental rates: <b>70% for patients aged 4–18 inclusive, 60% otherwise</b>, based on
/// the patient's age at the <b>care date</b>. Unknown DOB → the non-child (60%) rate. A lettre clé with no
/// VLC value yields <c>null</c> (the estimate is omitted "—", never computed as zero). The result is an
/// indicative, non-contractual figure — never persisted, never printed.
/// </summary>
public static class CnamReimbursementCalculator
{
    public const decimal ChildRate = 0.70m;
    public const decimal AdultRate = 0.60m;
    public const int ChildMinAgeInclusive = 4;
    public const int ChildMaxAgeInclusive = 18;

    /// <summary>The CNAM rate for a given age (child band 4–18 inclusive → 70%, else 60%).</summary>
    public static decimal RateForAge(int ageAtCare) =>
        ageAtCare >= ChildMinAgeInclusive && ageAtCare <= ChildMaxAgeInclusive ? ChildRate : AdultRate;

    /// <summary>The effective rate for a patient: age-based when the DOB is known, else the adult rate.</summary>
    public static decimal RateForPatient(DateTime? dateOfBirth, DateTime careDate) =>
        dateOfBirth.HasValue ? RateForAge(AgeAt(dateOfBirth.Value, careDate)) : AdultRate;

    /// <summary>Full-years age at the care date (not today).</summary>
    public static int AgeAt(DateTime dateOfBirth, DateTime careDate)
    {
        var age = careDate.Year - dateOfBirth.Year;
        if (careDate.Month < dateOfBirth.Month ||
            (careDate.Month == dateOfBirth.Month && careDate.Day < dateOfBirth.Day))
        {
            age--;
        }
        return age;
    }

    /// <summary>
    /// The indicative estimate for a single act, or <c>null</c> when its lettre clé has no VLC value.
    /// </summary>
    public static decimal? Estimate(decimal coefficient, decimal? vlc, DateTime? dateOfBirth, DateTime careDate)
    {
        if (vlc is null || coefficient <= 0)
        {
            return null;
        }

        var rate = RateForPatient(dateOfBirth, careDate);
        return coefficient * vlc.Value * rate;
    }

    /// <summary>
    /// Why <see cref="Estimate"/> returned null, as the enum member's own name — null when it did not.
    /// A missing cotation is reported ahead of a missing valeur: it is the half an admin can actually close.
    /// </summary>
    public static string? UnavailableReason(decimal coefficient, decimal? vlc)
    {
        if (coefficient <= 0)
        {
            return nameof(ReimbursementUnavailability.MissingCoefficient);
        }

        return vlc is null ? nameof(ReimbursementUnavailability.NoLetterValue) : null;
    }
}
