using ClinicManagement.Domain.Enums;

namespace ClinicManagement.Application.Features.Subscriptions;

/// <summary>
/// The French name of every value « Abonnement » renders — the state, the forfait, why a stretch of time was
/// covered, and how the vendor was paid.
///
/// <para><b>Server-side, on <c>AuditLabels</c>' and the caisse statement's precedent.</b> These are four closed
/// enums whose members are decided here; a client-side map would be a second list to extend, and the one that
/// forgets a new member renders a raw `SurMesure` to a dentist. The stable wire value travels beside every label,
/// so a caller still filters and compares on the key rather than on a translated string.</para>
///
/// <para>An unmapped value falls through to its own name rather than to « Inconnu »: a visible `Complimentary` is a
/// gap somebody can report, and « Inconnu » is not.</para>
/// </summary>
public static class SubscriptionLabels
{
    /// <summary>
    /// The fifth state, for a cabinet with no entitlement row at all (FR-13's failure state). It has no
    /// <see cref="SubscriptionState"/> member — the enum describes an entitlement and there is none — but the
    /// vendor report, the console and any screen that ever shows it must still agree on the words, which is what
    /// this class is for. It used to be a literal inlined in the report service and repeated in its tests.
    /// </summary>
    public const string NoSubscription = "Aucun abonnement";

    /// <summary>AC-2.1's four words, verbatim — the screen and the notification quote the same ones.</summary>
    public static string State(SubscriptionState state) => state switch
    {
        SubscriptionState.Trial => "Essai gratuit",
        SubscriptionState.Active => "Actif",
        SubscriptionState.Expired => "Expiré",
        SubscriptionState.Suspended => "Suspendu",
        _ => state.ToString(),
    };

    /// <summary>The three tiers the public Tarifs page sells (FR-10). A label and a price; it gates nothing.</summary>
    public static string Plan(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Cabinet => "Cabinet",
        SubscriptionPlan.Clinique => "Clinique",
        SubscriptionPlan.SurMesure => "Sur-mesure",
        _ => plan.ToString(),
    };

    /// <summary>
    /// Why the cabinet was covered for a stretch of time. « Antériorité » is the grandfathered case — a cabinet that
    /// already existed when the entitlement was introduced (AC-6.1); the entry's own note carries the full sentence.
    /// </summary>
    public static string PeriodKind(SubscriptionPeriodKind kind) => kind switch
    {
        SubscriptionPeriodKind.Trial => "Essai gratuit",
        SubscriptionPeriodKind.Paid => "Paiement",
        SubscriptionPeriodKind.Grandfathered => "Antériorité",
        SubscriptionPeriodKind.Complimentary => "Offert",
        _ => kind.ToString(),
    };

    /// <summary>
    /// How the vendor was paid. ⚠️ <see cref="SubscriptionPaymentMethod"/> is deliberately not the clinic's
    /// <c>PaymentMethod</c> (FR-2), so this is deliberately not <c>PaymentMethodLabels</c> either — a shared map
    /// would be the first step toward a shared aggregation.
    /// </summary>
    public static string PaymentMethod(SubscriptionPaymentMethod method) => method switch
    {
        SubscriptionPaymentMethod.Transfer => "Virement",
        SubscriptionPaymentMethod.Cash => "Espèces",
        SubscriptionPaymentMethod.Cheque => "Chèque",
        SubscriptionPaymentMethod.Card => "Carte bancaire",
        _ => method.ToString(),
    };
}
