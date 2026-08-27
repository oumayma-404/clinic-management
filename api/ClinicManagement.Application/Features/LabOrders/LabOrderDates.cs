namespace ClinicManagement.Application.Features.LabOrders;

/// <summary>
/// The one rule about a bon de prothèse's two dates: a piece cannot be <b>expected back</b> before it was
/// <b>sent</b>.
///
/// <para>Stated once because there are two doors onto it — create and update — and it was absent from both, from
/// the form, and from the domain. It is not only a data-quality point: <c>CountOverdueAsync</c> asks « is
/// <c>ExpectedDate</c> in the past and the piece not yet in », so a bon expected the week before it was sent is
/// permanently overdue and permanently in the count the lab screen leads with.</para>
///
/// <para>Both dates are optional and either may stand alone — plenty of bons are raised before a return date is
/// agreed — so the rule fires only when both are present.</para>
/// </summary>
public static class LabOrderDates
{
    public const string ExpectedBeforeSent =
        "La date prévue ne peut pas être antérieure à la date d'envoi.";

    /// <summary>The French refusal, or null when the pair is acceptable.</summary>
    public static string? Refuse(DateTime? sentDate, DateTime? expectedDate) =>
        sentDate.HasValue && expectedDate.HasValue && expectedDate.Value.Date < sentDate.Value.Date
            ? ExpectedBeforeSent
            : null;
}
