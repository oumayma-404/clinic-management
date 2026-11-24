using ClinicManagement.Application.Common;
using ClinicManagement.Application.Common.Csv;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Application.Features.Patients.Commands;
using ClinicManagement.Application.Features.Patients.Import;
using ClinicManagement.Domain.Entities;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// A patient brought over from a previous software keeps the day their file was opened, instead of being counted as
/// a « nouveau patient » of the import month. One rule (<see cref="PatientRegistrationDate"/>), two doors: every
/// creation (<see cref="PatientFromRequest.Build"/>, which the import script reaches) and the CSV import's dry run.
/// </summary>
public class PatientRegistrationDateTests
{
    private static readonly Guid ClinicId = Guid.Parse("7d0e7c1a-3b55-4b0e-9f43-1c1d2e3f4a5b");

    [Fact]
    public void A_File_Opening_Day_Moves_The_Creation_Date_Back_To_Noon_In_Tunis()
    {
        var built = PatientFromRequest.Build(
            new CreatePatientCommand { FirstName = "Mohamed", LastName = "Ben Salah", RegisteredOn = new DateTime(2015, 3, 12) },
            ClinicId);

        Assert.True(built.IsSuccess, built.Error);
        // Noon in Tunis (UTC+1) = 11:00 UTC on the SAME day: a reader of the bare UTC date and a clinic-local reader
        // both say 12/03/2015. Tunis midnight would be 23:00 UTC on the 11th.
        Assert.Equal(new DateTime(2015, 3, 12, 11, 0, 0, DateTimeKind.Utc), built.Value!.CreatedAt);
        Assert.Equal(new DateTime(2015, 3, 12), ClinicClock.ToClinicLocal(built.Value.CreatedAt).Date);
    }

    [Fact]
    public void No_File_Opening_Day_Keeps_The_Creation_Date_At_Now()
    {
        var before = DateTime.UtcNow;
        var built = PatientFromRequest.Build(new CreatePatientCommand { FirstName = "Amira", LastName = "Trabelsi" }, ClinicId);

        Assert.True(built.IsSuccess, built.Error);
        Assert.InRange(built.Value!.CreatedAt, before, DateTime.UtcNow);
    }

    [Fact]
    public void Today_Never_Stores_An_Instant_Still_To_Come()
    {
        var now = new DateTime(2026, 11, 24, 8, 0, 0, DateTimeKind.Utc); // 09:00 in Tunis, before noon
        Assert.Equal(now, PatientRegistrationDate.InstantFor(new DateTime(2026, 11, 24), now));
    }

    [Theory]
    [InlineData(1)]   // tomorrow
    [InlineData(400)] // next year
    public void A_Day_In_The_Future_Is_Refused_On_Every_Door(int daysAhead)
    {
        var day = ClinicClock.ClinicToday().AddDays(daysAhead);
        var built = PatientFromRequest.Build(
            new CreatePatientCommand { FirstName = "Sami", LastName = "Gharbi", RegisteredOn = day }, ClinicId);

        Assert.True(built.IsFailure);
        Assert.Contains("dans le futur", built.Error);
    }

    [Fact]
    public void A_Day_Before_The_Birth_Is_Refused_And_Names_Both_Dates()
    {
        var built = PatientFromRequest.Build(
            new CreatePatientCommand
            {
                FirstName = "Leila", LastName = "Ben Amor",
                DateOfBirth = new DateTime(1992, 11, 5), RegisteredOn = new DateTime(1990, 1, 1),
            },
            ClinicId);

        Assert.True(built.IsFailure);
        Assert.Equal(
            "La date de création du dossier (01/01/1990) précède la date de naissance (05/11/1992).", built.Error);
    }

    [Fact]
    public void A_Day_Before_1900_Is_Refused()
    {
        Assert.NotNull(PatientRegistrationDate.Refusal(new DateTime(1899, 12, 31), null, new DateTime(2026, 11, 24)));
        Assert.Null(PatientRegistrationDate.Refusal(new DateTime(1900, 1, 1), null, new DateTime(2026, 11, 24)));
    }

    [Fact]
    public void The_Domain_Only_Ever_Moves_The_Date_Earlier()
    {
        var patient = new Patient(Guid.NewGuid(), ClinicId, "Mohamed", "Ben Salah", null, "Unknown");
        var created = patient.CreatedAt;

        patient.RecordOriginalRegistration(created.AddDays(3));
        Assert.Equal(created, patient.CreatedAt);

        var earlier = created.AddYears(-10);
        patient.RecordOriginalRegistration(earlier);
        Assert.Equal(earlier, patient.CreatedAt);
    }

    // ---------------------------------------------------------------- the CSV import's door

    private static PatientImportRowRead ReadRow(string registered, string dateOfBirth = "")
    {
        var cells = new List<string> { "Ben Salah", "Mohamed", dateOfBirth, registered };
        var mapping = new Dictionary<PatientImportField, int>
        {
            [PatientImportField.LastName] = 0,
            [PatientImportField.FirstName] = 1,
            [PatientImportField.DateOfBirth] = 2,
            [PatientImportField.RegisteredOn] = 3,
        };
        return PatientImportRowReader.Read(new CsvRow(2, cells), mapping);
    }

    [Fact]
    public void The_Csv_Import_Carries_The_File_Opening_Day_To_The_Command()
    {
        var read = ReadRow("12/03/2015", dateOfBirth: "12/03/1985");

        Assert.Empty(read.Errors);
        Assert.Equal(new DateTime(2015, 3, 12), read.Command!.RegisteredOn);
    }

    [Fact]
    public void The_Csv_Dry_Run_Refuses_What_The_Save_Would_Refuse()
    {
        var beforeBirth = ReadRow("01/01/1980", dateOfBirth: "12/03/1985");
        Assert.Null(beforeBirth.Command);
        Assert.Contains(beforeBirth.Errors, e => e.Contains("précède la date de naissance"));

        var unreadable = ReadRow("mars 2015");
        Assert.Null(unreadable.Command);
        Assert.Contains(unreadable.Errors, e => e.StartsWith("Date de création illisible"));
    }

    [Fact]
    public void An_Empty_Cell_Is_Not_A_Refusal()
    {
        var read = ReadRow("");

        Assert.Empty(read.Errors);
        Assert.Null(read.Command!.RegisteredOn);
    }

    [Theory]
    [InlineData("Date de création")]
    [InlineData("Créé le")]
    [InlineData("Date d'inscription")]
    [InlineData("Patient depuis")]
    public void A_Previous_Softwares_Heading_Is_Recognised(string heading)
    {
        var mapping = PatientImportFields.Detect(new[] { "Nom", "Prénom", "Date de naissance", heading });

        Assert.Equal(3, mapping[PatientImportField.RegisteredOn]);
        Assert.Equal(2, mapping[PatientImportField.DateOfBirth]);
    }

    [Fact]
    public void A_File_This_Product_Exported_Re_Imports_With_Its_Real_Dates()
    {
        // Derived from the export itself: whatever heading it writes for the creation date must map back onto it.
        var bytes = ExportTables.Patients(Array.Empty<ClinicManagement.Application.DTOs.PatientDto>()).ToBytes();
        var headers = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿')
            .Split('\n')[0].TrimEnd('\r').Split(';');
        var mapping = PatientImportFields.Detect(headers);

        Assert.True(mapping.ContainsKey(PatientImportField.RegisteredOn),
            "the patient export's creation-date column is not recognised by the import");
        Assert.Equal("Inscrit le", headers[mapping[PatientImportField.RegisteredOn]]);
    }
}
