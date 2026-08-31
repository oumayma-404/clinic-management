using ClinicManagement.Application.Features.DentalActs;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.DentalActs;

/// <summary>
/// Reimbursement estimate calculator (FR-5.5): estimate = coefficient × VLC × rate; July-2021 rates
/// (70% ages 4–18 inclusive, 60% otherwise), age at the care date; unknown DOB → non-child; a lettre clé
/// with no VLC → omitted (null), not zero.
/// </summary>
public class CnamReimbursementEstimateTests
{
    private static readonly DateTime CareDate = new(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

    // REIMB-1
    [Fact]
    public void Estimate_Equals_Coefficient_Times_Vlc_Times_Rate() // [FR-5.5]
    {
        // coefficient 10, VLC 1.500, adult rate 0.60 → 9.000
        var estimate = CnamReimbursementCalculator.Estimate(10m, 1.5m, dateOfBirth: null, CareDate);
        Assert.Equal(9.000m, estimate);
    }

    // REIMB-2
    [Theory]
    [InlineData(3, 0.60)]
    [InlineData(4, 0.70)]
    [InlineData(5, 0.70)]
    [InlineData(17, 0.70)]
    [InlineData(18, 0.70)]
    [InlineData(19, 0.60)]
    public void Rate_Is_Child_Band_For_Ages_4_To_18_Inclusive(int age, decimal expectedRate) // [FR-5.5]
    {
        Assert.Equal(expectedRate, CnamReimbursementCalculator.RateForAge(age));

        // And end-to-end via a DOB that makes the patient exactly `age` at the care date.
        var dob = CareDate.AddYears(-age);
        var estimate = CnamReimbursementCalculator.Estimate(1m, 1m, dob, CareDate);
        Assert.Equal(expectedRate, estimate);
    }

    // REIMB-3
    [Fact]
    public void Age_Is_Computed_At_Care_Date_Not_Today() // [FR-5.5]
    {
        // DOB makes the patient 18 at the care date (child band) even though older now.
        var careDate = new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var dob = new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc); // 18 on 2020-06-01

        Assert.Equal(18, CnamReimbursementCalculator.AgeAt(dob, careDate));
        var estimate = CnamReimbursementCalculator.Estimate(10m, 1m, dob, careDate);
        Assert.Equal(7.0m, estimate); // 10 × 1 × 0.70 (child)
    }

    // REIMB-4
    [Fact]
    public void Unknown_Dob_Uses_NonChild_Rate() // [FR-5.5]
    {
        Assert.Equal(CnamReimbursementCalculator.AdultRate, CnamReimbursementCalculator.RateForPatient(null, CareDate));
        var estimate = CnamReimbursementCalculator.Estimate(10m, 2m, dateOfBirth: null, CareDate);
        Assert.Equal(12.0m, estimate); // 10 × 2 × 0.60
    }

    // REIMB-5
    [Fact]
    public void LettreCle_With_No_Vlc_Value_Omits_Estimate() // [edge: missing VLC]
    {
        var estimate = CnamReimbursementCalculator.Estimate(10m, vlc: null, dateOfBirth: null, CareDate);
        Assert.Null(estimate);
    }

    [Fact]
    public void UnavailableReason_Is_Null_When_The_Estimate_Is_Computable()
    {
        Assert.Null(CnamReimbursementCalculator.UnavailableReason(10m, 1.5m));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnavailableReason_Names_A_Missing_Cotation(decimal coefficient)
    {
        Assert.Equal(
            nameof(ReimbursementUnavailability.MissingCoefficient),
            CnamReimbursementCalculator.UnavailableReason(coefficient, 3m));
    }

    [Fact]
    public void UnavailableReason_Names_A_Lettre_Cle_The_Convention_Does_Not_Value()
    {
        Assert.Equal(
            nameof(ReimbursementUnavailability.NoLetterValue),
            CnamReimbursementCalculator.UnavailableReason(10m, vlc: null));
    }

    [Fact]
    public void UnavailableReason_Reports_The_Missing_Cotation_First_When_Both_Are_Absent()
    {
        // The cotation is the half an admin can close in the catalogue; the valeur is not.
        Assert.Equal(
            nameof(ReimbursementUnavailability.MissingCoefficient),
            CnamReimbursementCalculator.UnavailableReason(0m, vlc: null));
    }

    // Boundary birthday: exactly on the birthday counts as the older age.
    [Fact]
    public void AgeAt_On_Birthday_Counts_The_Full_Year()
    {
        var dob = new DateTime(2010, 7, 21, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(16, CnamReimbursementCalculator.AgeAt(dob, CareDate)); // 2026-07-21 → exactly 16
    }
}
