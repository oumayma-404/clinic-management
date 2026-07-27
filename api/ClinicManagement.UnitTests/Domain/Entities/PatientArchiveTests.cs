using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.ValueObjects;

namespace ClinicManagement.UnitTests.Domain.Entities;

/// <summary>
/// [AC-7][AC-9] Patient archiving: the escape hatch that keeps deletion refusable. Archiving hides a patient
/// from every list without destroying anything, and is always reversible.
/// </summary>
public class PatientArchiveTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static Patient NewPatient() => new(
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        ClinicId,
        "Sonia",
        "Bel Hadj",
        new DateTime(1990, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        "Female",
        new Email("sonia@example.tn"),
        new PhoneNumber("20123456"));

    // [AC-7] A new patient is never archived.
    [Fact]
    public void A_New_Patient_Is_Not_Archived()
    {
        var patient = NewPatient();

        Assert.False(patient.IsArchived);
        Assert.Null(patient.ArchivedAt);
        Assert.Null(patient.ArchiveReason);
    }

    // [AC-7] Archiving stamps the flag, the moment and the reason.
    [Fact]
    public void Archiving_Stamps_The_Flag_Reason_And_Moment()
    {
        var patient = NewPatient();

        patient.Archive("Doublon de la fiche 2026-0042");

        Assert.True(patient.IsArchived);
        Assert.NotNull(patient.ArchivedAt);
        Assert.Equal("Doublon de la fiche 2026-0042", patient.ArchiveReason);
    }

    // [AC-7] The reason is optional, and a blank one is stored as absent rather than as an empty string.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_Blank_Reason_Is_Stored_As_None(string? reason)
    {
        var patient = NewPatient();

        patient.Archive(reason);

        Assert.True(patient.IsArchived);
        Assert.Null(patient.ArchiveReason);
    }

    // [AC-7] Re-archiving is a no-op: a double-click must not rewrite the moment the decision was actually
    // taken, nor drop the reason that was given the first time.
    [Fact]
    public void Re_Archiving_Keeps_The_Original_Stamp_And_Reason()
    {
        var patient = NewPatient();
        patient.Archive("Doublon");
        var firstStamp = patient.ArchivedAt;

        patient.Archive("Autre motif");

        Assert.Equal(firstStamp, patient.ArchivedAt);
        Assert.Equal("Doublon", patient.ArchiveReason);
    }

    // [AC-9] Unarchiving restores the patient and clears the archive stamp entirely.
    [Fact]
    public void Unarchiving_Restores_The_Patient_And_Clears_The_Stamp()
    {
        var patient = NewPatient();
        patient.Archive("Doublon");

        patient.Unarchive();

        Assert.False(patient.IsArchived);
        Assert.Null(patient.ArchivedAt);
        Assert.Null(patient.ArchiveReason);
    }

    // [AC-9] Unarchiving a patient who was never archived is a no-op, not an error.
    [Fact]
    public void Unarchiving_A_Live_Patient_Is_A_No_Op()
    {
        var patient = NewPatient();

        patient.Unarchive();

        Assert.False(patient.IsArchived);
    }

    // [AC-7] Archiving destroys nothing — it is a visibility flag, not a delete.
    [Fact]
    public void Archiving_Preserves_Every_Field()
    {
        var patient = NewPatient();

        patient.Archive("Doublon");

        Assert.Equal("Sonia", patient.FirstName);
        Assert.Equal("Bel Hadj", patient.LastName);
        Assert.Equal("sonia@example.tn", patient.Email?.Value);
        Assert.Equal("20123456", patient.PhoneNumber?.Value);
        Assert.Equal(ClinicId, patient.ClinicId);
    }
}
