using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Import;
using ClinicManagement.Application.Features.Patients.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// <c>features/old-software-history</c> — what a cabinet brings from its previous software gets a place: three new
/// patient fields (Profession, Situation familiale, Pays d'origine) and a read-only « Ancien logiciel » history.
/// </summary>
public class OldSoftwarePatientFieldsTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClinicContext> _clinicContext = new();

    private UpdatePatientCommandHandler Handler() => new(_patients.Object, _clinicResolver.Object, _uow.Object, _clinicContext.Object);

    private Patient Stored()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        patient.SetOccupation("Enseignante");
        patient.SetMaritalStatus("Marié(e)");
        patient.SetCountryOfOrigin("Guinée");
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        return patient;
    }

    // The tri-state every patient field follows: an ordinary save that omits the keys must not erase them.
    [Fact]
    public async Task An_Update_That_Omits_The_Three_Keys_Leaves_Them_Alone()
    {
        var patient = Stored();

        var result = await Handler().Handle(new UpdatePatientCommand { Id = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Enseignante", patient.Occupation);
        Assert.Equal("Marié(e)", patient.MaritalStatus);
        Assert.Equal("Guinée", patient.CountryOfOrigin);
        Assert.Equal("Guinée", result.Value!.CountryOfOrigin);
    }

    [Fact]
    public async Task A_Blank_Value_Clears_And_A_Value_Sets()
    {
        var patient = Stored();

        var result = await Handler().Handle(new UpdatePatientCommand
        {
            Id = patient.Id, Occupation = "  ", MaritalStatus = "Célibataire", CountryOfOrigin = "",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(patient.Occupation);
        Assert.Equal("Célibataire", patient.MaritalStatus);
        Assert.Null(patient.CountryOfOrigin);
    }

    [Fact]
    public void A_New_Patient_Carries_The_Three_Fields()
    {
        var built = PatientFromRequest.Build(new CreatePatientCommand
        {
            FirstName = "Horma", LastName = "Lemina",
            Occupation = " Commerçante ", MaritalStatus = "Marie", CountryOfOrigin = "Guinée",
        }, PatientTestData.ClinicId);

        Assert.True(built.IsSuccess);
        Assert.Equal("Commerçante", built.Value!.Occupation);
        // An imported word outside the form's four is kept as written, never guessed.
        Assert.Equal("Marie", built.Value.MaritalStatus);
        Assert.Equal("Guinée", built.Value.CountryOfOrigin);
    }

    // Data that leaves as CSV must come back in: each new export column maps onto its own import field.
    [Theory]
    [InlineData(PatientImportField.Occupation, "Profession")]
    [InlineData(PatientImportField.MaritalStatus, "Situation familiale")]
    [InlineData(PatientImportField.CountryOfOrigin, "Pays d'origine")]
    public void The_Exported_Column_Re_Imports_Onto_Its_Field(PatientImportField field, string heading)
    {
        var bytes = ExportTables.Patients(Array.Empty<PatientDto>()).ToBytes();
        var headers = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿')
            .Split('\n')[0].TrimEnd('\r').Split(';');
        var mapping = PatientImportFields.Detect(headers);

        Assert.True(mapping.ContainsKey(field), $"the export's « {heading} » column is not recognised by the import");
        Assert.Equal(heading, headers[mapping[field]]);
    }

    // « Pays d'origine » is not the address: « Pays » alone must not be claimed by it.
    [Fact]
    public void Nationality_Is_Not_Read_From_An_Address_Country_Column()
    {
        var mapping = PatientImportFields.Detect(new[] { "Nom", "Prénom", "Nationalité" });

        Assert.Equal(2, mapping[PatientImportField.CountryOfOrigin]);
    }
}

public class AddPatientLegacyHistoryCommandTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientLegacyHistoryRepository> _history = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly List<PatientLegacyHistoryEntry> _stored = new();

    public AddPatientLegacyHistoryCommandTests()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));
        _history.Setup(h => h.GetByPatientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _stored.ToList());
        _history.Setup(h => h.AddRangeAsync(It.IsAny<IEnumerable<PatientLegacyHistoryEntry>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<PatientLegacyHistoryEntry>, CancellationToken>((e, _) => _stored.AddRange(e))
            .Returns(Task.CompletedTask);
        _uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
    }

    private AddPatientLegacyHistoryCommandHandler Handler() => new(_patients.Object, _history.Object, _clinicResolver.Object, _uow.Object);

    private Patient Patient(Guid clinicId)
    {
        var patient = PatientTestData.Patient(clinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        return patient;
    }

    private static LegacyHistoryEntryInput Line(string title, string category = "Consultation", DateOnly? date = null, string? details = null) =>
        new() { Title = title, Category = category, Date = date ?? new DateOnly(2022, 10, 19), Details = details };

    [Fact]
    public async Task Lines_Are_Stored_On_The_Patients_Clinic_In_The_Order_Given()
    {
        var patient = Patient(PatientTestData.ClinicId);

        var result = await Handler().Handle(new AddPatientLegacyHistoryCommand
        {
            PatientId = patient.Id,
            Entries = { Line("Consultation Dentaire", details: "14 fracturée"), Line("Radiographie Panoramique", "Radio") },
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value);
        Assert.All(_stored, e => Assert.Equal(PatientTestData.ClinicId, e.ClinicId));
        Assert.Equal(new[] { 0, 1 }, _stored.Select(e => e.Position));
        Assert.Equal("14 fracturée", _stored[0].Details);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // Two old records of one person (merged on import) each append; the second keeps its own order after the first.
    [Fact]
    public async Task A_Second_Batch_Continues_After_The_First()
    {
        var patient = Patient(PatientTestData.ClinicId);
        await Handler().Handle(new AddPatientLegacyHistoryCommand { PatientId = patient.Id, Entries = { Line("A"), Line("B") } }, CancellationToken.None);

        await Handler().Handle(new AddPatientLegacyHistoryCommand { PatientId = patient.Id, Entries = { Line("C") } }, CancellationToken.None);

        Assert.Equal(new[] { 0, 1, 2 }, _stored.Select(e => e.Position));
    }

    [Fact]
    public async Task Another_Clinics_Patient_Reads_As_Not_Found_And_Nothing_Is_Written()
    {
        var foreign = Patient(PatientTestData.OtherClinicId);

        var result = await Handler().Handle(new AddPatientLegacyHistoryCommand { PatientId = foreign.Id, Entries = { Line("X") } }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_stored);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // All or nothing: a half-written history would read as the whole of it.
    [Fact]
    public async Task One_Invalid_Line_Refuses_The_Whole_Batch_And_Names_It()
    {
        var patient = Patient(PatientTestData.ClinicId);

        var result = await Handler().Handle(new AddPatientLegacyHistoryCommand
        {
            PatientId = patient.Id,
            Entries = { Line("Bonne ligne"), Line("  ") },
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.StartsWith("Ligne 2", result.Error);
        Assert.Empty(_stored);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_Empty_Or_Oversized_Batch_Is_Refused()
    {
        var patient = Patient(PatientTestData.ClinicId);

        var empty = await Handler().Handle(new AddPatientLegacyHistoryCommand { PatientId = patient.Id }, CancellationToken.None);
        var tooMany = await Handler().Handle(new AddPatientLegacyHistoryCommand
        {
            PatientId = patient.Id,
            Entries = Enumerable.Range(0, AddPatientLegacyHistoryCommand.MaxEntriesPerCall + 1).Select(i => Line($"L{i}")).ToList(),
        }, CancellationToken.None);

        Assert.True(empty.IsFailure);
        Assert.True(tooMany.IsFailure);
        Assert.Empty(_stored);
    }

    // The patient row is not touched: its version is what an open « Modifier » form holds.
    [Fact]
    public async Task The_Patient_Row_Is_Not_Written()
    {
        var patient = Patient(PatientTestData.ClinicId);

        await Handler().Handle(new AddPatientLegacyHistoryCommand { PatientId = patient.Id, Entries = { Line("A") } }, CancellationToken.None);

        _patients.Verify(r => r.UpdateAsync(It.IsAny<Patient>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class GetPatientLegacyHistoryQueryTests
{
    private readonly Mock<IPatientRepository> _patients = new();
    private readonly Mock<IPatientLegacyHistoryRepository> _history = new();
    private readonly Mock<ICurrentClinicResolver> _clinicResolver = new();

    private GetPatientLegacyHistoryQueryHandler Handler() => new(_patients.Object, _history.Object, _clinicResolver.Object);

    [Fact]
    public async Task Another_Clinics_Patient_Reads_As_Not_Found_And_Its_Lines_Are_Never_Read()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));
        var foreign = PatientTestData.Patient(PatientTestData.OtherClinicId);
        _patients.Setup(r => r.GetByIdAsync(foreign.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreign);

        var result = await Handler().Handle(new GetPatientLegacyHistoryQuery { PatientId = foreign.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        _history.Verify(h => h.GetByPatientAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Line_Reads_Back_With_Its_Calendar_Day_And_A_Null_Date_Stays_Null()
    {
        _clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(PatientTestData.ClinicId));
        var patient = PatientTestData.Patient(PatientTestData.ClinicId);
        _patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
        _history.Setup(h => h.GetByPatientAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(new List<PatientLegacyHistoryEntry>
        {
            new(Guid.NewGuid(), patient.Id, patient.ClinicId, new DateOnly(2022, 10, 25), "Ordonnance", "Ordonnance", "GENTAMYCINE 80", 0),
            new(Guid.NewGuid(), patient.Id, patient.ClinicId, null, "Diagnostic", "Diagnostic", null, 1),
        });

        var result = await Handler().Handle(new GetPatientLegacyHistoryQuery { PatientId = patient.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2022-10-25", result.Value![0].Date);
        Assert.Null(result.Value[1].Date);
        Assert.Null(result.Value[1].Details);
    }
}

public class LegacyHistoryDeletionBlockerTests
{
    // A patient holding old-software lines cannot be deleted or merged away; the refusal names them and their tab.
    [Fact]
    public void Lines_From_The_Old_Software_Block_Deletion_And_Point_At_Their_Tab()
    {
        var counts = new PatientLinkedDataCounts(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, LegacyHistoryEntries: 3);

        var blocker = Assert.Single(PatientDeletionBlockers.From(counts));

        Assert.True(counts.Any);
        Assert.Equal("legacy-history", blocker.Tab);
        Assert.Equal("3 éléments de l'ancien logiciel y sont rattachés", PatientDeletionBlockers.DescribeAttached(counts));
    }
}
