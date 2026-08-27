using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// A patient may have <b>no date of birth</b> (AC-18, D-1, D-2).
///
/// <para>The defect this closes was a substitution, not an omission: <c>PatientFromRequest</c> replaced an absent
/// date with <c>UtcNow.AddYears(-30)</c> so a NOT NULL column would accept the row. That stored a birthday nobody
/// gave us — indistinguishable, afterwards, from one a receptionist typed — and fed it to
/// <see cref="DentitionRules"/>, so every undated walk-in was charted on <b>adult</b> teeth however old they were.
/// A walk-in registered at the desk with nothing but a name is the ordinary case, not a data-quality problem.</para>
///
/// <para>Duplicate matching is covered next door in <c>PatientDuplicateGuardTests</c>, which owns the handler-level
/// D-2 cases; what is here is the construction path and the dentition rule.</para>
/// </summary>
public class NullableDateOfBirthTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static CreatePatientCommand Request(DateTime? dateOfBirth, string? dentition = null) => new()
    {
        FirstName = "Sonia",
        LastName = "Bel Hadj",
        DateOfBirth = dateOfBirth,
        Gender = "F",
        Dentition = dentition,
    };

    // ── The substitution is gone ─────────────────────────────────────────────────────────────────────────────

    [Fact] // [AC-18]
    public void A_Patient_Built_Without_A_Date_Of_Birth_Stores_None()
    {
        var result = PatientFromRequest.Build(Request(dateOfBirth: null), ClinicId);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.DateOfBirth);
    }

    [Fact] // [AC-18]
    public void A_Supplied_Date_Of_Birth_Is_Stored_Verbatim()
    {
        var dob = new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc);

        var result = PatientFromRequest.Build(Request(dob), ClinicId);

        Assert.True(result.IsSuccess);
        Assert.Equal(dob, result.Value!.DateOfBirth);
    }

    /// <summary>
    /// The regression guard proper. « Thirty years ago » is what the old code wrote, and it is the one value that
    /// would still make every other assertion here pass while the defect was back.
    /// </summary>
    [Fact] // [AC-18]
    public void No_Date_Is_Manufactured_From_Todays_Date()
    {
        var result = PatientFromRequest.Build(Request(dateOfBirth: null), ClinicId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.DateOfBirth.HasValue);
        // Belt and braces: not merely "not thirty years ago", but not *any* recent fabrication.
        Assert.DoesNotContain(
            new[] { 30, 0 },
            years => result.Value!.DateOfBirth == DateTime.UtcNow.AddYears(-years));
    }

    // ── Dentition: ask, do not assume ────────────────────────────────────────────────────────────────────────

    [Fact] // [AC-18]
    public void Dentition_Is_Unknown_Without_A_Date_Of_Birth()
    {
        // Null is the answer, not a failure to produce one — the client mirror `dentitionFromBirthdate` has
        // always returned null here, saying « the form must not guess, it must keep asking ».
        Assert.Null(DentitionRules.FromDateOfBirth(null));
    }

    [Theory] // [AC-18]
    [InlineData(5, DentitionType.Child)]
    [InlineData(12, DentitionType.Child)]
    [InlineData(13, DentitionType.Adult)]
    [InlineData(40, DentitionType.Adult)]
    public void Dentition_Still_Follows_Age_When_A_Date_Is_Supplied(int ageYears, DentitionType expected)
    {
        // A fixed "now", so the case cannot pass or fail depending on when the suite runs — the trap
        // `ClinicClockTests` documents.
        var now = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var dob = now.AddYears(-ageYears);

        Assert.Equal(expected, DentitionRules.FromDateOfBirth(dob, now));
    }

    [Fact] // [AC-18]
    public void An_Undated_Patient_Is_Not_Asserted_To_Be_Adult()
    {
        // The entity's own default still exists (the column is NOT NULL), but nothing *claims* it from an absent
        // date — which is what lets the odontogram ask instead of opening on permanent teeth. If `Build` ever
        // starts calling SetDentition here again, the substitution is back in a new form.
        var result = PatientFromRequest.Build(Request(dateOfBirth: null), ClinicId);

        Assert.True(result.IsSuccess);
        Assert.Null(DentitionRules.FromDateOfBirth(result.Value!.DateOfBirth));
    }

    [Fact] // [AC-18]
    public void An_Explicit_Dentition_Wins_Over_An_Absent_Date()
    {
        // A dentist charting a six-year-old walk-in says so on the form; nothing about the missing birthday
        // should override that.
        var result = PatientFromRequest.Build(Request(dateOfBirth: null, dentition: "Child"), ClinicId);

        Assert.True(result.IsSuccess);
        Assert.Equal(DentitionType.Child, result.Value!.Dentition);
    }

    // ── D-1: no backfill, and the entity accepts both states ─────────────────────────────────────────────────

    [Fact] // [D-1]
    public void The_Entity_Accepts_A_Null_Date_Of_Birth()
    {
        var patient = new Patient(
            Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj", null, PatientGender.Female);

        Assert.Null(patient.DateOfBirth);
    }

    [Fact] // [D-1]
    public void An_Existing_Date_Can_Be_Kept_Through_An_Update()
    {
        var dob = new DateTime(1990, 5, 2, 0, 0, 0, DateTimeKind.Utc);
        var patient = new Patient(Guid.NewGuid(), ClinicId, "Sonia", "Bel Hadj", dob, PatientGender.Female);

        // The update command's rule is null-means-unchanged, so a caller omitting the field must not clear a
        // date already on file — the mirror image of the fabrication this feature removed.
        patient.UpdatePersonalInfo("Sonia", "Bel Hadj", patient.DateOfBirth, PatientGender.Female, null, null);

        Assert.Equal(dob, patient.DateOfBirth);
    }

    [Fact] // [AC-21]
    public void Insurance_Accepts_One_Side_And_Refuses_Neither()
    {
        // Adjacent to this feature and easy to regress together: both halves used to be mandatory, which is what
        // made the client pad a missing one with the literal "Unknown".
        Assert.Equal("CNAM", new InsuranceInfo("CNAM", null).Provider);
        Assert.Equal("12345", new InsuranceInfo(null, "12345").PolicyNumber);
        Assert.Throws<ArgumentException>(() => new InsuranceInfo("   ", null));
    }
}
