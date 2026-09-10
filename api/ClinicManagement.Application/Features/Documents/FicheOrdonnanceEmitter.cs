using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Application.DTOs;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace ClinicManagement.Application.Features.Documents;

/// <summary>
/// Turns what a fiche de soins prescribed into the séance's own <b>ordonnances</b> — real
/// <c>MedicalDocument</c>s, printable, e-mailable, listed in the patient's Documents tab and re-renderable by
/// the background PDF job, with the norm-mandated identity block the shared <c>DocumentIdentity</c> composer
/// already produces.
///
/// <para>
/// ⚠️ <b>Up to TWO documents per save, and that is the medically correct shape rather than an elaboration.</b>
/// Médicaments go on a <c>prescription</c>; examens (bilan, radio, avis) go on a <c>examens</c> — « le médecin
/// formule sur des ordonnances distinctes les prescriptions de médicaments … et les examens de laboratoire ».
/// The two sheets are handed to different people (la pharmacie, le laboratoire, le centre d'imagerie), an
/// examen prescription is single-use so one sheet cannot serve two destinations, and CNAM reimburses per line
/// against the matching prescription. The first version of this emitter put an examen on the médicament
/// ordonnance as one more line; it printed a panoramique on a form built to R.5132-3 for listes I/II.
/// See <c>DocumentTypes.Examens</c>.
/// </para>
///
/// <para>
/// ⚠️ <b>Called BEFORE the fiche's <c>SaveChangesAsync</c>, inside its transaction — never post-commit.</b>
/// The post-commit block of both fiche commands is for <i>derived</i> side effects (stock consumption, the
/// note d'honoraires, marking the visit complete): those may fail without the clinical record being wrong.
/// A prescription is <i>entered clinical data</i>. Emitting it best-effort would mean a dentist typing an
/// antibiotic, reading « fiche enregistrée », and having no ordonnance — the exact shape of silent loss this
/// codebase keeps finding. Either both land or neither does.
/// </para>
///
/// <para>
/// ⚠️ <b>It never deletes.</b> Clearing the section and re-saving leaves the existing ordonnances standing.
/// Three reasons, each sufficient: the paper may already be in the patient's hand; deletion is
/// <c>AdminOrDoctor</c> while the fiche is open to reception, so the fiche cannot honour the gate;
/// and « patient records resist destruction » is a standing rule here. The document's own delete path — named,
/// confirmed, role-gated — stays the only way. ⚠️ This holds <b>per document</b>: removing every examen from a
/// séance that also prescribes médicaments leaves the demande d'examens standing while the ordonnance is
/// updated, because the two are separate papers with separate histories.
/// </para>
///
/// <para>
/// ⚠️ <b>The clinic and practitioner values are resolved HERE, not accepted from the caller.</b> The document
/// editor sends <c>clinicName</c> / <c>clinicAddress</c> / <c>clinicPhone</c> / <c>doctorName</c> /
/// <c>doctorSpecialty</c> from the browser, with literal <c>"[Nom du cabinet]"</c> / <c>"Dr. [Nom]"</c>
/// fallbacks — so a failed clinic read there snapshots a placeholder onto a legal document. A fiche has no
/// business carrying those over the wire at all, and reading them from the database is both simpler and
/// strictly safer.
/// </para>
/// </summary>
public static class FicheOrdonnanceEmitter
{
    /// <summary>
    /// Creates or updates the séance's ordonnances, and reports what the séance history row should say about
    /// each of them.
    ///
    /// <para>
    /// <paramref name="prescription"/> null or empty and no document yet → nothing happens. Empty with a
    /// document already there → the document is left exactly as it is (see the type remark), and the summary
    /// is read back off it rather than from the empty payload — otherwise clearing the section would make the
    /// row claim nothing was prescribed while the ordonnance still says otherwise.
    /// </para>
    /// </summary>
    public static async Task<FicheOrdonnanceResult> EmitAsync(
        PrescriptionInput? prescription,
        DentalRecord record,
        Patient patient,
        Guid clinicId,
        string? callerUserId,
        IMedicalDocumentRepository documents,
        IClinicRepository clinics,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(patient);

        // One read for both documents — the repository returns every ordonnance this fiche owns, of either type.
        var owned = await FindAllAsync(record, clinicId, documents, cancellationToken);

        var (medicaments, examens) = Split(prescription);

        var ordonnance = await EmitOneAsync(
            DocumentTypes.Prescription,
            medicaments,
            prescription?.PrescriptionDocumentVersion ?? 0,
            Pick(owned, DocumentTypes.Prescription, record),
            record,
            patient,
            clinicId,
            callerUserId,
            documents,
            clinics,
            doctors,
            unitOfWork,
            logger,
            cancellationToken);

        var demande = await EmitOneAsync(
            DocumentTypes.Examens,
            examens,
            prescription?.ExamensDocumentVersion ?? 0,
            Pick(owned, DocumentTypes.Examens, record),
            record,
            patient,
            clinicId,
            callerUserId,
            documents,
            clinics,
            doctors,
            unitOfWork,
            logger,
            cancellationToken);

        return new FicheOrdonnanceResult(
            ordonnance.DocumentId,
            ordonnance.Summary,
            demande.DocumentId,
            demande.Summary);
    }

    /// <summary>
    /// Which sheet each prescribed line belongs on — <b>the only place that decision lives</b>, shared by the
    /// save path and the preview so an « aperçu » can never sort a line differently from the document the next
    /// save writes.
    ///
    /// <para>
    /// Blank names are dropped here rather than by each caller: a line whose name was never typed is not a
    /// prescription, and it must not be the reason a document gets created. ⚠️ <c>IsExamen</c> reads an absent
    /// or unrecognised kind as a <b>médicament</b>, which is what keeps every older caller — and every legacy
    /// document round-tripped through the fiche — writing exactly the sheet it used to.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<PrescriptionLineInput> Medicaments, IReadOnlyList<PrescriptionLineInput> Examens)
        Split(PrescriptionInput? prescription)
    {
        var lines = prescription?.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.Name))
            .ToList() ?? new List<PrescriptionLineInput>();

        return (
            lines.Where(l => !PrescriptionLineKinds.IsExamen(l.Kind)).ToList(),
            lines.Where(l => PrescriptionLineKinds.IsExamen(l.Kind)).ToList());
    }

    /// <summary>
    /// One document's half of the save. Identical logic for both types; what differs is the content shape,
    /// which <see cref="ComposeAsync"/> owns.
    /// </summary>
    private static async Task<(Guid? DocumentId, IReadOnlyList<string> Summary)> EmitOneAsync(
        string documentType,
        IReadOnlyList<PrescriptionLineInput> lines,
        uint expectedVersion,
        MedicalDocument? existing,
        DentalRecord record,
        Patient patient,
        Guid clinicId,
        string? callerUserId,
        IMedicalDocumentRepository documents,
        IClinicRepository clinics,
        IDoctorRepository doctors,
        IUnitOfWork unitOfWork,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            // Nothing prescribed of this kind. An existing document is deliberately untouched — see the type
            // remark — and its summary is read back off what it actually says.
            return (existing?.Id, existing is null ? Array.Empty<string>() : SummaryOf(documentType, existing.ContentJson));
        }

        var composed = await ComposeAsync(
            documentType,
            lines,
            FicheOrdonnanceContext.From(record),
            patient,
            clinicId,
            callerUserId,
            clinics,
            doctors,
            logger,
            cancellationToken);

        if (existing is not null)
        {
            /*
             * ⚠️ The document's own `xmin` does NOT protect it here, and the comment that stood on this line
             * claimed it did. This method loads the document **inside** the fiche's transaction, so the tracked
             * entity always carries the CURRENT token and the update always succeeds; the stale copy lives in
             * the browser's section state, which `xmin` cannot see.
             *
             * Measured end to end before this line existed: a colleague's correction to a médicament's name,
             * made through `/documents/prescription` while the fiche modal sat open, was reverted by the next
             * fiche save — green toast, no refusal, edit gone.
             *
             * So the expected version is ROUND-TRIPPED by the modal, which reads the document when it opens.
             * `SetExpectedVersion` puts it in the UPDATE's WHERE clause, and a mismatch raises the
             * `ConflictException` both fiche commands deliberately let through — which is what the modal's
             * `useConflict` is for. ⚠️ 0 means « not supplied » and turns the check off, so every older client
             * and every server-internal writer behaves exactly as it did.
             */
            unitOfWork.SetExpectedVersion(existing, expectedVersion);
            existing.Update(record.InterventionDate, composed.Document.ContentJson);
            await documents.UpdateAsync(existing, cancellationToken);
            return (existing.Id, composed.Summary);
        }

        await documents.AddAsync(composed.Document, cancellationToken);
        return (composed.Document.Id, composed.Summary);
    }

    /// <summary>
    /// Composes one ordonnance as an <b>unsaved</b> <c>MedicalDocument</c>, plus the row-sized labels of what
    /// it prescribes.
    ///
    /// <para>
    /// ⚠️ <b>It returns an entity precisely so the preview and the emitted document cannot diverge.</b> Three
    /// callers share it: the create path adds it, the update path copies its <c>ContentJson</c> onto the
    /// document already on file, and <c>PreviewFicheOrdonnanceQuery</c> hands it to
    /// <c>MedicalDocumentPdfMapping.ToPdfData</c> without ever persisting it. That is what makes « aperçu »
    /// inside the fiche byte-identical to the paper the next save produces — the alternative, composing the
    /// preview in the browser, is exactly how the document editor came to send <c>"[Nom du cabinet]"</c> to a
    /// legal document.
    /// </para>
    /// </summary>
    public static async Task<(MedicalDocument Document, IReadOnlyList<string> Summary)> ComposeAsync(
        string documentType,
        IReadOnlyList<PrescriptionLineInput> lines,
        FicheOrdonnanceContext context,
        Patient patient,
        Guid clinicId,
        string? callerUserId,
        IClinicRepository clinics,
        IDoctorRepository doctors,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(patient);

        var contentJson = documentType == DocumentTypes.Examens
            ? PrescriptionLines.BuildExamensContentJson(lines, context.InterventionDate)
            : PrescriptionLines.BuildPrescriptionContentJson(
                new PrescriptionInput { Lines = lines.ToList() },
                context.InterventionDate);

        // Derived from the SERIALISED content, not from the input, so the row and the paper can never disagree.
        var summary = SummaryOf(documentType, contentJson);

        // The cachet, the n° CNOMDT, the cabinet's city and e-mail. Issued in the name of the practitioner the
        // FICHE attributes the work to (record.DoctorId, resolved through PractitionerAttribution) — which is
        // the right answer and the one the client could not supply: a secretary recording a dentist's séance
        // must not put their own identity on the prescription.
        var snapshot = await PractitionerRenderSnapshot.ResolveAsync(
            context.DoctorId, callerUserId, clinicId, doctors, clinics, cancellationToken);
        contentJson = snapshot.ApplyTo(contentJson);

        var clinic = await clinics.GetByIdAsync(clinicId, cancellationToken);
        var doctor = context.DoctorId.HasValue
            ? await doctors.GetByIdAsync(context.DoctorId.Value, cancellationToken)
            : null;

        if (doctor is not null && doctor.ClinicId != clinicId)
        {
            // Cannot happen through PractitionerAttribution, which filters on the clinic's own doctors — but a
            // document names its issuer, so the tenant check is repeated rather than assumed.
            logger.LogWarning(
                "Refusing to issue an ordonnance in the name of doctor {DoctorId}, who belongs to another clinic",
                doctor.Id);
            doctor = null;
        }

        if (doctor is null && !string.IsNullOrEmpty(callerUserId))
        {
            /*
             * ⚠️ The caller's own record, as the LAST resort — the same fall-through
             * `PractitionerRenderSnapshot.ResolveAsync` already applies, and it has to be here too because the
             * two halves of one identity were being resolved by different rules.
             *
             * The snapshot (the cachet, the n° CNOMDT) fell back to the caller while this — the printed NAME and
             * spécialité — did not, so a document could carry one practitioner's cachet above the words « Dr. »
             * and nothing: internally inconsistent, on a form whose entire purpose is to carry that identity.
             * That is the defect `ResolveAsync`'s own remark describes and it survived one level up.
             *
             * ⚠️ It was found through the APERÇU rather than the save. On a fiche not yet created there is no
             * `record.DoctorId` to send — `PractitionerAttribution` sets it during the save, from this same
             * caller — so the preview named nobody while the document the save would emit names them. The whole
             * promise of composing the preview through this method is that it cannot differ from the paper, and
             * without this branch it differed in exactly the field a prescription is signed with.
             */
            doctor = await doctors.GetByUserIdAsync(callerUserId, cancellationToken);

            if (doctor is not null && doctor.ClinicId != clinicId)
            {
                doctor = null;
            }
        }

        var document = new MedicalDocument(
            Guid.NewGuid(),
            context.PatientId,
            clinicId,
            documentType,
            context.InterventionDate,
            $"{patient.FirstName} {patient.LastName}".Trim(),
            // Named PatientAge, holds a formatted date de naissance — see the field's own remark. Same format
            // as CreateMedicalDocumentCommand, so the stored and downloaded PDFs agree.
            patient.DateOfBirth?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture),
            contentJson,
            clinic?.Name ?? string.Empty,
            clinic?.Address ?? string.Empty,
            clinic?.Phone ?? string.Empty,
            doctor is not null ? $"Dr. {doctor.FullName}" : string.Empty,
            DoctorSpecialtyLabels.Label(doctor?.Specialty),
            isDraft: false,
            recipientDoctorName: null,
            recipientDoctorSpecialty: null,
            fileId: null,
            // Both links are set. The appointment keeps the « Séance » column of the Documents tab working;
            // the fiche id is what the séance history joins on, because the appointment is null on every fiche
            // entered outside the agenda. See MedicalDocument.DentalRecordId.
            appointmentId: context.AppointmentId,
            dentalRecordId: context.DentalRecordId);

        return (document, summary);
    }

    /// <summary>Row labels read from whichever array the document type stores its lines in.</summary>
    private static IReadOnlyList<string> SummaryOf(string documentType, string? contentJson) =>
        documentType == DocumentTypes.Examens
            ? PrescriptionLines.ExamenShortLabels(contentJson)
            : PrescriptionLines.ShortLabels(contentJson);

    /// <summary>
    /// The ordonnance of <paramref name="documentType"/> this fiche already owns, if any — its own, else a
    /// legacy one that names only the shared appointment. Returns the earliest when a visit somehow carries
    /// several, so a re-save keeps editing the same document instead of alternating between them.
    /// </summary>
    public static async Task<MedicalDocument?> FindAsync(
        DentalRecord record,
        Guid clinicId,
        string documentType,
        IMedicalDocumentRepository documents,
        CancellationToken cancellationToken)
    {
        var owned = await FindAllAsync(record, clinicId, documents, cancellationToken);
        return Pick(owned, documentType, record);
    }

    private static async Task<IReadOnlyList<MedicalDocument>> FindAllAsync(
        DentalRecord record,
        Guid clinicId,
        IMedicalDocumentRepository documents,
        CancellationToken cancellationToken) =>
        await documents.GetFicheOrdonnancesForDentalRecordsAsync(
            clinicId,
            new[] { record.Id },
            record.AppointmentId.HasValue ? new[] { record.AppointmentId.Value } : Array.Empty<Guid>(),
            cancellationToken);

    /// <summary>
    /// ⚠️ The legacy appointment-only fallback is applied to <c>prescription</c> only, and deliberately: the
    /// <c>examens</c> type is newer than <c>MedicalDocument.DentalRecordId</c>, so a demande d'examens with a
    /// null fiche id cannot exist. Letting the fallback run for it would mean a document some *other* fiche of
    /// the same visit created being claimed and overwritten here.
    /// </summary>
    private static MedicalDocument? Pick(
        IReadOnlyList<MedicalDocument> owned,
        string documentType,
        DentalRecord record)
    {
        var ofType = owned.Where(d => string.Equals(
            d.DocumentType, documentType, StringComparison.OrdinalIgnoreCase)).ToList();

        var own = ofType.FirstOrDefault(d => d.DentalRecordId == record.Id);
        if (own is not null || documentType == DocumentTypes.Examens)
        {
            return own;
        }

        return ofType.FirstOrDefault();
    }
}

/// <summary>
/// The scalars an ordonnance is composed from. A <c>DentalRecord</c> supplies them on the save path; the
/// preview path has no saved record and supplies them directly, which is the whole reason this exists as a
/// value rather than the entity.
/// </summary>
public sealed record FicheOrdonnanceContext(
    Guid PatientId,
    Guid? DoctorId,
    Guid? AppointmentId,
    Guid? DentalRecordId,
    DateTime InterventionDate)
{
    public static FicheOrdonnanceContext From(DentalRecord record) => new(
        record.PatientId,
        record.DoctorId,
        record.AppointmentId,
        record.Id,
        record.InterventionDate);
}

/// <summary>
/// What the fiche's save did to its ordonnances: the documents that exist afterwards (if any) and the
/// row-sized labels of what each prescribes. All four are read back onto <c>DentalRecordDto</c> so the modal
/// can round-trip both documents on the next save and the séance history can name the prescription without a
/// second request.
/// </summary>
public sealed record FicheOrdonnanceResult(
    Guid? DocumentId,
    IReadOnlyList<string> Summary,
    Guid? ExamensDocumentId,
    IReadOnlyList<string> ExamensSummary)
{
    /// <summary>
    /// The caller said nothing about prescriptions, so nothing was read and nothing was written.
    ///
    /// <para>
    /// ⚠️ This is the <b>absent</b> payload, not the empty one, and the distinction is the repo's standing
    /// tri-state rule for an update DTO: omitting a key means « unchanged ». A save that carries no
    /// <c>Prescription</c> at all — an older client, or any of the several server-internal writers — must not
    /// even look for a document, let alone touch one. An <i>empty</i> payload is a statement (« nothing is
    /// prescribed at this séance ») and does go through the emitter, which is what lets the response report the
    /// ordonnances the fiche already owns.
    /// </para>
    /// </summary>
    public static readonly FicheOrdonnanceResult None = new(
        null, Array.Empty<string>(), null, Array.Empty<string>());
}
