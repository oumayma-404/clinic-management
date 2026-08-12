using ClinicManagement.Application.Common.Interfaces;
using ClinicManagement.Domain.Entities;
using ClinicManagement.Domain.Enums;
using ClinicManagement.Domain.Repositories;

namespace ClinicManagement.Application.Features.Backup;

/// <summary>
/// Records that a practice's <b>whole record</b> left the building — who, which practice, when, and whether it
/// was actually delivered (<c>hosted-security-hardening</c> FR-4.2).
///
/// <para><b>It writes into the audit ledger rather than a table of its own</b>, and that is the point rather than
/// a saving: Part D's chain already makes an <c>AuditEntry</c> impossible to alter or remove without
/// <c>verify-schema</c> naming it, which is exactly AC-4's « the record of it cannot be silently removed ». A new
/// table would have needed its own tamper-evidence, its own migration and its own reader to be worth the same.
/// The download is not a <c>SaveChanges</c> mutation, so the interceptor never sees it — which is why this
/// appends explicitly.</para>
///
/// <para>⚠️ <b>The request row is NOT best-effort.</b> If it cannot be written the download does not happen, and
/// the refusal is a French sentence. <c>PlatformAccessLedger</c>'s reasoning applies verbatim: the operation
/// <i>is</i> what is being recorded, unlike <c>INotificationGenerator</c>, which swallows because the operation it
/// follows has already committed. An unrecorded export succeeding makes the guarantee false.</para>
///
/// <para>⚠️ <b>Delivery is a second row, not an update.</b> The ledger is append-only by construction — there is
/// no mutator on <see cref="AuditEntry"/> and no update on its repository — so « livrée » is recorded beside
/// « demandée » rather than over it. It is also the honest shape: a download that aborts at 90 % really did
/// happen and really did not arrive, and both facts are worth keeping.</para>
/// </summary>
public static class ArchiveAccessLedger
{
    /// <summary>The <c>EntityType</c> both rows carry, so « Journal d'activité » and any later reader agree.</summary>
    public const string EntityType = "ClinicArchive";

    /// <summary>What a caller sees when the row cannot be written. Beside the code, in one place, for the reason
    /// <c>SubscriptionRefusals</c> states: a sentence and its code are one statement.</summary>
    public const string UnrecordableMessage =
        "Le téléchargement de l'archive n'a pas pu être inscrit au journal du cabinet, et une exportation "
        + "non tracée ne peut pas être autorisée. Vos données sont intactes — réessayez, puis contactez votre "
        + "hébergeur si le problème persiste.";

    /// <summary>The code a client branches on. Distinct from `archive_invalid`, which is about the file.</summary>
    public const string UnrecordableCode = "archive_not_recorded";

    /// <summary>
    /// Records the request and commits it, <b>before</b> the archive is built (R-14: the file is buffered and
    /// uncapped, and recording afterwards would mean paying for the whole build to then refuse). Returns the
    /// entry's id so the delivery row can name it.
    /// </summary>
    /// <exception cref="Exception">
    /// Anything the save throws travels up. The caller turns it into <see cref="UnrecordableMessage"/> — it must
    /// not be swallowed, which is the whole difference between this and a notification.
    /// </exception>
    public static async Task<Guid> RecordRequestedAsync(
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        AuditActor actor,
        Guid clinicId,
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
            "Archive complète du cabinet demandée",
            occurredAt);

        await auditEntries.AddRangeAsync(new[] { entry }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return entry.Id;
    }

    /// <summary>
    /// Records what became of it: delivered, or not. Names the request row so the two are one story.
    ///
    /// <para>⚠️ <b>This one IS best-effort, and the asymmetry is deliberate.</b> It runs after the response body
    /// has completed, when there is no longer anybody to refuse — the archive has already left. Failing here
    /// would achieve nothing except an exception on a torn-down request; the honest handling is to log it, and
    /// the <i>request</i> row above is what makes the export attributable regardless.</para>
    /// </summary>
    public static async Task RecordDeliveryAsync(
        IAuditEntryRepository auditEntries,
        IUnitOfWork unitOfWork,
        AuditActor actor,
        Guid clinicId,
        Guid requestEntryId,
        bool delivered,
        long bytes,
        DateTime occurredAt,
        CancellationToken cancellationToken = default)
    {
        var entry = new AuditEntry(
            clinicId,
            actor.UserId,
            actor.Email,
            EntityType,
            clinicId.ToString(),
            AuditAction.Update,
            delivered
                ? $"Archive complète du cabinet livrée ({bytes} octets) — demande {requestEntryId}"
                : $"Archive complète du cabinet NON livrée (téléchargement interrompu) — demande {requestEntryId}",
            occurredAt);

        await auditEntries.AddRangeAsync(new[] { entry }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
