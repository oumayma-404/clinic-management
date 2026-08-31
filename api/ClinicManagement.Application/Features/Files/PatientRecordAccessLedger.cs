using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Files;

/// <summary>
/// Records that a patient's own content <b>left the server</b> — a radiograph, a scan, a medical document —
/// naming who took it, whose file it was, and when.
///
/// <para><b>The gap this closes.</b> <c>AuditAction</c> held Insert/Update/Delete alone, written by a
/// <c>SaveChanges</c> interceptor that by construction cannot see a read. So the ledger recorded every change to
/// a dossier and <b>nothing at all</b> about who had looked at one: downloading a patient's x-ray left no trace
/// of any kind. « Qui a ouvert ce dossier ? » is the first question a regulator or a patient complaint puts to a
/// medical record, and the only question that means anything against a colleague who legitimately *has*
/// access — the insider case is the one an access control cannot answer and a journal can.</para>
///
/// <para>⚠️ <b>Content leaving, not screens opened.</b> Auditing every render would write hundreds of rows a day
/// per practice and bury the record it exists to make readable — the argument that keeps <c>Notification</c> off
/// the interceptor, applied here. Listing a patient's files is not recorded; taking one away is.</para>
///
/// <para>⚠️ <b>NOT best-effort, like its two siblings</b> (<see cref="Backup.ArchiveAccessLedger"/> and
/// <c>ListExportLedger</c>): the operation <i>is</i> what is being recorded, so a download that succeeds
/// unrecorded makes the guarantee false. The objection — « a dentist mid-consultation must never be refused an
/// x-ray because a log write failed » — is real and is answered by the shape of the failure rather than by
/// swallowing it: this row is written to the same database the file's metadata was just read from, so if it
/// cannot be written the read had already failed. There is no state where the file is reachable and the ledger
/// is not.</para>
///
/// <para>⚠️ <b>The file's NAME is not recorded.</b> <c>DocumentFileNaming</c> composes one from the patient's
/// name and the document type, so it is PHI — the same reason <c>LogMask.FileName</c> exists. The row names the
/// patient by <b>id</b> and the file by <b>id</b>; a reader with access to the journal has access to the dossier
/// those resolve to, and a reader without it learns nothing.</para>
/// </summary>
public static class PatientRecordAccessLedger
{
    /// <summary>A patient's uploaded file — radiograph, scan, photograph, imported document.</summary>
    public const string FileEntityType = "PatientFileAccess";

    /// <summary>A document the product generated — ordonnance, certificat, bulletin CNAM, arrêt de travail.</summary>
    public const string DocumentEntityType = "MedicalDocumentAccess";

    /// <summary>What a caller sees when the row cannot be written. Beside its code, for one statement.</summary>
    public const string UnrecordableMessage =
        "Ce téléchargement n'a pas pu être inscrit au journal du cabinet, et un accès non tracé au dossier d'un "
        + "patient ne peut pas être autorisé. Le fichier est intact — réessayez, puis contactez votre hébergeur "
        + "si le problème persiste.";

    /// <summary>The code a client branches on.</summary>
    public const string UnrecordableCode = "access_not_recorded";

    /// <summary>
    /// Records the access and commits it, <b>before</b> the bytes are handed back.
    /// </summary>
    /// <param name="entityType"><see cref="FileEntityType"/> or <see cref="DocumentEntityType"/>.</param>
    /// <param name="patientId">Whose dossier this belongs to — what makes the row answerable per patient.</param>
    /// <param name="itemId">The file or document taken. An id, never its name; see the ⚠️ on the class.</param>
    /// <param name="what">
    /// A short French noun for the journal (« Radiographie ou pièce jointe », « Document médical »). Server-side
    /// on <c>AuditLabels</c>' reasoning: a client-side map is a second list to extend.
    /// </param>
    /// <exception cref="Exception">
    /// Anything the save throws travels up. The caller turns it into <see cref="UnrecordableMessage"/> — it must
    /// not be swallowed, which is the whole difference between this and a notification.
    /// </exception>
    public static async Task RecordAsync(
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        AuditActor actor,
        Guid clinicId,
        string entityType,
        Guid patientId,
        Guid itemId,
        string what,
        DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            clinicId,
            actor.UserId,
            actor.Email,
            entityType,
            // ⚠️ The PATIENT's id, not the file's, and that is the whole usefulness of the row. « Which files
            // left this dossier? » is the question somebody asks, and `AuditController` already filters on
            // entityId — keying on the file would make the journal answerable only to somebody who already knew
            // which file to ask about. The file's own id is in the summary.
            patientId.ToString(),
            AuditAction.Read,
            $"{what} téléchargé(e) — {itemId}",
            occurredAt);

        await auditEntries.AddRangeAsync(new[] { entry }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
