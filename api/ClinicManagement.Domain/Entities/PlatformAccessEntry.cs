using ClinicManagement.Domain.Common;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Domain.Entities;

/// <summary>
/// One thing a console account did to one cabinet (<c>platform-console</c> FR-5, AC-7.3) — the vendor's own
/// append-only ledger.
///
/// <para><b>Why this is not <see cref="AuditEntry"/>.</b> That ledger answers « who at this cabinet changed this
/// record? » and is read by that cabinet's own admin at <c>GET /api/audit</c>; its actor is a clinic user and its
/// rows are the practice's history. This one answers the opposite question — « what did the <i>vendor</i> do, and
/// to whom? » — its actor belongs to no cabinet, and its most important row (a detail simply being <b>read</b>)
/// is not a mutation at all, so nothing in a save-interceptor could ever see it.</para>
///
/// <para>⚠️ <b>No foreign key to <c>Clinics</c>, and the cabinet's name is copied in.</b> The same decision
/// <see cref="AuditEntry"/> made and for a stronger reason here: a cascade would erase the record that somebody
/// looked at a cabinet that has since been closed, which is precisely the row anyone auditing this console would
/// want. <see cref="ClinicName"/> is denormalised so the journal can still name it (EC-13), and
/// <see cref="AccountEmail"/> likewise so a deactivated console account's rows stay readable.</para>
///
/// <para>⚠️ <b>Append-only by construction, not by convention.</b> There is no mutator on this type, no update or
/// delete on its repository, and no write endpoint — the same shape <c>AuditController</c> has. A ledger somebody
/// can correct is not evidence.</para>
/// </summary>
public class PlatformAccessEntry : AggregateRoot<Guid>
{
    public const int MaxIdempotencyKeyLength = 100;

    /// <summary>The console account that acted. Recorded even after that account is deactivated.</summary>
    public Guid PlatformAccountId { get; private set; }

    /// <summary>The account's address at the time, so a row stays readable without joining a live account.</summary>
    public string AccountEmail { get; private set; } = string.Empty;

    /// <summary>The cabinet acted on. Deliberately not a foreign key — see the remarks above.</summary>
    public Guid ClinicId { get; private set; }

    /// <summary>The cabinet's name at the time, so a closed cabinet is still named rather than shown as a GUID.</summary>
    public string ClinicName { get; private set; } = string.Empty;

    public PlatformAccessAction Action { get; private set; }

    public DateTime OccurredAt { get; private set; }

    /// <summary>
    /// The <c>SubscriptionPeriod</c> this action produced or acted on, for the rows that have one (Part 4's grant,
    /// Parts 5–6's corrections). Null for a plain <see cref="PlatformAccessAction.ViewedClinic"/>.
    ///
    /// <para>Deliberately <b>not</b> a foreign key, for the reason the whole type has none: the ledger outlives the
    /// cabinet whose rows it names, and a cascade would erase the record of a payment taken for a practice that has
    /// since closed.</para>
    /// </summary>
    public Guid? SubscriptionPeriodId { get; private set; }

    /// <summary>
    /// The <c>MessagingAllowanceEntry</c> this action produced or acted on
    /// (<c>vendor-whatsapp-messaging-quota</c> US-6/US-7), or null for every other row.
    ///
    /// <para>⚠️ <b>Its own column rather than a reuse of <see cref="SubscriptionPeriodId"/>.</b> Both name « the thing
    /// the vendor was paid for », and sharing one column would have been one line — which is why it is refused: the
    /// journal would then assert that a forfait de rappels extended the cabinet's right to record work, and a replay
    /// keyed on <see cref="IdempotencyKey"/> would hand back the wrong kind of id. It is the same argument
    /// <c>PlatformReadShape</c> makes about not overloading <c>Note</c>/<c>Reference</c>: a semantic overload is not a
    /// free pass.</para>
    ///
    /// <para>Not a foreign key either, for the reason the whole type has none.</para>
    /// </summary>
    public Guid? MessagingAllowanceEntryId { get; private set; }

    /// <summary>
    /// The client's own key for the write this row records — <b>unique across the ledger</b>, which is what makes
    /// « a double-click produces one entry » (AC-4.6) a property of the database rather than of a handler winning a
    /// race. Null for every row that is not a keyed write.
    ///
    /// <para><b>Why the key lives on the ledger and not in a table of its own.</b> Every console write already
    /// produces exactly one row here, in the same transaction as the write itself, so the ledger is already the
    /// « one row per console action » table an idempotency store would duplicate. It also makes the replay
    /// answerable: the row names the entry that was created, so a repeated submission returns the first outcome
    /// instead of guessing at it.</para>
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public const int MaxReasonLength = 500;

    /// <summary>
    /// The clinic account this action was performed <b>on</b>, where the action names an individual —
    /// <see cref="PlatformAccessAction.SecondFactorReset"/> today, and nothing else. Null for every row that acts
    /// on the cabinet as a whole.
    ///
    /// <para>⚠️ <b>An id and not an address.</b> The address on the row would go stale the moment the person
    /// changes it, and this ledger's other denormalisations (<see cref="ClinicName"/>,
    /// <see cref="AccountEmail"/>) copy values in precisely because the <i>source</i> may vanish — a clinic user
    /// cannot, since the row is cascade-deleted with the cabinet either way. <c>TargetEmail</c> beside it carries
    /// the address as it stood, for the reading a year later.</para>
    ///
    /// <para>Not a foreign key, for the reason the whole type has none.</para>
    /// </summary>
    public string? TargetUserId { get; private set; }

    /// <summary>The target's address at the time, so the row names a person rather than a string key.</summary>
    public string? TargetEmail { get; private set; }

    /// <summary>
    /// The motif the acting account wrote, for the actions that demand one.
    ///
    /// <para>⚠️ <b>Only for a motif with nowhere else to live</b>, which today means the second-factor reset alone.
    /// A suspension's motif belongs on the entitlement and a cancellation's on the entry it strikes through, and
    /// copying either here would be the semantic overload <see cref="MessagingAllowanceEntryId"/>'s own remarks
    /// refuse — two rows then state the same fact and the day they disagree there is no way to tell which is
    /// right. A reset has no domain row at all: <c>DisableTotp</c> keeps no trace, so this column is the only
    /// possible home for « pourquoi ? ».</para>
    /// </summary>
    public string? Reason { get; private set; }

    private PlatformAccessEntry() { } // For EF Core

    public PlatformAccessEntry(
        Guid platformAccountId,
        string accountEmail,
        Guid clinicId,
        string clinicName,
        PlatformAccessAction action,
        DateTime occurredAt,
        Guid? subscriptionPeriodId = null,
        string? idempotencyKey = null,
        Guid? messagingAllowanceEntryId = null,
        string? targetUserId = null,
        string? targetEmail = null,
        string? reason = null)
        : base(Guid.NewGuid())
    {
        if (platformAccountId == Guid.Empty)
            throw new ArgumentException("Un compte console est requis pour une entrée du journal.", nameof(platformAccountId));

        if (clinicId == Guid.Empty)
            throw new ArgumentException("Un cabinet est requis pour une entrée du journal.", nameof(clinicId));

        PlatformAccountId = platformAccountId;
        AccountEmail = accountEmail?.Trim() ?? string.Empty;
        ClinicId = clinicId;
        ClinicName = clinicName?.Trim() ?? string.Empty;
        Action = action;
        OccurredAt = occurredAt;
        SubscriptionPeriodId = subscriptionPeriodId;
        MessagingAllowanceEntryId = messagingAllowanceEntryId;

        var key = idempotencyKey?.Trim();
        if (key is { Length: > MaxIdempotencyKeyLength })
        {
            throw new ArgumentException(
                $"La clé d'idempotence dépasse {MaxIdempotencyKeyLength} caractères.", nameof(idempotencyKey));
        }

        // Blank collapses to null rather than to "": the column's unique index treats every null as distinct, so a
        // handful of unkeyed rows carrying an empty string would collide with each other.
        IdempotencyKey = string.IsNullOrWhiteSpace(key) ? null : key;

        var motif = reason?.Trim();
        if (motif is { Length: > MaxReasonLength })
        {
            throw new ArgumentException(
                $"Le motif dépasse {MaxReasonLength} caractères.", nameof(reason));
        }

        // Blank collapses to null on all three, so « no motif was given » and « an empty one was » are one state
        // rather than two that read differently on the journal.
        TargetUserId = string.IsNullOrWhiteSpace(targetUserId) ? null : targetUserId.Trim();
        TargetEmail = string.IsNullOrWhiteSpace(targetEmail) ? null : targetEmail.Trim();
        Reason = string.IsNullOrWhiteSpace(motif) ? null : motif;
    }
}
