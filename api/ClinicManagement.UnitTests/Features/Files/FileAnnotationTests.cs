using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Files.Commands;
using ClinicManagement.Application.Features.Files.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Files;

/// <summary>
/// Markers dropped on the surface of a 3D model (<c>mesh-interactive-viewer</c>).
///
/// <para>⚠️ <b>What these can fail on that nothing else can.</b> An annotation is the first row in this product
/// hanging off a <i>file</i> rather than off a patient, so its tenant guard is a three-link chain — patient in
/// the caller's clinic, file on that patient, marker on that file — and the middle link is the one no other
/// test exercises: a file id belonging to another patient <i>of the same clinic</i> passes every filter the
/// database applies and is refused only here.</para>
/// </summary>
public class FileAnnotationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Patient Patient(Guid clinicId) => new(
        Guid.NewGuid(), clinicId, "Jean", "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
        new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));

    private static PatientFile File(Guid patientId, Guid clinicId) =>
        new(Guid.NewGuid(), patientId, clinicId, "arcade.stl", "files/arcade.stl", "model/stl", 190_000,
            FileType.Other, null);

    private static PatientFileAnnotation Annotation(Guid fileId, Guid clinicId, string label = "Repère 1") =>
        new(Guid.NewGuid(), fileId, clinicId, 1, 2, 3, 0, 0, 1, label, DateTime.UtcNow, "someone");

    private static Mock<ICurrentClinicResolver> Resolver(Guid clinicId)
    {
        var r = new Mock<ICurrentClinicResolver>();
        r.Setup(x => x.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(clinicId));
        return r;
    }

    private sealed record Bench(
        Mock<IPatientFileAnnotationRepository> Annotations,
        Mock<IPatientFileRepository> Files,
        Mock<IPatientRepository> Patients,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<ICurrentClinicResolver> Resolver,
        Patient Patient,
        PatientFile File);

    private static Bench Setup(Guid patientClinic, Guid callerClinic)
    {
        var patient = Patient(patientClinic);
        var file = File(patient.Id, patientClinic);

        var patients = new Mock<IPatientRepository>();
        patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);

        var files = new Mock<IPatientFileRepository>();
        files.Setup(r => r.GetByIdAsync(file.Id, It.IsAny<CancellationToken>())).ReturnsAsync(file);

        var annotations = new Mock<IPatientFileAnnotationRepository>();
        annotations.Setup(r => r.GetForFileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<PatientFileAnnotation>());

        return new Bench(annotations, files, patients, new Mock<IUnitOfWork>(), Resolver(callerClinic), patient, file);
    }

    private static CreateFileAnnotationCommandHandler CreateHandler(Bench b) =>
        new(b.Annotations.Object, b.Files.Object, b.Patients.Object, b.UnitOfWork.Object, b.Resolver.Object);

    // ── The chain of three ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Marker_Is_Stored_Against_The_File_And_Its_Clinic()
    {
        var b = Setup(ClinicId, ClinicId);
        PatientFileAnnotation? saved = null;
        b.Annotations
            .Setup(r => r.AddAsync(It.IsAny<PatientFileAnnotation>(), It.IsAny<CancellationToken>()))
            .Callback<PatientFileAnnotation, CancellationToken>((a, _) => saved = a);

        var result = await CreateHandler(b).Handle(new CreateFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            X = 12.5,
            Y = -3,
            Z = 0.25,
            NormalX = 0,
            NormalY = 1,
            NormalZ = 0,
            Label = "Limite cervicale 26",
            CreatedBy = "dr.benyoussef",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(saved);
        Assert.Equal(b.File.Id, saved!.PatientFileId);
        // ⚠️ From the FILE, not from the resolver: the file is the row the marker belongs to.
        Assert.Equal(b.File.ClinicId, saved.ClinicId);
        Assert.Equal(12.5, saved.X);
        Assert.Equal("Limite cervicale 26", saved.Label);
        Assert.Equal("dr.benyoussef", saved.CreatedBy);
        b.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_Marker_Is_Refused_On_Another_Clinics_Patient()
    {
        var b = Setup(OtherClinicId, ClinicId);

        var result = await CreateHandler(b).Handle(new CreateFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            Label = "x",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        b.Annotations.Verify(
            r => r.AddAsync(It.IsAny<PatientFileAnnotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ **The middle link, and the one nothing else catches.** Both rows are in the caller's own clinic, so
    /// the tenant filter is satisfied and the database is perfectly happy — the only thing standing between a
    /// caller and writing onto another patient's model is this comparison.
    /// </summary>
    [Fact]
    public async Task A_Marker_Is_Refused_On_A_File_Belonging_To_Another_Patient_Of_The_Same_Clinic()
    {
        var b = Setup(ClinicId, ClinicId);

        var neighbour = Patient(ClinicId);
        var theirFile = File(neighbour.Id, ClinicId);
        b.Files.Setup(r => r.GetByIdAsync(theirFile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(theirFile);

        var result = await CreateHandler(b).Handle(new CreateFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = theirFile.Id,
            Label = "x",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        b.Annotations.Verify(
            r => r.AddAsync(It.IsAny<PatientFileAnnotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Rename_Is_Refused_For_A_Marker_On_Another_File()
    {
        var b = Setup(ClinicId, ClinicId);

        var strayFileId = Guid.NewGuid();
        var stray = Annotation(strayFileId, ClinicId);
        b.Annotations.Setup(r => r.GetByIdAsync(stray.Id, It.IsAny<CancellationToken>())).ReturnsAsync(stray);

        var handler = new RenameFileAnnotationCommandHandler(
            b.Annotations.Object, b.Files.Object, b.Patients.Object, b.UnitOfWork.Object, b.Resolver.Object);

        var result = await handler.Handle(new RenameFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            AnnotationId = stray.Id,
            Label = "volé",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Repère 1", stray.Label);
        b.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── What a rename may and may not move ────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The point is not on the command at all, so this asserts a property of the <b>shape</b> rather than of
    /// a branch: there is no payload a client could send that would relocate somebody else's marker.
    /// </summary>
    [Fact]
    public async Task A_Rename_Moves_The_Label_And_Nothing_Else()
    {
        var b = Setup(ClinicId, ClinicId);
        var marker = Annotation(b.File.Id, ClinicId);
        b.Annotations.Setup(r => r.GetByIdAsync(marker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(marker);

        var handler = new RenameFileAnnotationCommandHandler(
            b.Annotations.Object, b.Files.Object, b.Patients.Object, b.UnitOfWork.Object, b.Resolver.Object);

        var result = await handler.Handle(new RenameFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            AnnotationId = marker.Id,
            Label = "  Fêlure distale  ",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Fêlure distale", marker.Label);   // trimmed
        Assert.Equal(1, marker.X);
        Assert.Equal(2, marker.Y);
        Assert.Equal(3, marker.Z);
        Assert.NotNull(marker.UpdatedAt);
    }

    /// <summary>
    /// ⚠️ Clearing the text is not deleting the marker. Somebody who empties the field still wants the pin
    /// where they put it, and refusing here would make the two gestures mean the same thing.
    /// </summary>
    [Fact]
    public async Task An_Empty_Label_Is_Accepted_And_Is_Not_A_Deletion()
    {
        var b = Setup(ClinicId, ClinicId);
        var marker = Annotation(b.File.Id, ClinicId);
        b.Annotations.Setup(r => r.GetByIdAsync(marker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(marker);

        var handler = new RenameFileAnnotationCommandHandler(
            b.Annotations.Object, b.Files.Object, b.Patients.Object, b.UnitOfWork.Object, b.Resolver.Object);

        var result = await handler.Handle(new RenameFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            AnnotationId = marker.Id,
            Label = "   ",
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, marker.Label);
        b.Annotations.Verify(
            r => r.DeleteAsync(It.IsAny<PatientFileAnnotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_Label_Longer_Than_The_Column_Is_Cut_Rather_Than_Throwing()
    {
        // The browser bounds the field, so this only happens to a hand-made request — and a 500 on a marker's
        // name is a worse answer than a truncated name.
        var marker = Annotation(Guid.NewGuid(), ClinicId, new string('a', PatientFileAnnotation.MaxLabelLength + 50));

        Assert.Equal(PatientFileAnnotation.MaxLabelLength, marker.Label.Length);
    }

    // ── The ceiling ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Nothing else bounds this table: a marker is created by touching a model, and a stuck finger on a
    /// touch screen creates as many as it likes.
    /// </summary>
    [Fact]
    public async Task A_File_Cannot_Carry_More_Markers_Than_The_Ceiling()
    {
        var b = Setup(ClinicId, ClinicId);
        var full = Enumerable
            .Range(0, CreateFileAnnotationCommandHandler.MaxPerFile)
            .Select(_ => Annotation(b.File.Id, ClinicId))
            .ToArray();
        b.Annotations
            .Setup(r => r.GetForFileAsync(b.File.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(full);

        var result = await CreateHandler(b).Handle(new CreateFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            Label = "un de trop",
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        b.Annotations.Verify(
            r => r.AddAsync(It.IsAny<PatientFileAnnotation>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The read ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_List_Refuses_A_File_Of_Another_Clinic_Rather_Than_Returning_Nothing()
    {
        // ⚠️ A refusal, never an empty list: « this model has no markers » and « you may not read this model »
        // are opposite facts, and the reassuring one must not stand in for the other.
        var b = Setup(OtherClinicId, ClinicId);

        var handler = new GetFileAnnotationsQueryHandler(
            b.Annotations.Object, b.Files.Object, b.Patients.Object, b.Resolver.Object);

        var result = await handler.Handle(
            new GetFileAnnotationsQuery { PatientId = b.Patient.Id, FileId = b.File.Id },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        b.Annotations.Verify(
            r => r.GetForFileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_Delete_Removes_The_Marker_And_Commits()
    {
        var b = Setup(ClinicId, ClinicId);
        var marker = Annotation(b.File.Id, ClinicId);
        b.Annotations.Setup(r => r.GetByIdAsync(marker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(marker);

        var handler = new DeleteFileAnnotationCommandHandler(
            b.Annotations.Object, b.Files.Object, b.Patients.Object, b.UnitOfWork.Object, b.Resolver.Object);

        var result = await handler.Handle(new DeleteFileAnnotationCommand
        {
            PatientId = b.Patient.Id,
            FileId = b.File.Id,
            AnnotationId = marker.Id,
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        b.Annotations.Verify(r => r.DeleteAsync(marker, It.IsAny<CancellationToken>()), Times.Once);
        b.UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
