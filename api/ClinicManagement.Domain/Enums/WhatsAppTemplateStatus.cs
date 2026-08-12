namespace ClinicManagement.Domain.Enums;

/// <summary>
/// Where Meta's review of the cabinet's French reminder template stands (FR-7). Together with
/// <see cref="WhatsAppConnectionStatus"/> it is what AC-1.4's five clinic-facing states derive from — see
/// <c>MessagingSenderState</c>, which is the one place that derivation happens.
///
/// <para>⚠️ <b>Only <see cref="Approved"/> means the cabinet can send.</b> Every other member holds its reminders
/// under <c>OutboxBlockReason.MessagingTemplateNotReady</c>, consuming nothing (Part 4 § 33a) — which is the whole
/// reason « connecté » must never be presented as « prêt à envoyer ».</para>
///
/// <para>⚠️ <b>Nothing stores this yet.</b> The four <c>ClinicReminderSettings</c> template columns and both of
/// FR-7a's writers (the webhook and the reconciling poll) arrive in Part 4; until then a cabinet's template state is
/// simply unknown, which <c>MessagingSenderState.From</c> takes as a nullable argument rather than guessing
/// <see cref="NotSubmitted"/> — a cabinet sending fine today on the install's own pre-approved template must not read
/// « en attente de validation ».</para>
/// </summary>
public enum WhatsAppTemplateStatus
{
    /// <summary>No template has been submitted on this cabinet's behalf. The state a fresh connection is in.</summary>
    NotSubmitted = 0,

    /// <summary>Submitted and under Meta's review — up to 24 h (AC-1.5).</summary>
    PendingReview = 1,

    /// <summary>Usable. The only member that lets a reminder leave the building.</summary>
    Approved = 2,

    /// <summary>Meta refused it. Recovery is the <b>vendor's</b> action, never the cabinet's (FR-7, EC-10).</summary>
    Rejected = 3,

    /// <summary>Paused by Meta, typically for quality. Recoverable, but not by the practice.</summary>
    Paused = 4,

    /// <summary>Disabled by Meta. Terminal as far as this template is concerned.</summary>
    Disabled = 5
}
