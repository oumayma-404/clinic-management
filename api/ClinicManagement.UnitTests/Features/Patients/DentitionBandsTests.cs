using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Enums;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// The three denture bands — « temporaire » under 6, « mixte » 6 to 12, « définitive » from 13.
///
/// <para>These exist because the field grew a <b>third</b> value, and a third value is where a two-way rule quietly
/// becomes wrong: with two bands there was one boundary and it was hard to get backwards, with three there are two
/// and the middle one can be swallowed by either neighbour without anything failing to compile. Every year from 0 to
/// 20 is asserted individually rather than by sampling around the edges, because a band that collapses does so
/// silently — the form simply pre-selects the wrong arch and the dentist corrects it, or does not.</para>
/// </summary>
public class DentitionBandsTests
{
    [Theory]
    [InlineData(0, DentitionType.Child)]
    [InlineData(1, DentitionType.Child)]
    [InlineData(2, DentitionType.Child)]
    [InlineData(3, DentitionType.Child)]
    [InlineData(4, DentitionType.Child)]
    [InlineData(5, DentitionType.Child)]
    [InlineData(6, DentitionType.Mixed)]
    [InlineData(7, DentitionType.Mixed)]
    [InlineData(8, DentitionType.Mixed)]
    [InlineData(9, DentitionType.Mixed)]
    [InlineData(10, DentitionType.Mixed)]
    [InlineData(11, DentitionType.Mixed)]
    [InlineData(12, DentitionType.Mixed)]
    [InlineData(13, DentitionType.Adult)]
    [InlineData(14, DentitionType.Adult)]
    [InlineData(20, DentitionType.Adult)]
    [InlineData(80, DentitionType.Adult)]
    public void Every_Age_Falls_In_Exactly_One_Band(int age, DentitionType expected)
    {
        Assert.Equal(expected, DentitionRules.FromAgeYears(age));
    }

    /// <summary>
    /// The two boundaries are the constants, not literals that happen to agree with them today.
    /// </summary>
    [Fact]
    public void The_Boundaries_Are_The_Declared_Constants()
    {
        Assert.Equal(DentitionType.Child, DentitionRules.FromAgeYears(DentitionRules.MixedFromAgeYears - 1));
        Assert.Equal(DentitionType.Mixed, DentitionRules.FromAgeYears(DentitionRules.MixedFromAgeYears));
        Assert.Equal(DentitionType.Mixed, DentitionRules.FromAgeYears(DentitionRules.AdultFromAgeYears - 1));
        Assert.Equal(DentitionType.Adult, DentitionRules.FromAgeYears(DentitionRules.AdultFromAgeYears));
    }

    /// <summary>
    /// ⚠️ The mixed band must be non-empty. If the two constants ever met, `FromAgeYears` would still compile and
    /// still answer, and « mixte » would simply become an option no birth date could ever select — a value the form
    /// offers and the derivation never reaches.
    /// </summary>
    [Fact]
    public void The_Mixed_Band_Is_Not_Empty()
    {
        Assert.True(DentitionRules.MixedFromAgeYears < DentitionRules.AdultFromAgeYears);
    }

    /// <summary>
    /// A patient with no date of birth is still « demandez, n'assumez pas » — the third value changes nothing here.
    /// </summary>
    [Fact]
    public void No_Date_Of_Birth_Asserts_Nothing()
    {
        Assert.Null(DentitionRules.FromDateOfBirth(null));
    }

    /// <summary>
    /// A date of birth answers through the same bands. Anchored on a fixed "now" so the test does not change
    /// meaning with the calendar, and dated mid-year so the birthday-not-yet-reached branch is exercised too.
    /// </summary>
    [Theory]
    [InlineData(2023, DentitionType.Child)]  // 3 on the anchor date
    [InlineData(2018, DentitionType.Mixed)]  // 8
    [InlineData(2010, DentitionType.Adult)]  // 16
    public void A_Date_Of_Birth_Answers_Through_The_Same_Bands(int birthYear, DentitionType expected)
    {
        var nowUtc = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
        var dob = new DateTime(birthYear, 3, 14, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, DentitionRules.FromDateOfBirth(dob, nowUtc));
    }

    /// <summary>
    /// ⚠️ <c>Mixed</c> is <b>2</b>. The values are persisted through <c>HasConversion&lt;int&gt;()</c>, so inserting
    /// the new member between the existing two — which is where it belongs in reading order, and therefore where a
    /// later editor will be tempted to put it — would silently repoint every stored row: every « Adulte » patient
    /// would come back as « mixte ». The numbering is the data contract, and it is asserted rather than trusted.
    /// </summary>
    [Fact]
    public void The_Stored_Numbers_Never_Move()
    {
        Assert.Equal(0, (int)DentitionType.Child);
        Assert.Equal(1, (int)DentitionType.Adult);
        Assert.Equal(2, (int)DentitionType.Mixed);
    }

    /// <summary>
    /// The wire carries the enum member's own name, and <c>Mixed</c> has to round-trip like the other two — the
    /// client sends this string and an unparsed value falls back to the age rule, which would look like the form
    /// silently ignoring the dentist's choice.
    /// </summary>
    [Theory]
    [InlineData("Child", DentitionType.Child)]
    [InlineData("Mixed", DentitionType.Mixed)]
    [InlineData("Adult", DentitionType.Adult)]
    [InlineData("mixed", DentitionType.Mixed)]
    public void The_Wire_Value_Round_Trips(string wire, DentitionType expected)
    {
        Assert.Equal(expected, DentitionRules.Parse(wire));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mixte")]
    public void An_Unrecognised_Value_Falls_Back_Rather_Than_Guessing(string? wire)
    {
        Assert.Null(DentitionRules.Parse(wire));
    }
}
