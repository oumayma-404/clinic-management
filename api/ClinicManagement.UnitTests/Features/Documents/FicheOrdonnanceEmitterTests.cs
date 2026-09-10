using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Application.Features.Documents;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace ClinicManagement.UnitTests.Features.Documents;

/// <summary>
/// The séance's ordonnance — created, updated, and <b>never deleted</b> by the fiche de soins.
/// </summary>
public class FicheOrdonnanceEmitterTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid PatientId = Guid.NewGuid();
    private static readonly Guid DoctorId = Guid.NewGuid();
    private static readonly DateTime Intervention = new(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public Mock<IMedicalDocumentRepository> Docs { get; } = new();
        public Mock<IClinicRepository> Clinics { get; } = new();
        public Mock<IDoctorRepository> Doctors { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();

        public Patient Patient { get; } = new(
            PatientId, ClinicId, "Amine", "Trabelsi", new DateTime(1994, 3, 12), "Homme");

        public DentalRecord Record { get; }

        public Harness(Guid? appointmentId = null, bool withDoctor = true)
        {
            Record = new DentalRecord(
                Guid.NewGuid(), PatientId, ClinicId, Intervention, 0m, true,
                appointmentId: appointmentId);
            if (withDoctor)
            {
                Record.SetDoctor(DoctorId);
            }

            Clinics.Setup(r => r.GetByIdAsync(ClinicId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Clinic(
                    ClinicId, "Cabinet Dentaire Ben Salah", "12 rue de Rome, Tunis", "+216 71 234 567",
                    "contact@cabinet.tn", city: "Tunis"));

            Doctors.Setup(r => r.GetByIdAsync(DoctorId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Doctor(DoctorId, ClinicId, "Leïla", "Ben Salah", "Orthodontist"));

            // No document yet, on either link. Stubbed rather than left to Moq's default because an unstubbed
            // collection member hands back null in this suite.
            Docs.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<MedicalDocument>());
        }

        public void Existing(MedicalDocument document) =>
            Docs.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                    It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new[] { document });

        public Task<FicheOrdonnanceResult> Emit(PrescriptionInput? prescription) =>
            FicheOrdonnanceEmitter.EmitAsync(
                prescription, Record, Patient, ClinicId, "user-1",
                Docs.Object, Clinics.Object, Doctors.Object, Uow.Object,
                NullLogger<Harness>.Instance, CancellationToken.None);
    }

    private static PrescriptionInput OneDrug(string name = "Augmentin Comprimé") => new()
    {
        Lines = new List<PrescriptionLineInput>
        {
            new()
            {
                Kind = PrescriptionLineKinds.Medicament,
                Name = name,
                Dosage = "1 g",
                TimesPerDay = "3",
                Duration = "7",
            },
        },
    };

    // ── Creating ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_Prescribed_Seance_Issues_An_Ordonnance()
    {
        var h = new Harness();
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        var result = await h.Emit(OneDrug());

        Assert.NotNull(added);
        Assert.Equal(DocumentTypes.Prescription, added!.DocumentType);
        Assert.Equal(added.Id, result.DocumentId);
        Assert.Equal(new[] { "Augmentin Comprimé 1 g" }, result.Summary);
        // The link the séance history joins on.
        Assert.Equal(h.Record.Id, added.DentalRecordId);
    }

    /// <summary>
    /// ⚠️ The clinic and practitioner values are read from the DATABASE, never accepted from a caller. The
    /// document editor sends them from the browser with literal « [Nom du cabinet] » / « Dr. [Nom] » fallbacks,
    /// so a failed clinic read there snapshots a placeholder onto a legal document; a fiche must not be able to.
    /// </summary>
    [Fact]
    public async Task The_Cabinet_And_Practitioner_Are_Resolved_Server_Side()
    {
        var h = new Harness();
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        await h.Emit(OneDrug());

        Assert.Equal("Cabinet Dentaire Ben Salah", added!.ClinicName);
        Assert.Equal("12 rue de Rome, Tunis", added.ClinicAddress);
        Assert.Equal("+216 71 234 567", added.ClinicPhone);
        Assert.Equal("Dr. Leïla Ben Salah", added.DoctorName);
        // The stored specialty key is English; the document prints French — see DoctorSpecialtyLabels.
        Assert.Equal("Orthodontiste", added.DoctorSpecialty);
        Assert.Equal("Amine Trabelsi", added.PatientName);
        Assert.Equal("12/03/1994", added.PatientAge);
    }

    /// <summary>
    /// Both links are set when the fiche documents a visit: the appointment keeps the Documents tab's
    /// « Séance » column working, the fiche id is what the séance history joins on.
    /// </summary>
    [Fact]
    public async Task An_Ordonnance_Carries_Both_Links_When_The_Fiche_Documents_A_Visit()
    {
        var appointmentId = Guid.NewGuid();
        var h = new Harness(appointmentId);
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        await h.Emit(OneDrug());

        Assert.Equal(appointmentId, added!.AppointmentId);
        Assert.Equal(h.Record.Id, added.DentalRecordId);
    }

    /// <summary>
    /// ⚠️ The printed name and the cachet must come from the SAME doctor. The snapshot
    /// (`PractitionerRenderSnapshot.ResolveAsync`) always fell back to the caller's own record when the fiche
    /// attributed nobody; the entity-level name and spécialité did not — so a document could carry one
    /// practitioner's cachet and n° CNOMDT above a blank prescriber line. Found through the aperçu, where a
    /// not-yet-created fiche has no `DoctorId` to send at all.
    /// </summary>
    [Fact]
    public async Task With_No_Attributed_Practitioner_The_Name_Falls_Back_To_The_Caller_Like_The_Cachet_Does()
    {
        var h = new Harness(withDoctor: false);
        h.Doctors.Setup(r => r.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(Guid.NewGuid(), ClinicId, "Salma", "Ben Youssef", "Dentist"));
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        await h.Emit(OneDrug());

        Assert.Equal("Dr. Salma Ben Youssef", added!.DoctorName);
        Assert.Equal("Médecin dentiste", added.DoctorSpecialty);
    }

    /// <summary>A doctor record belonging to another clinic is refused on the fall-through too.</summary>
    [Fact]
    public async Task The_Callers_Own_Record_Is_Tenant_Checked_On_The_Fall_Through()
    {
        var h = new Harness(withDoctor: false);
        h.Doctors.Setup(r => r.GetByUserIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Doctor(Guid.NewGuid(), Guid.NewGuid(), "Foreign", "Practitioner", "Dentist"));
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        await h.Emit(OneDrug());

        Assert.Equal(string.Empty, added!.DoctorName);
    }

    [Fact]
    public async Task A_Fiche_With_No_Attributed_Practitioner_Still_Issues_One()
    {
        // A fiche with no attribution is a real outcome (PractitionerAttribution leaves it null rather than
        // guessing), and it must not cost the patient their prescription.
        var h = new Harness(withDoctor: false);
        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        var result = await h.Emit(OneDrug());

        Assert.NotNull(result.DocumentId);
        Assert.Equal(string.Empty, added!.DoctorName);
        Assert.Equal(string.Empty, added.DoctorSpecialty);
    }

    // ── Updating ──────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Re_Saving_Updates_The_Same_Ordonnance_Rather_Than_Issuing_A_Second()
    {
        var h = new Harness();
        var existing = Existing(h.Record.Id);
        h.Existing(existing);

        var result = await h.Emit(OneDrug("Ibuprofène Comprimé"));

        Assert.Equal(existing.Id, result.DocumentId);
        h.Docs.Verify(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Docs.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        // ⚠️ Asserted on the PARSED value, never on the raw string. `PractitionerRenderSnapshot.ApplyTo`
        // re-serialises the whole object with the default encoder to add the cachet and the n° d'ordre, so the
        // stored bytes hold « Ibuprofène » — which is what every document the API writes has always looked
        // like. A raw `Assert.Contains` here reads as a data-loss failure and is a test bug.
        Assert.Equal(
            new[] { "Ibuprofène Comprimé 1 g" },
            PrescriptionLines.ShortLabels(existing.ContentJson));
    }

    // ── Never deleting ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ The property the whole design rests on. Clearing the section and re-saving leaves the ordonnance
    /// standing: that paper may be in the patient's hand, deletion is role-gated on the document itself while
    /// the fiche is open to reception, and patient records resist destruction here.
    /// </summary>
    [Fact]
    public async Task Emptying_The_Section_Never_Deletes_The_Ordonnance()
    {
        var h = new Harness();
        var existing = Existing(h.Record.Id);
        h.Existing(existing);
        var contentBefore = existing.ContentJson;

        var result = await h.Emit(new PrescriptionInput { Lines = new List<PrescriptionLineInput>() });

        h.Docs.Verify(r => r.DeleteAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Docs.Verify(r => r.UpdateAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(contentBefore, existing.ContentJson);
        // The response still names it, so the séance row keeps saying what is prescribed.
        Assert.Equal(existing.Id, result.DocumentId);
        Assert.NotEmpty(result.Summary);
    }

    [Fact]
    public async Task A_Seance_With_Nothing_Prescribed_Issues_Nothing()
    {
        var h = new Harness();

        var result = await h.Emit(new PrescriptionInput { Lines = new List<PrescriptionLineInput>() });

        Assert.Null(result.DocumentId);
        Assert.Empty(result.Summary);
        h.Docs.Verify(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// A blank trailing row is the ordinary state of a form somebody has just pressed « + Médicament » on.
    /// It must not mint an ordonnance with one nameless line on it.
    /// </summary>
    [Fact]
    public async Task A_Line_With_No_Name_Does_Not_Count_As_A_Prescription()
    {
        var h = new Harness();

        var result = await h.Emit(new PrescriptionInput
        {
            Lines = new List<PrescriptionLineInput>
            {
                new() { Kind = PrescriptionLineKinds.Medicament, Name = "   " },
            },
        });

        Assert.Null(result.DocumentId);
        h.Docs.Verify(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The legacy join ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// An ordonnance written before <c>DentalRecordId</c> existed carries only the appointment. The fiche's own
    /// document still wins over it, so a séance that has issued one does not report the older one instead.
    /// </summary>
    [Fact]
    public async Task The_Fiches_Own_Document_Wins_Over_A_Legacy_One_Sharing_Its_Visit()
    {
        var appointmentId = Guid.NewGuid();
        var h = new Harness(appointmentId);
        var legacy = Existing(dentalRecordId: null, appointmentId: appointmentId);
        var mine = Existing(h.Record.Id, appointmentId);
        h.Docs.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { legacy, mine });

        var found = await FicheOrdonnanceEmitter.FindAsync(
            h.Record, ClinicId, DocumentTypes.Prescription, h.Docs.Object, CancellationToken.None);

        Assert.Same(mine, found);
    }

    [Fact]
    public async Task A_Legacy_Document_Naming_Only_The_Visit_Is_Still_Found()
    {
        var appointmentId = Guid.NewGuid();
        var h = new Harness(appointmentId);
        var legacy = Existing(dentalRecordId: null, appointmentId: appointmentId);
        h.Existing(legacy);

        var found = await FicheOrdonnanceEmitter.FindAsync(
            h.Record, ClinicId, DocumentTypes.Prescription, h.Docs.Object, CancellationToken.None);

        Assert.Same(legacy, found);
    }

    // ── Two sheets, never one ─────────────────────────────────────────────────────────────────────────────────

    private static PrescriptionLineInput Examen(string name = "Radiographie panoramique dentaire") =>
        new() { Kind = PrescriptionLineKinds.Examen, Name = name };

    /// <summary>
    /// ⚠️ The property this split exists for. A médicament and an examen may not share a sheet — different
    /// destinations (la pharmacie, le laboratoire), a single-use examen prescription, and one CNAM claim per
    /// line — so a séance that prescribes both issues TWO documents.
    /// </summary>
    [Fact]
    public async Task A_Seance_That_Prescribes_Both_Issues_Two_Documents()
    {
        var h = new Harness();
        var added = new List<MedicalDocument>();
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added.Add(d));

        var payload = OneDrug();
        payload.Lines.Add(Examen());

        var result = await h.Emit(payload);

        Assert.Equal(2, added.Count);
        Assert.Contains(added, d => d.DocumentType == DocumentTypes.Prescription);
        Assert.Contains(added, d => d.DocumentType == DocumentTypes.Examens);

        var ordonnance = added.Single(d => d.DocumentType == DocumentTypes.Prescription);
        var demande = added.Single(d => d.DocumentType == DocumentTypes.Examens);

        Assert.Equal(ordonnance.Id, result.DocumentId);
        Assert.Equal(demande.Id, result.ExamensDocumentId);
        Assert.Equal(new[] { "Augmentin Comprimé 1 g" }, result.Summary);
        Assert.Equal(new[] { "Radiographie panoramique dentaire" }, result.ExamensSummary);

        // Both name the fiche, so the séance history finds each of them.
        Assert.All(added, d => Assert.Equal(h.Record.Id, d.DentalRecordId));
    }

    /// <summary>
    /// The defect the split removes, asserted from both sides: the médicament sheet must not carry the examen,
    /// and the examens sheet must not carry the médicament.
    /// </summary>
    [Fact]
    public async Task Neither_Sheet_Carries_The_Others_Lines()
    {
        var h = new Harness();
        var added = new List<MedicalDocument>();
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added.Add(d));

        var payload = OneDrug();
        payload.Lines.Add(Examen());

        await h.Emit(payload);

        var ordonnance = added.Single(d => d.DocumentType == DocumentTypes.Prescription);
        var demande = added.Single(d => d.DocumentType == DocumentTypes.Examens);

        // Parsed, never raw — ApplyTo re-serialises with the default encoder, so « Comprimé » is escaped in
        // the stored bytes and a raw Contains reads as data loss that did not happen.
        Assert.Equal(new[] { "Augmentin Comprimé 1 g" }, PrescriptionLines.ShortLabels(ordonnance.ContentJson));
        Assert.Empty(PrescriptionLines.ExamenShortLabels(ordonnance.ContentJson));

        Assert.Equal(
            new[] { "Radiographie panoramique dentaire" },
            PrescriptionLines.ExamenShortLabels(demande.ContentJson));
        Assert.Empty(PrescriptionLines.ShortLabels(demande.ContentJson));
    }

    [Fact]
    public async Task A_Seance_With_Only_Examens_Issues_No_Medicament_Ordonnance()
    {
        var h = new Harness();
        var added = new List<MedicalDocument>();
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added.Add(d));

        var result = await h.Emit(new PrescriptionInput
        {
            Lines = new List<PrescriptionLineInput> { Examen("Bilan sanguin : NFS, glycémie") },
        });

        Assert.Single(added);
        Assert.Equal(DocumentTypes.Examens, added[0].DocumentType);
        Assert.Null(result.DocumentId);
        Assert.Empty(result.Summary);
        Assert.Equal(added[0].Id, result.ExamensDocumentId);
        Assert.Equal(new[] { "Bilan sanguin : NFS, glycémie" }, result.ExamensSummary);
    }

    /// <summary>
    /// ⚠️ The renouvellement was withdrawn from the product (« we do not need it »), so <b>neither</b> sheet
    /// carries it any more. Kept as a case rather than deleted: the médicament sheet used to write the key and
    /// R.5132-3 still lists renouvellement, so this is what fails if somebody restores it from the norm.
    /// </summary>
    [Fact]
    public async Task Neither_Sheet_Carries_A_Renouvellement()
    {
        var h = new Harness();
        var added = new List<MedicalDocument>();
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added.Add(d));

        var payload = OneDrug();
        payload.Lines.Add(Examen());

        await h.Emit(payload);

        foreach (var document in added)
        {
            Assert.DoesNotContain("renewals", document.ContentJson);
        }
    }

    /// <summary>
    /// ⚠️ The never-delete rule holds <b>per document</b>. Removing every examen from a séance that still
    /// prescribes a médicament updates the ordonnance and leaves the demande d'examens exactly as it is — two
    /// papers, two histories, and the one already handed over is not rewritten by the other's edit.
    /// </summary>
    [Fact]
    public async Task Emptying_Only_The_Examens_Leaves_The_Demande_Standing()
    {
        var h = new Harness();
        var ordonnance = Existing(h.Record.Id);
        var demande = Existing(
            h.Record.Id,
            documentType: DocumentTypes.Examens,
            contentJson: "{\"examens\":[{\"name\":\"Radiographie panoramique dentaire\"}]}");
        h.Docs.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ordonnance, demande });
        var demandeBefore = demande.ContentJson;

        var result = await h.Emit(OneDrug("Ibuprofène Comprimé"));

        h.Docs.Verify(r => r.UpdateAsync(ordonnance, It.IsAny<CancellationToken>()), Times.Once);
        h.Docs.Verify(r => r.UpdateAsync(demande, It.IsAny<CancellationToken>()), Times.Never);
        h.Docs.Verify(r => r.DeleteAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(demandeBefore, demande.ContentJson);

        // And the row still names both, so nothing disappears from the séance history either.
        Assert.Equal(demande.Id, result.ExamensDocumentId);
        Assert.Equal(new[] { "Radiographie panoramique dentaire" }, result.ExamensSummary);
    }

    /// <summary>
    /// ⚠️ <b>Nothing was migrated, and this is what makes that safe.</b> Ordonnances written before the split
    /// hold their examens as lines of the <c>medications</c> array, each carrying <c>kind: "examen"</c>. The
    /// fiche reads them back as examens, so the next save moves them onto their own sheet — the médicament
    /// ordonnance is rewritten without them and a demande d'examens is issued. No backfill, no data touched
    /// until a human reopens the séance.
    /// </summary>
    [Fact]
    public async Task A_Legacy_Examen_Inside_The_Medications_Array_Moves_To_Its_Own_Sheet_On_Re_Save()
    {
        var h = new Harness();
        var legacy = Existing(
            h.Record.Id,
            contentJson:
            "{\"medications\":[{\"kind\":\"medicament\",\"name\":\"Augmentin Comprimé\",\"dosage\":\"1 g\"},"
            + "{\"kind\":\"examen\",\"name\":\"Radiographie panoramique dentaire\"}]}");
        h.Existing(legacy);

        // Exactly what the modal round-trips: PrescriptionLines.Read hands both lines back with their kinds.
        var reopened = new PrescriptionInput { Lines = PrescriptionLines.Read(legacy.ContentJson).ToList() };
        Assert.Equal(2, reopened.Lines.Count);

        MedicalDocument? added = null;
        h.Docs.Setup(r => r.AddAsync(It.IsAny<MedicalDocument>(), It.IsAny<CancellationToken>()))
            .Callback<MedicalDocument, CancellationToken>((d, _) => added = d);

        var result = await h.Emit(reopened);

        // The médicament sheet keeps the drug and loses the examen.
        Assert.Equal(legacy.Id, result.DocumentId);
        Assert.Equal(new[] { "Augmentin Comprimé 1 g" }, PrescriptionLines.ShortLabels(legacy.ContentJson));
        Assert.Empty(PrescriptionLines.ExamenShortLabels(legacy.ContentJson));

        // And the examen gets a sheet of its own.
        Assert.NotNull(added);
        Assert.Equal(DocumentTypes.Examens, added!.DocumentType);
        Assert.Equal(new[] { "Radiographie panoramique dentaire" }, result.ExamensSummary);
    }

    /// <summary>
    /// A demande d'examens is newer than <c>MedicalDocument.DentalRecordId</c>, so one with a null fiche id
    /// cannot exist — and the legacy appointment fallback must not run for it, or a document belonging to
    /// another fiche of the same visit would be claimed and overwritten here.
    /// </summary>
    [Fact]
    public async Task The_Legacy_Visit_Fallback_Never_Claims_A_Demande_Examens()
    {
        var appointmentId = Guid.NewGuid();
        var h = new Harness(appointmentId);
        var otherFichesDemande = Existing(
            dentalRecordId: null,
            appointmentId: appointmentId,
            documentType: DocumentTypes.Examens,
            contentJson: "{\"examens\":[{\"name\":\"Téléradiographie\"}]}");
        h.Existing(otherFichesDemande);

        var found = await FicheOrdonnanceEmitter.FindAsync(
            h.Record, ClinicId, DocumentTypes.Examens, h.Docs.Object, CancellationToken.None);

        Assert.Null(found);
    }

    // -- The other door -------------------------------------------------------------------------------------

    /// <summary>
    /// ⚠️ The document is editable from `/documents/prescription` too, and its own `xmin` does NOT protect it
    /// from this save: the emitter loads it INSIDE the fiche's transaction, so the tracked copy always carries
    /// the current token. The stale copy is in the browser. Measured end to end before this was fixed: a
    /// colleague's correction to a medicament's name was reverted by the next fiche save, with a success toast
    /// and no refusal. The modal therefore round-trips the version and it is declared as the expected one.
    /// </summary>
    [Fact]
    public async Task The_Version_The_Modal_Read_Is_Declared_As_The_Expected_One()
    {
        var h = new Harness();
        var existing = Existing(h.Record.Id);
        h.Existing(existing);

        var payload = OneDrug();
        payload.PrescriptionDocumentVersion = 4242;

        await h.Emit(payload);

        h.Uow.Verify(u => u.SetExpectedVersion(existing, 4242), Times.Once);
    }

    /// <summary>
    /// ⚠️ 0 means « not supplied » and turns the check off - the solution-wide rule. That is what keeps every
    /// older client and every server-internal writer behaving exactly as it did.
    /// </summary>
    [Fact]
    public async Task An_Absent_Version_Leaves_The_Check_Off()
    {
        var h = new Harness();
        var existing = Existing(h.Record.Id);
        h.Existing(existing);

        await h.Emit(OneDrug());

        h.Uow.Verify(u => u.SetExpectedVersion(It.IsAny<object>(), 0u), Times.AtLeastOnce);
        h.Uow.Verify(
            u => u.SetExpectedVersion(It.IsAny<object>(), It.Is<uint>(v => v != 0)),
            Times.Never);
    }

    /// <summary>Each sheet carries its OWN token - one document's edit must not refuse the other's save.</summary>
    [Fact]
    public async Task Each_Sheet_Declares_Its_Own_Version()
    {
        var h = new Harness();
        var ordonnance = Existing(h.Record.Id);
        var demande = Existing(
            h.Record.Id,
            documentType: DocumentTypes.Examens,
            contentJson: "{\"examens\":[{\"name\":\"Panoramique\"}]}");
        h.Docs.Setup(r => r.GetFicheOrdonnancesForDentalRecordsAsync(
                It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ordonnance, demande });

        var payload = OneDrug();
        payload.Lines.Add(Examen());
        payload.PrescriptionDocumentVersion = 11;
        payload.ExamensDocumentVersion = 22;

        await h.Emit(payload);

        h.Uow.Verify(u => u.SetExpectedVersion(ordonnance, 11), Times.Once);
        h.Uow.Verify(u => u.SetExpectedVersion(demande, 22), Times.Once);
    }

    private static MedicalDocument Existing(
        Guid? dentalRecordId,
        Guid? appointmentId = null,
        string documentType = DocumentTypes.Prescription,
        string? contentJson = null) => new(
        Guid.NewGuid(),
        PatientId,
        ClinicId,
        documentType,
        Intervention,
        "Amine Trabelsi",
        "12/03/1994",
        contentJson
            ?? "{\"medications\":[{\"kind\":\"medicament\",\"name\":\"Augmentin Comprimé\",\"dosage\":\"1 g\"}]}",
        "Cabinet Dentaire Ben Salah",
        "12 rue de Rome, Tunis",
        "+216 71 234 567",
        "Dr. Leïla Ben Salah",
        "Orthodontiste",
        isDraft: false,
        recipientDoctorName: null,
        recipientDoctorSpecialty: null,
        fileId: null,
        appointmentId: appointmentId,
        dentalRecordId: dentalRecordId);
}
