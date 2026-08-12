using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// The five states a cabinet's WhatsApp sender can be in, as the practice is told them (AC-1.4).
/// </summary>
public enum MessagingSenderState
{
    /// <summary>No WhatsApp connection at all — the state every cabinet starts in.</summary>
    NotConnected = 0,

    /// <summary>Connected, and Meta is still reviewing the reminder template (AC-1.5).</summary>
    PendingReview = 1,

    /// <summary>Connected and the template is approved. The <b>only</b> state that can send.</summary>
    Ready = 2,

    /// <summary>Meta refused the template. The vendor's problem to fix, not the practice's (FR-7, EC-10).</summary>
    TemplateRefused = 3,

    /// <summary>The connection or the number is in an error state — Meta has stopped it, or onboarding failed.</summary>
    Suspended = 4
}

/// <summary>
/// The one derivation of AC-1.4's five states from what is actually stored, and their French wording.
///
/// <para><b>⚠️ It exists so that « connecté » can never be presented as « prêt à envoyer ».</b> Those are two
/// different facts — a cabinet whose template Meta is still reviewing is connected and cannot send a thing — and the
/// clinic card, the console file and the current-month read all state a sender state, so a second derivation is how
/// two screens come to disagree about whether a practice's reminders are going out.</para>
///
/// <para><b>⚠️ The template status is nullable, and null is not <see cref="WhatsAppTemplateStatus.NotSubmitted"/>.</b>
/// Nothing stores a per-cabinet template state until Part 4 (§ 33), so before then the answer is genuinely
/// <i>unknown</i> and the connection alone decides. Defaulting to <c>NotSubmitted</c> instead would report « en
/// attente de validation » for every cabinet that is sending perfectly well today on the install's own pre-approved
/// template — a statement about us, rendered as a statement about them, which is the AC-2.4 mistake one field over.
/// When Part 4 passes a real value the null branch stops being reachable for a connected cabinet.</para>
/// </summary>
public static class MessagingSender
{
    /// <param name="template">
    /// The stored template state, or <b>null</b> where this deployment does not track one yet (see the ⚠️ above).
    /// </param>
    public static MessagingSenderState From(WhatsAppConnectionStatus connection, WhatsAppTemplateStatus? template)
    {
        // The connection is asked first, and Error outranks everything: a number Meta has stopped cannot send
        // whatever its template says, and « modèle refusé » would point the practice at the wrong thing.
        switch (connection)
        {
            case WhatsAppConnectionStatus.NotConnected:
                return MessagingSenderState.NotConnected;
            case WhatsAppConnectionStatus.Error:
                return MessagingSenderState.Suspended;
        }

        return template switch
        {
            null => MessagingSenderState.Ready,
            WhatsAppTemplateStatus.Approved => MessagingSenderState.Ready,
            WhatsAppTemplateStatus.Rejected => MessagingSenderState.TemplateRefused,
            // Paused and Disabled are Meta withdrawing a template it had approved, which is a refusal from the
            // practice's point of view and carries the same contact route; NotSubmitted and PendingReview are both
            // « we are waiting », and distinguishing them clinic-side would ask the practice to care which.
            WhatsAppTemplateStatus.Paused or WhatsAppTemplateStatus.Disabled => MessagingSenderState.TemplateRefused,
            _ => MessagingSenderState.PendingReview,
        };
    }

    /// <summary>
    /// The state in words, never by colour alone (NFR accessibility). Server-side on <c>SubscriptionLabels</c>'
    /// precedent: a client-side map is a second list to extend, and the one that forgets a member renders a raw
    /// <c>TemplateRefused</c> to a dentist.
    /// </summary>
    public static string Label(MessagingSenderState state) => state switch
    {
        MessagingSenderState.NotConnected => "WhatsApp n'est pas connecté",
        MessagingSenderState.PendingReview => "En attente de validation par Meta",
        MessagingSenderState.Ready => "Prêt à envoyer",
        MessagingSenderState.TemplateRefused => "Modèle de message refusé",
        MessagingSenderState.Suspended => "Envoi suspendu par Meta",
        _ => "État inconnu",
    };
}
