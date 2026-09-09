using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// Hardening pass — cross-clinic isolation (AC-1) and the « Tabac » tri-state for the patient handlers.
/// Mirrors the Stock handler tests' shape (Moq + xUnit Assert).
/// </summary>
public static class PatientTestData
{
    public static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    public static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static Patient Patient(Guid clinicId, TobaccoUse? tobacco = null)
    {
        var patient = new Patient(
            Guid.NewGuid(),
            clinicId,
            "Jean",
            "Dupont",
            new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "M",
            new Email("jean.dupont@example.com"),
            new PhoneNumber("+21620123456"),
            address: null);

        if (tobacco is not null)
        {
            patient.UpdateTobaccoUse(tobacco);
        }

        return patient;
    }
}

public class UpdatePatientCommandHandlerTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private UpdatePatientCommandHandler Handler() => new(_patients.Object, _clinicResolver.Object, _uow.Object, _clinicContext.Object);

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

    /// <summary>
    /// ⚠️ The regression the retired insurance block WAS: an omitted key must leave the stored answer alone.
    ///
    /// <para>`UpdateInsuranceInfo(null)` sat in an `else` branch, so every update that did not echo the block back
    /// wiped the patient's insurer — latent only because one caller always echoed it. « Tabac » is on the
    /// `Specified` pattern so the same shape cannot recur, and this is the test that says so.</para>
    /// </summary>
    [Fact]
    public async Task Handle_Should_Leave_Tobacco_Alone_When_Key_Omitted()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(
            PatientTestData.ClinicId,
            new TobaccoUse(SmokingStatus.Smoker, 20, TobaccoUnit.Cigarettes));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var result = await Handler().Handle(new UpdatePatientCommand { Id = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(patient.TobaccoUse);
        Assert.Equal(SmokingStatus.Smoker, patient.TobaccoUse!.Status);
        Assert.Equal(20, patient.TobaccoUse.PerDay);
    }

    /// <summary>An explicit null un-records the answer — the other half of the tri-state.</summary>
    [Fact]
    public async Task Handle_Should_Clear_Tobacco_When_Explicitly_Null()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(
            PatientTestData.ClinicId,
            new TobaccoUse(SmokingStatus.Smoker, 20, TobaccoUnit.Cigarettes));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var result = await Handler().Handle(
            new UpdatePatientCommand { Id = patient.Id, TobaccoUse = null }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.TobaccoUse);
        Assert.Null(patient.TobaccoUse);
    }

    [Fact]
    public async Task Handle_Should_Set_Tobacco_When_Provided()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand
        {
            Id = patient.Id,
            TobaccoUse = new TobaccoUseDto { Status = "Smoker", PerDay = 1, Unit = "Packs" },
        };
        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Smoker", result.Value!.TobaccoUse!.Status);
        Assert.Equal("Packs", result.Value!.TobaccoUse!.Unit);
        Assert.Equal(TobaccoUnit.Packs, patient.TobaccoUse!.Unit);
    }

    /// <summary>
    /// « Motif de consultation » follows `Notes`' convention: present sets, present-but-blank clears, omitted
    /// leaves alone.
    /// </summary>
    [Fact]
    public async Task Handle_Should_Set_And_Clear_The_Consultation_Reason()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        await Handler().Handle(
            new UpdatePatientCommand { Id = patient.Id, ConsultationReason = "  Douleur 36  " },
            CancellationToken.None);
        Assert.Equal("Douleur 36", patient.ConsultationReason);

        await Handler().Handle(new UpdatePatientCommand { Id = patient.Id }, CancellationToken.None);
        Assert.Equal("Douleur 36", patient.ConsultationReason);

        await Handler().Handle(
            new UpdatePatientCommand { Id = patient.Id, ConsultationReason = "" }, CancellationToken.None);
        Assert.Null(patient.ConsultationReason);
    }

    /// <summary>
    /// ⚠️ The defect this whole change was asked for: « Sfax » and nothing else.
    ///
    /// <para>This path called <c>new Address(...)</c> with no guard, so a partial address threw
    /// <see cref="ArgumentException"/> into the handler's catch-all and came back as the generic error, losing
    /// the save. Its create-path twin dropped the same input silently — hence a test on each.</para>
    /// </summary>
    [Theory]
    [InlineData("12 rue de Marseille", null, null, null)]
    [InlineData(null, "Sfax", null, null)]
    [InlineData(null, null, "Sfax", null)]
    [InlineData(null, null, null, "3000")]
    public async Task Handle_Should_Store_A_Partial_Address(string? street, string? city, string? state, string? zip)
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand
        {
            Id = patient.Id,
            Address = new AddressDto { Street = street, City = city, State = state, ZipCode = zip },
        };
        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(patient.Address);
        Assert.Equal(street, patient.Address!.Street);
        Assert.Equal(city, patient.Address.City);
        Assert.Equal(state, patient.Address.State);
        Assert.Equal(zip, patient.Address.ZipCode);
    }

    /// <summary>An all-blank block still clears — « vider l'adresse » must stay reachable.</summary>
    [Fact]
    public async Task Handle_Should_Clear_An_Address_Whose_Every_Box_Is_Blank()
    {
        Authenticated();
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        patient.UpdatePersonalInfo(
            patient.FirstName, patient.LastName, patient.DateOfBirth, patient.Gender, patient.Email,
            patient.PhoneNumber, Address.OfAny("12 rue de Marseille", "Tunis", "Tunis", "1000"));
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var command = new UpdatePatientCommand
        {
            Id = patient.Id,
            Address = new AddressDto { Street = "  ", City = "", State = null, ZipCode = null },
        };
        var result = await Handler().Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(patient.Address);
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
    // The fiche history's « encaissé sur le traitement » read-back. Stubbed to an empty set: this class is about
    // the tenant guard, and an unstubbed collection-returning mock hands back NULL, which the handler's
    // catch-all would convert into a French business failure — the success assertion would then fail with a
    // message pointing nowhere near the missing stub.
    private readonly Mock<ITreatmentPlanRepository> _plans = new();

    /// <summary>
    /// The séance-history « quelle ordonnance ? » read-back. Stubbed to an empty set for exactly the reason
    /// spelled out above <c>_plans</c>: an unstubbed collection-returning mock hands back null here, and the
    /// handler's catch-all would turn the resulting dereference into a French business failure whose message
    /// says nothing about a missing stub.
    /// </summary>
    private readonly Mock<IMedicalDocumentRepository> _medicalDocuments = new();

    private GetDentalRecordsQueryHandler Handler()
    {
        _plans.Setup(r => r.GetCollectedByDentalRecordAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DentalRecordCollectedRow>());
        _medicalDocuments.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<MedicalDocument>());
        return new(
            _dentalRecords.Object, _patients.Object, _plans.Object, _medicalDocuments.Object,
            _clinicResolver.Object);
    }

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
