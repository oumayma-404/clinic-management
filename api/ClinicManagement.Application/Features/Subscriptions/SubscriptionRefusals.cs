using System.Globalization;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// The three refusals a cabinet that may not record new work can meet, each with the machine-readable code the
/// browser routes on (AC-4.4, AC-4.5, EC-6).
///
/// <para><b>One authority, because the sentence and the code are one statement.</b> The gate writes them, the
/// client maps them onto « Abonnement », and the coverage tests assert them; three copies of a French sentence is
/// how a reworded message silently stops matching the code it was paired with — the
/// <c>Contains("déjà facturée")</c> defect one feature over.</para>
///
/// <para>⚠️ <b>Every sentence says what still works before it says what does not.</b> The refusal is met chairside,
/// mid-consultation, and the fear it has to answer first is « have I lost the patient's file? ». Nothing here
/// mentions signing in or out: the refusal never ends the session (AC-4.5).</para>
/// </summary>
public static class SubscriptionRefusals
{
    public const string RequiredCode = "subscription_required";

    public const string SuspendedCode = "subscription_suspended";

    /// <summary>
    /// ⚠️ <b>Distinct from <see cref="RequiredCode"/> on purpose (EC-6).</b> A cabinet with no entitlement row is a
    /// fault on our side, not a lapse on theirs, so it must not be answered with « renouvelez votre abonnement » —
    /// there is nothing for the cabinet to renew, and the remedy is us restoring the row.
    /// </summary>
    public const string MissingCode = "subscription_missing";

    /// <summary>The end date as it is written to a cabinet: a Tunisian calendar day, never an instant.</summary>
    public const string DateFormat = "dd/MM/yyyy";

    /// <summary>
    /// The entitlement ran out on <paramref name="endsOn"/>. The date is named because « expiré » alone invites a
    /// call asking when, and because a cabinet that paid last week needs to see which period is being refused.
    /// </summary>
    public static string Required(DateTime endsOn) =>
        $"Votre abonnement a expiré le {endsOn.ToString(DateFormat, CultureInfo.InvariantCulture)}. "
        + "Vous pouvez toujours consulter et exporter vos données. "
        + "Rendez-vous dans « Abonnement » pour le renouveler.";

    /// <summary>
    /// Suspended, which outranks any date (EC-11). Deliberately carries <b>no</b> date and does not say « expiré »: a
    /// suspension is not fixed by paying, so sending the cabinet to renew would cost them money and change nothing.
    /// </summary>
    public const string Suspended =
        "Votre accès est suspendu. Contactez-nous pour rétablir votre abonnement.";

    public const string Missing =
        "L'abonnement de ce cabinet est introuvable. Contactez-nous, nous le rétablissons.";
}
