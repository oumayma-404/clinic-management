using System.Reflection;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Application.Features.Documents.Queries;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// Hardening pass — medical documents carry no ClinicId, so tenant checks go through the owning
/// Patient. Verifies cross-clinic Get/Delete read as "not found" (AC-1) and that deleting a document
/// also removes its stored blob + PatientFile row (AC-7 — no orphaned file).
/// </summary>
public class MedicalDocumentTenantIsolationTests
{
    private static readonly Guid ClinicId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherClinicId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static Patient Patient(Guid clinicId) => new(
        Guid.NewGuid(),
        clinicId,
        "Jean",
        "Dupont",
        new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        "M",
        new Email("jean.dupont@example.com"),
        new PhoneNumber("+21620123456"));

    private static MedicalDocument Document(Patient patient, Guid? fileId = null)
    {
        var doc = new MedicalDocument(
            Guid.NewGuid(),
            patient.Id,
            "prescription",
            DateTime.UtcNow,
            "Jean Dupont",
            "34",
            "[]",
            "Clinic",
            "Address",
            "+21671000000",
            "Dr House",
            "Dentist",
            fileId: fileId);
        // The handler reads document.Patient.ClinicId; that navigation is EF-populated at runtime (private
        // setter). Set it via reflection so the tenant check has a Patient to resolve against.
        typeof(MedicalDocument).GetProperty(nameof(MedicalDocument.Patient))!.SetValue(doc, patient);
        return doc;
    }

    // ---- GetMedicalDocumentQuery (AC-1, DEV-1 pattern: IClinicContext + IUserRepository) -----

    [Fact]
    public async Task Get_Should_Return_NotFound_For_Other_Clinic_Document()
    {
        var doc = Document(Patient(OtherClinicId));
        var docs = new Mock<IMedicalDocumentRepository>();
        docs.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var user = User.CreateLocalUser(ClinicId, "doctor", "doc@clinic.com", "HASH", "Doc");
        var context = new Mock<IClinicContext>();
        context.Setup(c => c.GetUserId()).Returns(user.Id);
        var users = new Mock<IUserRepository>();
        users.Setup(r => r.GetByAuth0SubAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);

        var handler = new GetMedicalDocumentQueryHandler(docs.Object, context.Object, users.Object);
        var result = await handler.Handle(new GetMedicalDocumentQuery { Id = doc.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ---- GetMedicalDocumentsQuery (AC-1: no-arg branch must scope to the caller's clinic) -----

    [Fact]
    public async Task GetAll_Should_Only_Return_Own_Clinic_Documents()
    {
        var own = Document(Patient(ClinicId));
        var docs = new Mock<IMedicalDocumentRepository>();
        // The no-arg branch now scopes in SQL via GetByClinicIdAsync (Finding 5) — the repository returns
        // only the caller's clinic's documents rather than GetAllAsync + an in-memory filter.
        docs.Setup(r => r.GetByClinicIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { own });
        var clinicResolver = new Mock<ICurrentClinicResolver>();
        clinicResolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Guid>.Success(ClinicId));

        var handler = new GetMedicalDocumentsQueryHandler(docs.Object, clinicResolver.Object);
        var result = await handler.Handle(new GetMedicalDocumentsQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dto = Assert.Single(result.Value!);
        Assert.Equal(own.Id, dto.Id);
    }

    // ---- DeleteMedicalDocumentCommand (AC-1 + AC-7) -------------------------

    private static (Mock<IMedicalDocumentRepository> docs, Mock<IPatientFileRepository> files,
        Mock<IFileStorage> storage, Mock<ICurrentClinicResolver> resolver, Mock<IUnitOfWork> uow,
        DeleteMedicalDocumentCommandHandler handler) DeleteHandler()
    {
        var docs = new Mock<IMedicalDocumentRepository>();
        var files = new Mock<IPatientFileRepository>();
        var storage = new Mock<IFileStorage>();
        var resolver = new Mock<ICurrentClinicResolver>();
        var uow = new Mock<IUnitOfWork>();
        var handler = new DeleteMedicalDocumentCommandHandler(
            docs.Object, files.Object, storage.Object, resolver.Object, uow.Object,
            NullLogger<DeleteMedicalDocumentCommandHandler>.Instance);
        return (docs, files, storage, resolver, uow, handler);
    }

    [Fact]
    public async Task Delete_Should_Return_NotFound_For_Other_Clinic_Document()
    {
        var (docs, files, storage, resolver, uow, handler) = DeleteHandler();
        var doc = Document(Patient(OtherClinicId), fileId: Guid.NewGuid());
        docs.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));

        var result = await handler.Handle(new DeleteMedicalDocumentCommand { Id = doc.Id }, CancellationToken.None);

        Assert.True(result.IsFailure);
        docs.Verify(r => r.DeleteAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        files.Verify(r => r.DeleteAsync(It.IsAny<PatientFile>(), It.IsAny<CancellationToken>()), Times.Never);
        storage.Verify(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [AC-7] Deleting a document removes the DB row, the PatientFile row, AND the underlying blob.
    [Fact]
    public async Task Delete_Should_Remove_Document_File_And_Blob()
    {
        var (docs, files, storage, resolver, uow, handler) = DeleteHandler();
        var patient = Patient(ClinicId);
        var fileId = Guid.NewGuid();
        var doc = Document(patient, fileId);
        var patientFile = new PatientFile(fileId, patient.Id, "ordonnance.pdf", "documents/ordonnance.pdf",
            "application/pdf", 1024, FileType.MedicalRecord);

        docs.Setup(r => r.GetByIdAsync(doc.Id, It.IsAny<CancellationToken>())).ReturnsAsync(doc);
        resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<Guid>.Success(ClinicId));
        files.Setup(r => r.GetByIdAsync(fileId, It.IsAny<CancellationToken>())).ReturnsAsync(patientFile);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await handler.Handle(new DeleteMedicalDocumentCommand { Id = doc.Id }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        files.Verify(r => r.DeleteAsync(patientFile, It.IsAny<CancellationToken>()), Times.Once);
        docs.Verify(r => r.DeleteAsync(doc, It.IsAny<CancellationToken>()), Times.Once);
        storage.Verify(s => s.DeleteAsync("documents/ordonnance.pdf", It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
