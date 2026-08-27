using System.Text.Json;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.Common.Models;
using ClinicManagement.Application.Features.Documents.Commands;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using ClinicManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// Official-documents production-readiness, Part E (FR-4). A lettre de liaison now addresses an *external*
/// confrère: the recipient name is free text (no clinic-doctor lookup, LIA-1) and required (LIA-2); the
/// guided clinical fields round-trip through ContentJson (LIA-3). Render-side omission of empty fields and
/// legacy-letter compatibility (LIA-4/LIA-5) are covered by <c>LiaisonRenderContentTests</c>.
/// </summary>
public class LiaisonContentTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private const string UserId = "auth0|doctor-1";

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

        public Harness()
        {
            var patient = new Patient(Guid.NewGuid(), ClinicId, "Jean", "Dupont",
                new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), "M",
                new Email("jean.dupont@example.com"), new PhoneNumber("+21620123456"));
            PatientId = patient.Id;

            Patients.Setup(r => r.GetByIdAsync(patient.Id, It.IsAny<CancellationToken>())).ReturnsAsync(patient);
            Resolver.Setup(r => r.GetClinicIdAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(Result<Guid>.Success(ClinicId));
            ClinicContext.Setup(c => c.GetUserId()).Returns(UserId);
            Clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(ClinicId, "Cabinet", city: "Tunis"));
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

    // [LIA-1] a free-text external recipient is stored verbatim; no clinic-doctor lookup is performed.
    [Fact]
    public async Task Create_Liaison_With_External_Recipient_Succeeds()
    {
        var h = new Harness();

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "liaison",
            DocumentDate = DateTime.UtcNow,
            RecipientDoctorName = "Dr Ahmed Ben Salah",
            RecipientDoctorSpecialty = "Chirurgien maxillo-facial",
            ContentJson = "{\"recipientAddress\":\"12 rue de la Santé, Tunis\"}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(h.Captured);
        Assert.Equal("Dr Ahmed Ben Salah", h.Captured!.RecipientDoctorName);
        Assert.Equal("Chirurgien maxillo-facial", h.Captured.RecipientDoctorSpecialty);
        // The recipient is free text — the command never resolves a Doctor by name/id for the recipient.
        h.Doctors.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [LIA-2] the recipient name is the only required field — a liaison without one is rejected up-front.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Missing_Recipient_Name_Is_Rejected(string? recipientName)
    {
        var h = new Harness();

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "liaison",
            DocumentDate = DateTime.UtcNow,
            RecipientDoctorName = recipientName,
            ContentJson = "{}"
        }, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Contains("confrère destinataire", result.Error);
        // Rejected before any work — no patient lookup, no document persisted.
        h.Patients.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Docs.Verify(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // [LIA-3] the guided clinical fields round-trip through ContentJson.
    [Fact]
    public async Task Guided_Fields_Persisted_In_Content()
    {
        var h = new Harness();

        var result = await h.Handler().Handle(new CreateMedicalDocumentCommand
        {
            PatientId = h.PatientId,
            DocumentType = "liaison",
            DocumentDate = DateTime.UtcNow,
            RecipientDoctorName = "Dr Externe",
            ContentJson = "{\"motif\":\"avis spécialisé\",\"examenClinique\":\"tuméfaction\"," +
                          "\"examenRadiologique\":\"image kystique\",\"actesRealises\":\"drainage\"," +
                          "\"prescriptions\":\"Amoxicilline 1g x2/j pendant 7 jours\"}"
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        using var doc = JsonDocument.Parse(h.Captured!.ContentJson);
        var root = doc.RootElement;
        Assert.Equal("avis spécialisé", root.GetProperty("motif").GetString());
        Assert.Equal("tuméfaction", root.GetProperty("examenClinique").GetString());
        Assert.Equal("image kystique", root.GetProperty("examenRadiologique").GetString());
        Assert.Equal("drainage", root.GetProperty("actesRealises").GetString());
        Assert.Equal("Amoxicilline 1g x2/j pendant 7 jours", root.GetProperty("prescriptions").GetString());
    }
}
