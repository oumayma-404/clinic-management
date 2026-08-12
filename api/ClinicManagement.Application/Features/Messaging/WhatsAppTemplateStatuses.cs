using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// Meta's own template-status words → <see cref="WhatsAppTemplateStatus"/>, in one place because FR-7a has
/// <b>two writers</b>: the <c>message_template_status_update</c> webhook (which sends the word as an
/// <c>event</c>) and the reconciling poll (which reads it as a <c>status</c>). Two mappings would let a cabinet's
/// state depend on which writer got there first.
///
/// <para><b>⚠️ An unrecognised word is not <see cref="WhatsAppTemplateStatus.Approved"/>.</b> Meta adds states
/// (<c>IN_APPEAL</c>, <c>PENDING_DELETION</c>, <c>LIMIT_EXCEEDED</c>, <c>ARCHIVED</c>) and this product's rule is
/// that <b>only</b> <c>Approved</c> may send — so anything we cannot read must fall on the holding side, where the
/// consequence is a delayed reminder rather than a message Meta refuses and a unit nobody can account for.</para>
/// </summary>
public static class WhatsAppTemplateStatuses
{
    /// <param name="value">Meta's word, in any casing, or null.</param>
    /// <returns>
    /// The mapped status, or <b>null</b> when the payload carried nothing at all — which is « we learned nothing »
    /// and must not be written over a state we already have (a null status means « unknown », not « not submitted »).
    /// </returns>
    public static WhatsAppTemplateStatus? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant() switch
        {
            "APPROVED" => WhatsAppTemplateStatus.Approved,
            "REJECTED" => WhatsAppTemplateStatus.Rejected,
            "PENDING" or "PENDING_REVIEW" or "IN_APPEAL" => WhatsAppTemplateStatus.PendingReview,
            "PAUSED" => WhatsAppTemplateStatus.Paused,
            "DISABLED" or "ARCHIVED" or "PENDING_DELETION" or "DELETED" => WhatsAppTemplateStatus.Disabled,
            // See the ⚠️ above: an unknown word holds reminders rather than releasing them.
            _ => WhatsAppTemplateStatus.PendingReview,
        };
    }

    /// <summary>
    /// The states Meta may still move by itself — the reconciling poll's candidate set (FR-7a).
    ///
    /// <para>⚠️ <see cref="WhatsAppTemplateStatus.Paused"/> is in it: Meta un-pauses a template whose quality
    /// recovers, and no webhook is guaranteed for that — a cabinet parked there for ever with its reminders held is
    /// exactly the stranding the poll exists to end.</para>
    ///
    /// <para>⚠️ <b>An array rather than a predicate, because the repository's candidate query is SQL.</b> A
    /// <c>switch</c> does not translate, so the alternative was the same set written a second time as a
    /// <c>WHERE</c> clause — and the copy that drifts is the one no compiler checks. <see cref="IsTerminal"/> is
    /// derived from this, so the two answers cannot disagree.</para>
    /// </summary>
    public static readonly WhatsAppTemplateStatus[] AwaitingMeta =
    [
        WhatsAppTemplateStatus.NotSubmitted,
        WhatsAppTemplateStatus.PendingReview,
        WhatsAppTemplateStatus.Paused,
    ];

    /// <summary>Is this a state Meta will not move again by itself? The poll leaves these alone.</summary>
    public static bool IsTerminal(WhatsAppTemplateStatus status) => !AwaitingMeta.Contains(status);
}
