using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Writing an antécédent must not touch the patient's own row.
///
/// <para>
/// <c>Version</c> maps onto PostgreSQL's <c>xmin</c>, which advances on any UPDATE of the row, so a child write
/// that stamped the parent's <c>UpdatedAt</c> also moved the concurrency token the open patient form was
/// holding. The front end saves a patient by PUTting the patient and then writing each history entry in turn:
/// every entry bumped the token again, the version returned by the PUT was stale before the sequence finished,
/// and the next save was refused with « cet enregistrement a été modifié par quelqu'un d'autre » naming a
/// colleague who did not exist. A sequence that failed partway left the form holding a version no later click
/// could match, until a full page reload.
/// </para>
/// <para>
/// ⚠️ Nothing here touches a database, so these tests cannot see the UPDATE itself. They hold the two things
/// that <i>caused</i> it and that a later edit would plausibly restore: the domain method's stamp, and the
/// handler's explicit <c>UpdateAsync(patient)</c>.
/// </para>
/// </summary>
public class PatientHistoryVersionGuardTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public PatientHistoryVersionGuardTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));
    }

    private Patient TrackedPatient()
    {
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        return patient;
    }

    private PatientMedicalHistory Medical(Patient patient) =>
        new(Guid.NewGuid(), patient.Id, patient.ClinicId, "Diabète", null, null);

    private PatientFamilyHistory Family(Patient patient) =>
        new(Guid.NewGuid(), patient.Id, patient.ClinicId, "Mère", "Diabète", null);

    /// <summary>The patient row is never written — asserted identically for all six handlers.</summary>
    private void AssertPatientRowUntouched() =>
        _patients.Verify(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Never);

    // ── the domain half ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Adding_a_medical_entry_leaves_the_patients_own_timestamp_alone()
    {
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        var before = patient.UpdatedAt;

        patient.AddMedicalHistoryEntry(Medical(patient));

        Assert.Equal(before, patient.UpdatedAt);
        Assert.Single(patient.MedicalHistoryEntries);
    }

    [Fact]
    public void Removing_a_medical_entry_leaves_the_patients_own_timestamp_alone()
    {
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        var entry = Medical(patient);
        patient.AddMedicalHistoryEntry(entry);
        var before = patient.UpdatedAt;

        patient.RemoveMedicalHistoryEntry(entry.Id);

        Assert.Equal(before, patient.UpdatedAt);
        Assert.Empty(patient.MedicalHistoryEntries);
    }

    [Fact]
    public void Adding_a_family_entry_leaves_the_patients_own_timestamp_alone()
    {
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        var before = patient.UpdatedAt;

        patient.AddFamilyHistoryEntry(Family(patient));

        Assert.Equal(before, patient.UpdatedAt);
        Assert.Single(patient.FamilyHistoryEntries);
    }

    [Fact]
    public void Removing_a_family_entry_leaves_the_patients_own_timestamp_alone()
    {
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        var entry = Family(patient);
        patient.AddFamilyHistoryEntry(entry);
        var before = patient.UpdatedAt;

        patient.RemoveFamilyHistoryEntry(entry.Id);

        Assert.Equal(before, patient.UpdatedAt);
        Assert.Empty(patient.FamilyHistoryEntries);
    }

    // ── the handler half ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creating_a_medical_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();

        var result = await new CreatePatientMedicalHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new CreatePatientMedicalHistoryCommand { PatientId = patient.Id, Description = "Diabète" },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Updating_a_medical_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();
        var entry = Medical(patient);
        patient.AddMedicalHistoryEntry(entry);

        var result = await new UpdatePatientMedicalHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new UpdatePatientMedicalHistoryCommand
                {
                    Id = entry.Id,
                    PatientId = patient.Id,
                    Description = "Diabète de type 2",
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
    }

    [Fact]
    public async Task Deleting_a_medical_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();
        var entry = Medical(patient);
        patient.AddMedicalHistoryEntry(entry);

        var result = await new DeletePatientMedicalHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new DeletePatientMedicalHistoryCommand { Id = entry.Id, PatientId = patient.Id },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
        Assert.Empty(patient.MedicalHistoryEntries);
    }

    [Fact]
    public async Task Creating_a_family_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();

        var result = await new CreatePatientFamilyHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new CreatePatientFamilyHistoryCommand
                {
                    PatientId = patient.Id,
                    Relationship = "Mère",
                    Condition = "Diabète",
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Updating_a_family_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();
        var entry = Family(patient);
        patient.AddFamilyHistoryEntry(entry);

        var result = await new UpdatePatientFamilyHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new UpdatePatientFamilyHistoryCommand
                {
                    Id = entry.Id,
                    PatientId = patient.Id,
                    Condition = "Diabète de type 2",
                },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
    }

    [Fact]
    public async Task Deleting_a_family_entry_does_not_write_the_patient_row()
    {
        var patient = TrackedPatient();
        var entry = Family(patient);
        patient.AddFamilyHistoryEntry(entry);

        var result = await new DeletePatientFamilyHistoryCommandHandler(
                _patients.Object, _clinicResolver.Object, _uow.Object)
            .Handle(
                new DeletePatientFamilyHistoryCommand { Id = entry.Id, PatientId = patient.Id },
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertPatientRowUntouched();
        Assert.Empty(patient.FamilyHistoryEntries);
    }
}
