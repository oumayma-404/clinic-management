using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One row per mutated aggregate: who changed what, when. The ledger that lets an owner answer « qui a supprimé
/// ce patient ? », « qui a annulé cette facture ? », « qui a effacé cette dépense ? » — questions the product
/// could not answer at all before it, since the only attributable actions in the whole system were voiding a
/// payment and voiding an installment (an avoir recorded no actor).
///
/// <para><b>Why a ledger and not <c>CreatedBy</c>/<c>ModifiedBy</c> on <see cref="Entity{TId}"/>.</b> Columns on
/// the base class look cheaper and are not: they turn attribution into a <b>write-path obligation on 38
/// entities</b>, and any writer that forgets one produces an unattributed row indistinguishable from a
/// legitimately unattributable one. They also answer nothing about a delete, which is the question most often
/// asked. A `SaveChangesInterceptor` sees every save by construction — it cannot be forgotten, which is the
/// whole argument.</para>
///
/// <para><b>What it deliberately is not.</b> Not a temporal table and not an undo log: it does not store the old
/// and new value of every column, so it cannot reconstruct a record. It stores the identity of the change and,
/// for the two cases where the identity alone says too little, a compact summary — see
/// <see cref="ChangedFields"/>.</para>
/// </summary>
public class AuditEntry : Entity<Guid>
{
    /// <summary>
    /// A changed-field summary longer than this is truncated. The column is unbounded text, so the cap is not
    /// about storage: a summary is meant to be *read* in a table row, and a wall of every property of a fat
    /// aggregate tells an owner less than four field names do.
    /// </summary>
    public const int MaxChangedFieldsLength = 512;

    /// <summary>
    /// The clinic the mutated aggregate belongs to — <b>nullable</b>, and that is a decision worth explaining.
    ///
    /// <para>Almost every aggregate carries a <c>ClinicId</c>, and for <c>Clinic</c> and
    /// <c>ClinicReminderSettings</c> the id <em>is</em> the clinic. The residue is real though: a console verb or
    /// a background job can mutate a row with no clinic in scope and nothing on the row to derive one from.
    /// Writing <c>Guid.Empty</c> there would put a sentinel into the <c>(ClinicId, OccurredAt)</c> index that
    /// reads as a real clinic to every query — the same class of defect as the four placeholder contact literals
    /// this codebase spent a feature retiring. A null says « unattributed », which is true and queryable.</para>
    /// </summary>
    public Guid? ClinicId { get; private set; }

    /// <summary>
    /// The actor: a <c>User.Id</c> (an Auth0 <c>sub</c> or <c>local|{guid}</c>) for a request, or
    /// <c>job|&lt;name&gt;</c> for a background job / console verb. Never null: a mutation nobody can be named
    /// for is still worth a row, and the row says <c>job|unknown</c> rather than being dropped — a gap in the
    /// ledger is indistinguishable from « nothing happened », which is the one thing it must never claim.
    /// </summary>
    public string UserId { get; private set; } = string.Empty;

    /// <summary>
    /// The actor's email when the token carried one. Denormalised on purpose: it is the only thing that still
    /// names the person after their account is deleted, which is exactly when the ledger is being read.
    /// </summary>
    public string? UserEmail { get; private set; }

    /// <summary>The CLR name of the aggregate — <c>Patient</c>, <c>Invoice</c>, <c>Expense</c>.</summary>
    public string EntityType { get; private set; } = string.Empty;

    /// <summary>
    /// The aggregate's primary key, as text. Text and not <c>Guid</c> because <c>User</c> is keyed by a string
    /// (the Auth0 <c>sub</c>) while everything else is a <c>Guid</c> — one column that holds both beats a
    /// nullable pair where every reader has to know which one to look at.
    /// </summary>
    public string EntityId { get; private set; } = string.Empty;

    public AuditAction Action { get; private set; }

    /// <summary>
    /// A compact, human-readable summary of what moved — <c>Status: Issued → Cancelled; AmountCollected</c> — or
    /// the deleted row's identifying values. Null for an insert, where the action and the entity already say
    /// everything a summary could.
    /// </summary>
    public string? ChangedFields { get; private set; }

    /// <summary>When it happened, UTC. Ordered on descending — the ledger is read newest-first.</summary>
    public DateTime OccurredAt { get; private set; }

    private AuditEntry() { } // For EF Core

    public AuditEntry(
        Guid? clinicId,
        string userId,
        string? userEmail,
        string entityType,
        string entityId,
        AuditAction action,
        string? changedFields,
        DateTime occurredAt)
        : base(Guid.NewGuid())
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("Un acteur est requis pour une entrée d'audit.", nameof(userId));
        if (string.IsNullOrWhiteSpace(entityType))
            throw new ArgumentException("Le type d'entité est requis pour une entrée d'audit.", nameof(entityType));
        if (string.IsNullOrWhiteSpace(entityId))
            throw new ArgumentException("L'identifiant de l'entité est requis pour une entrée d'audit.", nameof(entityId));

        ClinicId = clinicId;
        UserId = userId;
        UserEmail = userEmail;
        EntityType = entityType;
        EntityId = entityId;
        Action = action;
        ChangedFields = Truncate(changedFields);
        OccurredAt = occurredAt;
    }

    /// <summary>
    /// Truncation is the entity's business, not the interceptor's: the cap is a property of the column and the
    /// row would otherwise depend on every future caller remembering it. An elided summary keeps its « … » so a
    /// reader can tell a short list from a cut one.
    /// </summary>
    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= MaxChangedFieldsLength
            ? trimmed
            : trimmed[..(MaxChangedFieldsLength - 1)] + "…";
    }
}
