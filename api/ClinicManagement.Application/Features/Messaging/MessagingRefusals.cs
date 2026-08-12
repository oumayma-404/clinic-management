using System.Globalization;
using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Messaging;

/// <summary>
/// The clinic-facing sentences a cabinet meets when its WhatsApp reminder forfait cannot cover a send, each with the
/// machine-readable code beside it (FR-10).
///
/// <para><b>One authority, because the sentence and the code are one statement.</b> The gate writes them onto the
/// parked row, the « Rappels » screen renders them and the recall refusal reuses one; three copies of a French
/// sentence is how a reworded message silently stops matching the code it was paired with — the
/// <c>Contains("déjà facturée")</c> defect this repo deleted. <c>SubscriptionRefusals</c>' own law.</para>
///
/// <para>⚠️ <b>Every sentence says what still works before it says what does not.</b> It is read chairside,
/// mid-consultation, and the first fear it has to answer is « have I lost something? ». The agenda, the records and
/// the <b>SMS</b> reminders are named explicitly (AC-2.6), and nothing here mentions signing in or out.</para>
///
/// <para>⚠️ <b>The exhausted sentence must never promise that the held reminders go out on the 1st.</b> A reminder is
/// measured against the forfait when it comes <i>due</i> — 24 h or 6 h before the visit — so by the time the month
/// turns its appointment has passed and it is refused as obsolete, not sent (AC-4.2, AC-4.5). The renewal date is
/// stated as a fact about the <b>forfait</b>; the remedy offered is the top-up; and the practice is pointed at
/// « Rappels » to see which patients were not prevented. This is asserted over these strings rather than checked by
/// eye — see the Part 2 validation.</para>
/// </summary>
public static class MessagingRefusals
{
    /// <summary>The forfait is spent for this month (AC-4.1).</summary>
    public const string ExhaustedCode = "messaging_allowance_exhausted";

    /// <summary>
    /// ⚠️ <b>Distinct from <see cref="ExhaustedCode"/> on purpose (AC-4.3).</b> A cabinet with no allowance record is
    /// a fault on <i>our</i> side, not a limit it has reached, so it must not be told its forfait ran out — there is
    /// nothing for the practice to have spent, and the remedy is us restoring the row.
    /// </summary>
    public const string MissingCode = "messaging_allowance_missing";

    /// <summary>The date as it is written to a cabinet: a Tunisian calendar day, never an instant.</summary>
    public const string DateFormat = "dd/MM/yyyy";

    /// <summary>
    /// What the practice reads on « Rappels » and on a refused « Relancer », and the sentence the parked row carries.
    /// </summary>
    /// <param name="resetsOn">The first day of the next Tunisian month — <c>ClinicClock.FirstDayOfNextMonth</c>.</param>
    public static string Exhausted(DateTime resetsOn) =>
        "Votre forfait de rappels WhatsApp est épuisé pour ce mois-ci. "
        + "Vos rendez-vous, vos dossiers et vos rappels SMS continuent normalement. "
        + "Les rappels en attente partiront dès que nous augmentons votre forfait ; "
        + $"votre forfait se renouvelle le {resetsOn.ToString(DateFormat, CultureInfo.InvariantCulture)}. "
        + "Consultez « Rappels » pour savoir quels patients n'ont pas été prévenus.";

    /// <summary>
    /// No allowance record at all. Carries <b>no date</b> and does not say « épuisé »: neither would be true, and
    /// sending the practice to wait for a renewal would leave them waiting for something that will not happen.
    /// </summary>
    public const string Missing =
        "Le forfait de rappels WhatsApp de ce cabinet est introuvable. "
        + "Vos rappels WhatsApp sont en attente — contactez-nous, nous le rétablissons.";

    /// <summary>
    /// AC-5.1's « Relancer » refusal. Shorter than <see cref="Exhausted"/> because it answers a gesture the user just
    /// made rather than explaining a screen, and it names the one action that <b>does</b> work — « Marquer comme
    /// contacté » — so the patient does not silently stay on the relance list.
    /// </summary>
    public const string RecallExhausted =
        "Votre forfait de rappels WhatsApp est épuisé pour ce mois-ci. "
        + "Vous pouvez contacter ce patient autrement, puis utiliser « Marquer comme contacté ».";

    /// <summary>
    /// The short French sentences recorded <b>on the parked row</b> and shown beside it in the delivery log
    /// (AC-4.9). Deliberately terse, unlike the screen's paragraphs above: this sits in a table cell next to the
    /// patient's name, and it says the send is <b>waiting</b> rather than failed, because that is what parking means
    /// — nothing was lost and nothing was attempted.
    /// </summary>
    public static string ParkedExhausted(DateTime resetsOn) =>
        $"Forfait de rappels WhatsApp épuisé — envoi en attente, renouvellement le "
        + resetsOn.ToString(DateFormat, CultureInfo.InvariantCulture);

    public const string ParkedMissing =
        "Forfait de rappels WhatsApp introuvable — envoi en attente du rétablissement";

    /// <summary>AC-1.7 — the cabinet's WhatsApp identity is ours to provision, not theirs to type.</summary>
    public const string ManualWhatsAppCode = "messaging_whatsapp_is_vendor_managed";

    /// <summary>
    /// AC-1.7's server-side half. The fields are absent from the screen where vendor messaging is available, and this
    /// is what makes that a rule rather than a UI decision.
    ///
    /// <para>⚠️ It says what to do instead. « Non autorisé » on a field somebody just filled in, with no alternative
    /// named, is how a practice concludes WhatsApp cannot be switched on at all — when in fact it is one button.</para>
    /// </summary>
    public const string ManualWhatsApp =
        "Les identifiants WhatsApp sont fournis par nous sur cette installation et ne se saisissent pas ici. "
        + "Utilisez « Connecter WhatsApp » : votre numéro et votre modèle de message sont configurés pour vous.";

    /// <summary>
    /// § 33a's parked sentence: the cabinet's WhatsApp template is not usable, so the send waits (FR-7, EC-9, EC-10).
    ///
    /// <para>⚠️ It names <b>which</b> of the two situations it is, because they have opposite remedies and only one of
    /// them ends by itself: waiting on Meta's review resolves on its own, while a refused, paused or disabled template
    /// is recovered by <i>us</i> and never by the practice (FR-7). It consumes nothing either way — the whole reason
    /// this is a gate term and not a sender outcome.</para>
    /// </summary>
    public static string ParkedTemplateNotReady(WhatsAppTemplateStatus status) =>
        status is WhatsAppTemplateStatus.NotSubmitted or WhatsAppTemplateStatus.PendingReview
            ? "Modèle WhatsApp en attente de validation par Meta — envoi en attente, rien n'est décompté"
            : "Modèle WhatsApp refusé ou suspendu par Meta — envoi en attente, nous nous en occupons";
}
