using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// Official-documents production-readiness, Part A (FR-1.4 honoraires retirement + FR-6.2 filename fix).
/// The "note d'honoraires" document type is retired — compliant honoraires are issued through the Invoice
/// pipeline — so creating one is rejected before any work happens. The French filename map is now a single
/// shared helper (<see cref="DocumentFileNaming"/>) so the create and update paths can't drift; the update
/// copy had been missing the <c>bulletin-cnam</c> arm (a re-saved BS1 was filed under the raw type name).
/// </summary>
public class DocumentTypeAndFilenameTests
{
    private static CreateMedicalDocumentCommandHandler CreateHandler(Mock<IPatientRepository>? patients = null) =>
        new(
            new Mock<IMedicalDocumentRepository>().Object,
            (patients ?? new Mock<IPatientRepository>()).Object,
            new Mock<IPatientFolderRepository>().Object,
            new Mock<IPatientFileRepository>().Object,
            new Mock<IFileStorage>().Object,
            new Mock<IAppointmentRepository>().Object,
            new Mock<ICurrentClinicResolver>().Object,
            new Mock<INotificationGenerator>().Object,
            new Mock<IRealtimeNotifier>().Object,
            new Mock<IUnitOfWork>().Object,
            NullLogger<CreateMedicalDocumentCommandHandler>.Instance);

    // [TYPE-1] Creating a document of the retired "honoraires" type is rejected.
    [Theory]
    [InlineData("honoraires")]
    [InlineData("HONORAIRES")]
    [InlineData("  honoraires  ")]
    public async Task Create_With_Honoraires_Type_Is_Rejected(string type)
    {
        var patients = new Mock<IPatientRepository>();
        var handler = CreateHandler(patients);

        var result = await handler.Handle(
            new CreateMedicalDocumentCommand { PatientId = Guid.NewGuid(), DocumentType = type, ContentJson = "{}" },
            CancellationToken.None);

        Assert.True(result.IsFailure);
        // Rejected up-front — no patient lookup, so no honoraires MedicalDocument is ever created.
        patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // A still-supported type is NOT rejected by the honoraires guard (it proceeds to the patient lookup,
    // which — with an unconfigured mock — resolves to "patient not found", i.e. past the type guard).
    [Fact]
    public async Task Create_With_Supported_Type_Passes_The_Type_Guard()
    {
        var patients = new Mock<IPatientRepository>();
        var handler = CreateHandler(patients);

        var result = await handler.Handle(
            new CreateMedicalDocumentCommand { PatientId = Guid.NewGuid(), DocumentType = "certificat", ContentJson = "{}" },
            CancellationToken.None);

        Assert.True(result.IsFailure); // "Patient not found" — but the honoraires guard let it through.
        patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // [FILE-1 / FILE-2 / FR-6.2] One shared map drives both create and update filenames — including the
    // previously-missing bulletin-cnam arm on the update path.
    [Theory]
    [InlineData("prescription", "ordonnance")]
    [InlineData("certificat", "certificat-medical")]
    [InlineData("liaison", "lettre-de-liaison")]
    [InlineData("honoraires", "note-d-honoraires")]
    [InlineData("bulletin-cnam", "bulletin-de-soins-cnam")]
    public void GetDocumentTypeName_Maps_To_French_Base_Name(string type, string expected)
    {
        Assert.Equal(expected, DocumentFileNaming.GetDocumentTypeName(type));
    }

    // [FILE-1] The bulletin-cnam mapping is case-insensitive (the raw type may arrive capitalised).
    [Fact]
    public void GetDocumentTypeName_Bulletin_Cnam_Is_Case_Insensitive()
    {
        Assert.Equal("bulletin-de-soins-cnam", DocumentFileNaming.GetDocumentTypeName("Bulletin-CNAM"));
    }

    // An unknown type passes through, lowercased (unchanged fallback behaviour).
    [Fact]
    public void GetDocumentTypeName_Unknown_Type_Passes_Through_Lowercased()
    {
        Assert.Equal("autre", DocumentFileNaming.GetDocumentTypeName("AUTRE"));
    }
}
