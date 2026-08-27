using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Tenant isolation for the clinical record — the four PHI tables that had no by-id isolation test
/// (multi-tenant-cloud US-2; <c>MedicalDocument</c>, <c>PatientFile</c> and <c>PatientFolder</c> are already
/// covered by their own suites).
///
/// <para><b>Why these four need a test the other twenty-one do not.</b> <c>DentalRecord</c>,
/// <c>PatientMedicalHistory</c>, <c>PatientFamilyHistory</c> and <c>ToothState</c> carry <b>no ClinicId column
/// at all</b>, so no query filter is possible and US-2's second layer does nothing for them — before or after
/// this story. Each is a child of <c>Patient</c> and designed to be reached through it, yet each is fetched by
/// its own id by its own controller, with the parent loaded <i>after</i> the child. The per-handler DB-resolved
/// check is therefore the only layer they have, and this is the only place it can be held.
/// <c>features/fix-patient-file-tenant-isolation</c> exists because this exact class already leaked once.</para>
///
/// <para>A mocked repository applies no filter, which is what makes these assertions meaningful: the row handed
/// back belongs to another clinic, exactly as it would look to a handler whose backstop was inactive. Every case
/// asserts the operation fails, reads as « introuvable » rather than « interdit » (no existence disclosure), and
/// saves nothing.</para>
/// </summary>
public class ClinicalRecordTenantIsolationTests
{
    private static readonly Guid CallerClinic = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinic = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTime Visit = new(2026, 3, 4, 9, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Patient _foreignPatient = new(
        Guid.NewGuid(), OtherClinic, "Hédi", "Bouazizi", new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), "Male");

    public ClinicalRecordTenantIsolationTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(CallerClinic));
        _patients.Setup(r => r.GetByIdAsync(_foreignPatient.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_foreignPatient);
    }

    private void AssertRefusedAndNothingSaved(bool isFailure, string? error)
    {
        Assert.True(isFailure);
        Assert.Contains("introuvable", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Fiches de soins (DentalRecord) ----

    [Fact]
    public async Task GetDentalRecords_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var records = new Mock<IDentalRecordRepository>();

        var handler = new GetDentalRecordsQueryHandler(records.Object, _patients.Object, _clinicResolver.Object);

        var result = await handler.Handle(
            new GetDentalRecordsQuery { PatientId = _foreignPatient.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        records.Verify(
            r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never); // Refused before any PHI is read, not filtered afterwards.
    }

    [Fact]
    public async Task DeleteDentalRecord_Refuses_Another_Clinics_Record() // [US-2]
    {
        var record = new DentalRecord(Guid.NewGuid(), _foreignPatient.Id, _foreignPatient.ClinicId, Visit, 0m, isAdultTeeth: true);
        var records = new Mock<IDentalRecordRepository>();
        records.Setup(r => r.GetByIdAsync(record.Id, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var handler = new DeleteDentalRecordCommandHandler(
            records.Object, _patients.Object, new Mock<ITreatmentPlanRepository>().Object,
            new Mock<IInvoiceRepository>().Object, _clinicResolver.Object, _uow.Object,
            NullLogger<DeleteDentalRecordCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeleteDentalRecordCommand { Id = record.Id, PatientId = _foreignPatient.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
        records.Verify(r => r.DeleteAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---- Antécédents médicaux (where an allergy lives) ----

    [Fact]
    public async Task GetMedicalHistory_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var handler = new GetPatientMedicalHistoryQueryHandler(_patients.Object, _clinicResolver.Object);

        var result = await handler.Handle(
            new GetPatientMedicalHistoryQuery { PatientId = _foreignPatient.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task DeleteMedicalHistory_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var handler = new DeletePatientMedicalHistoryCommandHandler(
            _patients.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new DeletePatientMedicalHistoryCommand { Id = Guid.NewGuid(), PatientId = _foreignPatient.Id },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    // ---- Antécédents familiaux ----

    [Fact]
    public async Task GetFamilyHistory_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var handler = new GetPatientFamilyHistoryQueryHandler(_patients.Object, _clinicResolver.Object);

        var result = await handler.Handle(
            new GetPatientFamilyHistoryQuery { PatientId = _foreignPatient.Id }, CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    [Fact]
    public async Task DeleteFamilyHistory_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var handler = new DeletePatientFamilyHistoryCommandHandler(
            _patients.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new DeletePatientFamilyHistoryCommand { Id = Guid.NewGuid(), PatientId = _foreignPatient.Id },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }

    // ---- Odontogramme (ToothState) ----

    [Fact]
    public async Task GetOdontogram_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var teeth = new Mock<IToothStateRepository>();

        var handler = new GetOdontogramQueryHandler(
            _patients.Object, teeth.Object, _clinicResolver.Object,
            NullLogger<GetOdontogramQueryHandler>.Instance);

        var result = await handler.Handle(
            new GetOdontogramQuery { PatientId = _foreignPatient.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        teeth.Verify(r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemoveToothCondition_Refuses_Another_Clinics_Patient() // [US-2]
    {
        var state = new ToothState(
            Guid.NewGuid(), _foreignPatient.Id, _foreignPatient.ClinicId, 11, ToothCondition.Carie, Visit,
            source: ToothStateSource.Diagnosis);
        var teeth = new Mock<IToothStateRepository>();
        teeth.Setup(r => r.GetByIdAsync(state.Id, It.IsAny<CancellationToken>())).ReturnsAsync(state);

        var handler = new RemoveToothConditionCommandHandler(
            _patients.Object, teeth.Object, _clinicResolver.Object, _uow.Object);

        var result = await handler.Handle(
            new RemoveToothConditionCommand { PatientId = _foreignPatient.Id, ToothStateId = state.Id },
            CancellationToken.None);

        AssertRefusedAndNothingSaved(result.IsFailure, result.Error);
    }
}
