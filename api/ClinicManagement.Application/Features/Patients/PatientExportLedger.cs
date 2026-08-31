using System.Text;
using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Patients;

/// <summary>
/// Records that the practice's <b>patient roster</b> left the building as a CSV — who, which practice, how many
/// rows, and under which filters.
///
/// <para><b>Why this exists, stated plainly.</b> <c>GET /api/patients/export</c> returns twenty columns per
/// patient including <i>Date de naissance</i>, <i>Adresse</i>, <i>Identifiant CNAM</i>, <i>Antécédents
/// médicaux</i> and <i>Allergies</i> — the whole cabinet's identified medical dataset in one file. It carried
/// <b>none</b> of the four controls the whole-clinic ZIP archive carries: no step-up, no rate limit, no audit
/// row, and it is open to every clinic role. The archive is « the practice on a laptop » and is guarded as such;
/// this endpoint is the same data through a different door, and the asymmetry was the finding. Nothing in the
/// product could answer « who took the patient list, and when? ».</para>
///
/// <para><b>It writes into the audit ledger rather than a table of its own</b>, for
/// <see cref="Backup.ArchiveAccessLedger"/>'s reason verbatim: the chain already makes an <see cref="AuditEntry"/>
/// impossible to alter or remove without <c>verify-schema</c> naming it, and a new table would need its own
/// tamper-evidence, migration and reader to be worth the same. A CSV download is not a <c>SaveChanges</c>
/// mutation, so the interceptor never sees it — which is why this appends explicitly.</para>
///
/// <para>⚠️ <b>NOT best-effort.</b> If the row cannot be written the export does not happen, and the refusal is a
/// French sentence. Same rule, same reason: the operation <i>is</i> what is being recorded, unlike
/// <c>INotificationGenerator</c>, which swallows because the operation it follows has already committed. An
/// unrecorded export that succeeds makes the guarantee false.</para>
///
/// <para>⚠️ <b>A sibling of <see cref="Backup.ArchiveAccessLedger"/> rather than a shared abstraction.</b> The
/// two genuinely differ — the archive records a second « delivered / not delivered » row and stamps
/// <c>Clinic.LastArchiveDownloadedAtUtc</c>, neither of which means anything for a CSV that is built and
/// returned in one pass. What must not drift is the <i>rule</i> (append to the chain, refuse if you cannot), and
/// that is held by both files stating it rather than by an abstraction that would have to grow a flag per
/// difference.</para>
/// </summary>
public static class PatientExportLedger
{
    /// <summary>The <c>EntityType</c> the row carries, so « Journal d'activité » can name it.</summary>
    public const string EntityType = "PatientExport";

    /// <summary>
    /// What a caller sees when the row cannot be written. Beside its code, in one place, for the reason
    /// <c>SubscriptionRefusals</c> states: a sentence and its code are one statement.
    /// </summary>
    public const string UnrecordableMessage =
        "L'export de la liste des patients n'a pas pu être inscrit au journal du cabinet, et une exportation "
        + "non tracée ne peut pas être autorisée. Vos données sont intactes — réessayez, puis contactez votre "
        + "hébergeur si le problème persiste.";

    /// <summary>The code a client branches on.</summary>
    public const string UnrecordableCode = "export_not_recorded";

    /// <summary>
    /// Records the export and commits it, <b>before</b> the file is handed back.
    /// </summary>
    /// <param name="rowCount">
    /// How many patients are in the file. Recorded because it is the difference that matters when reading the
    /// journal back: « a colleague exported one filtered row » and « somebody took all 2 300 patients » are the
    /// same action and completely different events.
    /// </param>
    /// <param name="filterSummary">
    /// Which filters were in force, already rendered by <see cref="DescribeFilters"/>. The filter <b>values</b>
    /// are deliberately excluded — a search term on this product is a patient's name, and FR-4.4 keeps those out
    /// of anything durable. What is recorded is that a search was applied, never what was searched for.
    /// </param>
    /// <exception cref="Exception">
    /// Anything the save throws travels up. The caller turns it into <see cref="UnrecordableMessage"/> — it must
    /// not be swallowed, which is the whole difference between this and a notification.
    /// </exception>
    public static async Task<Guid> RecordAsync(
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        AuditActor actor,
        Guid clinicId,
        int rowCount,
        string filterSummary,
        DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            clinicId,
            actor.UserId,
            actor.Email,
            EntityType,
            clinicId.ToString(),
            AuditAction.Insert,
            $"Liste des patients exportée en CSV — {rowCount} ligne(s), {filterSummary}",
            occurredAt);

        await auditEntries.AddRangeAsync(new[] { entry }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return entry.Id;
    }

    /// <summary>
    /// Describes the filters in force without repeating what was typed.
    ///
    /// <para>⚠️ <b>« recherche appliquée », never the term itself.</b> On this product a patient search box holds
    /// a patient's name, so recording the value would put PHI into the one table designed never to be deleted —
    /// the exact thing <c>LogTemplateCoverageTests</c> guards the log file against.</para>
    /// </summary>
    public static string DescribeFilters(
        string? searchTerm,
        DateTime? createdFrom,
        DateTime? createdTo,
        bool flaggedOnly)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            parts.Add("recherche appliquée");
        }

        if (createdFrom.HasValue || createdTo.HasValue)
        {
            parts.Add("filtré par date d'inscription");
        }

        if (flaggedOnly)
        {
            parts.Add("patients signalés uniquement");
        }

        return parts.Count == 0
            ? "sans filtre (tout le cabinet)"
            : string.Join(", ", parts);
    }
}
