namespace ClinicManagement.Domain.Enums;

/// <summary>
/// How the vendor was paid for a subscription period.
///
/// <para>⚠️ <b>Deliberately not the clinic's <see cref="PaymentMethod"/></b>, which it otherwise resembles. FR-2
/// requires that these amounts are the vendor's revenue and never the clinic's — they must not reach la caisse,
/// l'extrait, « Créances », the dashboard's Argent section or any patient's balance. A shared enum is the first
/// step toward a shared aggregation, and the money reads all key off <c>PaymentMethod</c>.</para>
/// </summary>
public enum SubscriptionPaymentMethod
{
    Transfer = 1,
    Cash = 2,
    Cheque = 3,
    Card = 4
}
