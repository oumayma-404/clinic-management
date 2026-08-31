using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ClinicManagement.Application.Features.Patients;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.ValueObjects;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Patients;

/// <summary>
/// One patient's dossier, as the archive a cabinet hands them.
///
/// <para><b>What this serves.</b> The right of access under <i>loi organique 2004-63</i> — and the request a
/// practice fields whenever somebody changes dentist. Nothing in the product could produce it: every export was
/// list-scoped and the whole-clinic archive is the practice's own backup, so the answer was to assemble it by
/// hand from about ten screens.</para>
///
/// <para>⚠️ <b>The load-bearing case is <c>A_file_kept_at_the_cabinet_is_listed_rather_than_dropped</c>.</b> Since
/// the coffre feature a file's original may live on the practice's machine, so the ZIP cannot carry it — and an
/// archive that silently omits a radiograph is worse than one that names it, because the reader has no other way
/// to learn it existed. Every other assertion here would still pass with that file quietly missing.</para>
/// </summary>
public class PatientDossierPackagerTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTime Today = new(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc);

    private static Patient APatient() => new(
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        ClinicId,
        "Sonia",
        "Ben Salah",
        new DateTime(1990, 4, 12, 0, 0, 0, DateTimeKind.Utc),
        "Female",
        new Email("sonia@example.tn"),
        new PhoneNumber("20123456"));

    private static PatientDossier Build(
        IReadOnlyList<PatientFile>? files = null,
        IReadOnlyList<(Guid, string, byte[])>? contents = null,
        IReadOnlySet<Guid>? unreadable = null) =>
        PatientDossierPackager.Build(
            APatient(),
            "Cabinet Ben Salah",
            Array.Empty<Appointment>(),
            Array.Empty<DentalRecord>(),
            Array.Empty<ToothState>(),
            Array.Empty<MedicalDocument>(),
            files ?? Array.Empty<PatientFile>(),
            contents ?? Array.Empty<(Guid, string, byte[])>(),
            unreadable ?? new HashSet<Guid>(),
            Today);

    private static Dictionary<string, string> Entries(PatientDossier dossier)
    {
        using var stream = new MemoryStream(dossier.Content);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        return zip.Entries.ToDictionary(
            e => e.FullName,
            e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            },
            StringComparer.Ordinal);
    }

    [Fact]
    public void The_dossier_carries_the_patients_own_identity_and_a_readable_note()
    {
        var entries = Entries(Build());

        Assert.Contains("identite.csv", entries.Keys);
        Assert.Contains("LISEZ-MOI.txt", entries.Keys);
        Assert.Contains("Ben Salah", entries["identite.csv"]);
        Assert.Contains("Cabinet Ben Salah", entries["LISEZ-MOI.txt"]);
    }

    // A section with no rows is left out rather than shipped empty — an empty « antécédents familiaux » file
    // reads as « we have nothing on file », which is a claim, not an absence.
    [Fact]
    public void An_empty_section_is_omitted_rather_than_shipped_blank()
    {
        var entries = Entries(Build());

        Assert.DoesNotContain("antecedents-familiaux.csv", entries.Keys);
        Assert.DoesNotContain("rendez-vous.csv", entries.Keys);
    }

    // ⚠️ THE case. A file whose original is at the cabinet cannot be enclosed — and must still be named, with
    // its state, or the archive is quietly incomplete.
    [Fact]
    public void A_file_kept_at_the_cabinet_is_listed_rather_than_dropped()
    {
        var atTheCabinet = AFile("radio-panoramique.jpg", 120_000);

        var dossier = Build(files: new[] { atTheCabinet });
        var entries = Entries(dossier);

        Assert.Contains("fichiers.csv", entries.Keys);
        Assert.Contains("radio-panoramique.jpg", entries["fichiers.csv"]);
        Assert.Contains("conservé au cabinet", entries["fichiers.csv"]);
        Assert.Equal(0, dossier.FilesIncluded);
        Assert.Equal(1, dossier.FilesListedOnly);

        // And the reader is told in prose, not only in a spreadsheet column.
        Assert.Contains("IMPORTANT", entries["LISEZ-MOI.txt"]);
        Assert.Contains("ne sont pas joints", entries["LISEZ-MOI.txt"]);
    }

    [Fact]
    public void A_file_on_the_server_is_enclosed_and_marked_as_joined()
    {
        var file = AFile("scanner.png", 2048);
        var bytes = new byte[] { 1, 2, 3, 4 };

        var dossier = Build(
            files: new[] { file },
            contents: new[] { (file.Id, "scanner.png", bytes) });
        var entries = Entries(dossier);

        Assert.Contains("fichiers/scanner.png", entries.Keys);
        Assert.Contains("Joint", entries["fichiers.csv"]);
        Assert.Equal(1, dossier.FilesIncluded);
        Assert.Equal(0, dossier.FilesListedOnly);
        Assert.DoesNotContain("IMPORTANT", entries["LISEZ-MOI.txt"]);
    }

    // Two files may legitimately share a name. A ZIP with duplicate entry names is broken in some readers and
    // silently drops one in others — either way the patient loses a radiograph.
    [Fact]
    public void Two_files_with_the_same_name_both_survive()
    {
        var first = AFile("radio.jpg", 10);
        var second = AFile("radio.jpg", 10);

        var dossier = Build(
            files: new[] { first, second },
            contents: new[] { (first.Id, "radio.jpg", new byte[] { 1 }), (second.Id, "radio-2.jpg", new byte[] { 2 }) });
        var entries = Entries(dossier);

        Assert.Contains("fichiers/radio.jpg", entries.Keys);
        Assert.Contains("fichiers/radio-2.jpg", entries.Keys);
        Assert.Equal(2, dossier.FilesIncluded);
    }

    [Fact]
    public void The_archive_is_named_for_the_patient_and_the_clinics_own_day()
    {
        Assert.Equal("dossier-sonia-ben-salah-2026-08-31.zip", Build().FileName);
    }

    /// <summary>
    /// ⚠️ <b>A file the server could not READ is not a file kept at the cabinet</b>, and the first version of
    /// this packager said it was. Downloading a real dossier reported four files as « conservé au cabinet »
    /// which were <c>Residency = Hosted</c> with a storage key all along — the objects were simply missing, the
    /// fetch threw, and the catch labelled them with an assertion about <i>where the file is</i>. That sends a
    /// patient to their cabinet for something the cabinet does not have.
    /// </summary>
    [Fact]
    public void A_file_the_server_could_not_read_is_not_reported_as_kept_at_the_cabinet()
    {
        var broken = AFile("radio-illisible.jpg", 50_000);

        var dossier = Build(
            files: new[] { broken },
            unreadable: new HashSet<Guid> { broken.Id });
        var entries = Entries(dossier);

        Assert.Contains("n'a pas pu être lu", entries["fichiers.csv"]);
        Assert.DoesNotContain("conservé au cabinet", entries["fichiers.csv"]);

        // And the reader is told it is a fault to report, not an errand to run.
        Assert.Contains("Signalez-le à votre cabinet", entries["LISEZ-MOI.txt"]);
    }

    /// <summary>
    /// The README is the one file here written to be read first, by a patient, in French — and Notepad on
    /// Windows reads a BOM-less UTF-8 file in the system codepage, so « enregistrées » arrives as mojibake.
    /// <c>CsvTable</c> documents this at length for the CSVs; the .txt was written without one and a real
    /// export is what showed it.
    /// </summary>
    [Fact]
    public void The_readable_note_carries_a_utf8_bom_like_every_other_file_here()
    {
        using var stream = new MemoryStream(Build().Content);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        using var entry = zip.GetEntry("LISEZ-MOI.txt")!.Open();
        using var buffer = new MemoryStream();
        entry.CopyTo(buffer);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, buffer.ToArray()[..3]);
    }

    // A cabinet with no name recorded is a real state of the data. « Cabinet : » with nothing after it reads as
    // a broken file, so the line goes rather than hanging empty.
    [Fact]
    public void A_cabinet_with_no_name_recorded_does_not_leave_a_dangling_line()
    {
        var dossier = PatientDossierPackager.Build(
            APatient(), string.Empty,
            Array.Empty<Appointment>(), Array.Empty<DentalRecord>(), Array.Empty<ToothState>(),
            Array.Empty<MedicalDocument>(), Array.Empty<PatientFile>(),
            Array.Empty<(Guid, string, byte[])>(), new HashSet<Guid>(), Today);

        Assert.DoesNotContain("Cabinet :", Entries(dossier)["LISEZ-MOI.txt"]);
    }

    private static PatientFile AFile(string name, long size) => new(
        id: Guid.NewGuid(),
        patientId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
        clinicId: ClinicId,
        fileName: name,
        storageKey: $"clinics/{ClinicId}/{Guid.NewGuid()}",
        contentType: "image/jpeg",
        fileSize: size,
        fileType: FileType.Scan);
}
