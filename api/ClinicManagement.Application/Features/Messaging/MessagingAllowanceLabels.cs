using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// The French wording of the messaging feature's closed enums, in one place — <c>SubscriptionLabels</c>' counterpart.
///
/// <para>Server-side for that file's reason: a client-side map is a second list to extend, and the one that forgets a
/// member renders a raw <c>TopUp</c> to whoever is reading the screen. <c>MessagingSender.Label</c> already does this
/// for the sender state; these are the two enums the console and the verbs also have to say out loud.</para>
///
/// <para>⚠️ An unmapped member falls through to its own name rather than to « Inconnu », so a member added later reads
/// as slightly technical instead of disappearing.</para>
/// </summary>
public static class MessagingAllowanceLabels
{
    /// <summary>
    /// What kind of allocation an entry is.
    ///
    /// <para>« Forfait mensuel » / « Complément ponctuel » rather than a literal translation of the enum: the
    /// distinction a reader needs is <i>does this repeat every month or not</i>, which « standing » and « top-up » do
    /// not carry in French.</para>
    /// </summary>
    public static string Kind(MessagingAllowanceKind kind) => kind switch
    {
        MessagingAllowanceKind.Standing => "Forfait mensuel",
        MessagingAllowanceKind.TopUp => "Complément ponctuel",
        _ => kind.ToString()
    };

    /// <summary>
    /// The state of the cabinet's Meta message template, as the <b>vendor</b> is told it (FR-7a).
    ///
    /// <para>⚠️ Deliberately more granular than <c>MessagingSender.Label</c>, which the <i>practice</i> reads: that one
    /// folds <c>NotSubmitted</c> and <c>PendingReview</c> into « en attente de validation », because a cabinet should
    /// not have to care which — while the vendor is precisely who acts on the difference (one needs submitting, the
    /// other needs waiting).</para>
    /// </summary>
    public static string TemplateStatus(WhatsAppTemplateStatus status) => status switch
    {
        WhatsAppTemplateStatus.NotSubmitted => "Modèle non soumis",
        WhatsAppTemplateStatus.PendingReview => "Modèle en cours de validation",
        WhatsAppTemplateStatus.Approved => "Modèle approuvé",
        WhatsAppTemplateStatus.Rejected => "Modèle refusé",
        WhatsAppTemplateStatus.Paused => "Modèle suspendu par Meta",
        WhatsAppTemplateStatus.Disabled => "Modèle désactivé par Meta",
        _ => status.ToString()
    };
}
