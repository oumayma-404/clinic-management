using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// Official-documents production-readiness, Part C (FR-3.3 / FR-6.1). The issuing practitioner's cachet key
/// + CNOMDT ordre number and the cabinet city are snapshotted into the document's ContentJson at creation,
/// so the unauthenticated background PDF job can render them without a live doctor/clinic lookup (CERT-5).
/// </summary>
public class CertificatContentTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private const string UserId = "auth0|doctor-1";

    private static Doctor DoctorWithCachet(string ordre, string cachetKey, string cachetType)
    {
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, "Alice", "Martin", "Médecin dentiste");
        doctor.LinkToUser(UserId);
        doctor.SetOrdreNumber(ordre);
        doctor.SetCachet(cachetKey, cachetType);
        return doctor;
    }

    private sealed class Harness
    {
        public Mock<IMedicalDocumentRepository> Docs { get; } = new();
        public Mock<IPatientRepository> Patients { get; } = new();
        public Mock<IPatientFolderRepository> Folders { get; } = new();
        public Mock<IPatientFileRepository> Files { get; } = new();
        public Mock<IFileStorage> Storage { get; } = new();
        public Mock<IAppointmentRepository> Appointments { get; } = new();
        public Mock<ICurrentClinicResolver> Resolver { get; } = new();
        public Mock<IClinicContext> ClinicContext { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<INotificationGenerator> Generator { get; } = new();
        public Mock<IRealtimeNotifier> Realtime { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();

        public Guid PatientId { get; }
        public MedicalDocument? Captured { get; private set; }

        public Harness(Doctor? doctor, Clinic? clinic)
        {
            var patient = new Patient(Guid.NewGuid(), ClinicId, "Jean", "Dupont",
                new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));
            PatientId = patient.Id;

            Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            ClinicContext.Setup(c => c.GetUserId()).Returns(UserId);
            Doctors.Setup(r => r.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(doctor);
            Clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>())).ReturnsAsync(clinic);
            Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
                .Callback<MedicalDocument, CancellationToken>((d, _) => Captured = d);
            Uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        }

        public CreateMedicalDocumentCommandHandler Handler() => new(
            Docs.Object, Patients.Object, Folders.Object, Files.Object, Storage.Object,
            Appointments.Object, Resolver.Object, ClinicContext.Object, Doctors.Object, Clinics.Object,
            Generator.Object, Realtime.Object, Uow.Object,
            NullLogger<CreateMedicalDocumentCommandHandler>.Instance);
    }

    // [CERT-5] cachet key + ordre + clinic city are written into ContentJson, alongside the original content.
    [Theory]
    [InlineData("certificat")]
    [InlineData("prescription")]
    [InlineData("liaison")]
    public async Task Doctor_Cachet_And_Order_Number_Are_Snapshotted_At_Creation(string documentType)
    {
        var doctor = DoctorWithCachet("CNOMDT-12345", "cabinet/doctors/x/cachet", "image/jpeg");
        var clinic = new Clinic(ClinicId, "Cabinet Dentaire", city: "Tunis");
        var h = new Harness(doctor, clinic);

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = documentType,
            DocumentDate = DateTime.UtcNow,
            // A liaison now requires an external recipient name (Part E, FR-4.1) — supply one so the snapshot
            // assertion exercises the liaison path; ignored by the other document types.
            RecipientDoctorName = "Dr Externe",
            ContentJson = "{\"objet\":\"présence\"}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(h.Captured);

        using var doc = JsonDocument.Parse(h.Captured!.ContentJson);
        var root = doc.RootElement;
        Assert.Equal("Tunis", root.GetProperty(PractitionerRenderSnapshot.ClinicCityKey).GetString());
        Assert.Equal("CNOMDT-12345", root.GetProperty(PractitionerRenderSnapshot.DoctorOrdreNumberKey).GetString());
        Assert.Equal("cabinet/doctors/x/cachet", root.GetProperty(PractitionerRenderSnapshot.DoctorCachetKeyKey).GetString());
        Assert.Equal("image/jpeg", root.GetProperty(PractitionerRenderSnapshot.DoctorCachetContentTypeKey).GetString());
        // The original editor content is preserved (snapshot is additive, not destructive).
        Assert.Equal("présence", root.GetProperty("objet").GetString());
    }

    // [CERT-5] a doctor with an ordre but no cachet snapshots only the fields that exist (no cachet keys).
    [Fact]
    public async Task Ordre_Only_Doctor_Snapshots_Ordre_And_City_But_No_Cachet()
    {
        var doctor = new Doctor(Guid.NewGuid(), ClinicId, "Bob", "Durand", "Médecin dentiste");
        doctor.LinkToUser(UserId);
        doctor.SetOrdreNumber("CNOMDT-777");
        var clinic = new Clinic(ClinicId, "Cabinet", city: "Sfax");
        var h = new Harness(doctor, clinic);

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "certificat",
            DocumentDate = DateTime.UtcNow,
            ContentJson = "{}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(h.Captured!.ContentJson);
        var root = doc.RootElement;
        Assert.Equal("Sfax", root.GetProperty(PractitionerRenderSnapshot.ClinicCityKey).GetString());
        Assert.Equal("CNOMDT-777", root.GetProperty(PractitionerRenderSnapshot.DoctorOrdreNumberKey).GetString());
        Assert.False(root.TryGetProperty(PractitionerRenderSnapshot.DoctorCachetKeyKey, out _));
    }

    // [CERT-5 / edge] no linked doctor and no clinic city → creation still succeeds, ContentJson unchanged.
    [Fact]
    public async Task No_Practitioner_Data_Leaves_ContentJson_Unchanged()
    {
        var h = new Harness(doctor: null, clinic: new Clinic(ClinicId, "Cabinet"));

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "prescription",
            DocumentDate = DateTime.UtcNow,
            ContentJson = "{\"a\":\"b\"}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(h.Captured!.ContentJson);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty(PractitionerRenderSnapshot.ClinicCityKey, out _));
        Assert.False(root.TryGetProperty(PractitionerRenderSnapshot.DoctorCachetKeyKey, out _));
        Assert.Equal("b", root.GetProperty("a").GetString());
    }

    // [CERT-1] the create path persists all four certificat content fields (objet/motif + ordre + start date
    // + duration) — one consistent schema, no silent drop — alongside the additive practitioner snapshot (FR-2.2).
    [Fact]
    public async Task Create_Certificat_Persists_All_Fields_Consistently()
    {
        var doctor = DoctorWithCachet("CNOMDT-12345", "cabinet/doctors/x/cachet", "image/png");
        var h = new Harness(doctor, new Clinic(ClinicId, "Cabinet", city: "Tunis"));

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "certificat",
            DocumentDate = DateTime.UtcNow,
            ContentJson = "{\"objetMotif\":\"présence ce jour\",\"doctorOrderNumber\":\"9\"," +
                          "\"startDate\":\"2026-07-21\",\"duration\":\"3\"}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(h.Captured!.ContentJson);
        var root = doc.RootElement;
        Assert.Equal("présence ce jour", root.GetProperty("objetMotif").GetString());
        Assert.Equal("9", root.GetProperty("doctorOrderNumber").GetString());
        Assert.Equal("2026-07-21", root.GetProperty("startDate").GetString());
        Assert.Equal("3", root.GetProperty("duration").GetString());
    }

    // [CERT-4] round-tripping a certificat through the update path keeps all four fields (no silent drop).
    [Fact]
    public async Task Update_Certificat_Preserves_All_Fields()
    {
        const string content = "{\"objetMotif\":\"soins en cours\",\"doctorOrderNumber\":\"9\"," +
                               "\"startDate\":\"2026-07-21\",\"duration\":\"5\"}";
        var document = new MedicalDocument(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "certificat", DateTime.UtcNow, "Jean Dupont", "36 ans",
            "{}", "Cabinet", "Adresse", "+21671000000", "Dr Alice", "Médecin dentiste",
            isDraft: false, null, null, null, null);

        var docs = new Mock<IMedicalDocumentRepository>();
        docs.Setup(r => r.GetByIdAsync(document.Id, It.IsAny<CancellationToken>())).ReturnsAsync(document);
        var clinicContext = new Mock<IClinicContext>();
        clinicContext.Setup(c => c.GetUserId()).Returns((string?)null); // background scope → skips clinic check
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UpdateMedicalDocumentCommandHandler(
            docs.Object, new Mock<IPatientFolderRepository>().Object, new Mock<IPatientFileRepository>().Object,
            new Mock<IFileStorage>().Object, clinicContext.Object, new Mock<IUserRepository>().Object,
            new Mock<IDoctorRepository>().Object, new Mock<IClinicRepository>().Object,
            uow.Object, NullLogger<UpdateMedicalDocumentCommandHandler>.Instance);

        var result = await handler.Handle(new UpdateMedicalDocumentCommand
        {
            Id = document.Id,
            DocumentDate = document.DocumentDate,
            ContentJson = content
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(result.Value!.ContentJson);
        var root = doc.RootElement;
        Assert.Equal("soins en cours", root.GetProperty("objetMotif").GetString());
        Assert.Equal("9", root.GetProperty("doctorOrderNumber").GetString());
        Assert.Equal("2026-07-21", root.GetProperty("startDate").GetString());
        Assert.Equal("5", root.GetProperty("duration").GetString());
    }
}
