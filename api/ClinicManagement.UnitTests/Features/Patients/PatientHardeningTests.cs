using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Hardening pass — cross-clinic isolation (AC-1) and the insurance-clear fix (AC-8) for the patient
/// handlers. Mirrors the Stock handler tests' shape (Moq + xUnit Assert).
/// </summary>
public static class PatientTestData
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static Patient Patient(Guid clinicId, InsuranceInfo? insurance = null) => new(
        Guid.NewGuid(),
        clinicId,
        "Jean",
        "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "M",
        new Email("jean.dupont@example.com"),
        new PhoneNumber("+21620123456"),
        address: null,
        insuranceInfo: insurance);
}

public class UpdatePatientCommandHandlerTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private UpdatePatientCommandHandler Handler() => new(_patients.Object, _clinicResolver.Object, _uow.Object);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));

    // [AC-1] A patient from another clinic reads as "not found" — never leaks/mutates cross-tenant data.
    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Other_Clinic_Patient()
    {
        Authenticated();
        var foreign = PatientTestData.Patient(PatientTestData.OtherClinicId);
        _patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(new UpdatePatientCommand { Id = foreign.Id, FirstName = "Hacked" }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _patients.Verify(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-8] Omitting InsuranceInfo (null) clears the stored insurance.
    [Fact]
    public async Task Handle_Should_Clear_Insurance_When_Omitted()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(
            PatientTestData.ClinicId,
            new InsuranceInfo("CNAM", "POL-123", "GRP-9", null));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var result = await Handler().Handle(new UpdatePatientCommand { Id = patient.Id, InsuranceInfo = null }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.InsuranceInfo);
        Assert.Null(patient.InsuranceInfo);
    }

    // [AC-8] Providing InsuranceInfo still updates it.
    [Fact]
    public async Task Handle_Should_Set_Insurance_When_Provided()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand
        {
            Id = patient.Id,
            InsuranceInfo = new InsuranceInfoDto { Provider = "STAR", PolicyNumber = "P-999" }
        };
        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value!.InsuranceInfo);
        Assert.Equal("STAR", result.Value!.InsuranceInfo!.Provider);
        Assert.Equal("STAR", patient.InsuranceInfo!.Provider);
    }

    // Clinic cannot be resolved (unauthenticated) → failure, nothing persisted.
    [Fact]
    public async Task Handle_Should_Fail_When_Clinic_Not_Resolved()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Failure("User ID not found in token"));

        var result = await Handler().Handle(new UpdatePatientCommand { Id = Guid.NewGuid() }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class CreatePatientMedicalHistoryCommandHandlerTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    private CreatePatientMedicalHistoryCommandHandler Handler() => new(_patients.Object, _clinicResolver.Object, _uow.Object);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));

    // [AC-1] Cannot add medical history to a patient owned by another clinic.
    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Other_Clinic_Patient()
    {
        Authenticated();
        var foreign = PatientTestData.Patient(PatientTestData.OtherClinicId);
        _patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(
            new CreatePatientMedicalHistoryCommand { PatientId = foreign.Id, Description = "Diabetes" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        _patients.Verify(r => r.AddMedicalHistoryEntryAsync(It.IsAny<PatientMedicalHistory>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // Own-clinic patient → the entry is added and persisted.
    [Fact]
    public async Task Handle_Should_Add_Entry_For_Own_Clinic_Patient()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var result = await Handler().Handle(
            new CreatePatientMedicalHistoryCommand { PatientId = patient.Id, Description = "Asthma" },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Asthma", result.Value!.Description);
        _patients.Verify(r => r.AddMedicalHistoryEntryAsync(It.IsAny<PatientMedicalHistory>(), It.IsAny<CancellationToken>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}

public class GetDentalRecordsQueryHandlerTests
{
    private readonly Mock<IDentalRecordRepository> _dentalRecords = new();
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetDentalRecordsQueryHandler Handler() =>
        new(_dentalRecords.Object, _patients.Object, _clinicResolver.Object);

    private void Authenticated() =>
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));

    // [AC-1 / Finding 1] Reading dental records for a patient owned by another clinic reads as "not
    // found" — the child DentalRecord entity is not covered by the global filter, so this is the guard.
    [Fact]
    public async Task Handle_Should_Return_NotFound_For_Other_Clinic_Patient()
    {
        Authenticated();
        var foreign = PatientTestData.Patient(PatientTestData.OtherClinicId);
        _patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(new GetDentalRecordsQuery { PatientId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _dentalRecords.Verify(r => r.GetByPatientIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
