using ClinicManagement.Application.Common;

namespace ClinicManagement.Application.Features.Invoices;

/// <summary>
/// Validation shared by every money date the clinic supplies — a payment's <c>PaidOn</c>, an installment
/// payment's, an avoir's <c>RefundedOn</c>.
///
/// <para>
/// These are all non-nullable <see cref="DateTime"/>s that reach the domain unvalidated, so a client omitting
/// the key posts <c>0001-01-01</c>. Such a row still moves the collected total but is invisible in every cash
/// window forever, which is a permanent, silent divergence between the stored column and the row sums. A
/// future date is the mirror image: counted in the balance today, absent from the caisse until the date
/// arrives.
/// </para>
/// </summary>
public static class PaymentDateRules
{
    /// <summary>Anything at or before this is an unset/garbage date rather than a real one.</summary>
    private static readonly DateTime Floor = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <returns>A French error, or null when the date is acceptable.</returns>
    public static string? Validate(DateTime value, string fieldLabel, DateTime? nowUtc = null)
    {
        if (value == default || value < Floor)
        {
            return $"{fieldLabel} est absente ou invalide.";
        }

        // Compared by calendar day, not instant: a payment recorded "today" from a client an hour ahead of the
        // server must not be rejected as being in the future.
        //
        // ⚠️ The day is the **clinic's**, not UTC's (J3). `DateTime.UtcNow.Date` runs the clinic's calendar from
        // 01:00 to 01:00 Tunis, so between 00:00 and 01:00 the date the client itself pre-filled with
        // `todayLocalIso()` — the browser's own local day — was refused here as being « dans le futur ». The
        // client and the server disagreed about what day it was, and the only screen that could reveal it was
        // the one a dentist uses at the end of a late session. P6 fixed the numbering and la caisse's default
        // and left the one validator every money date flows through.
        var today = ClinicClock.ClinicToday(nowUtc);
        if (value.Date > today)
        {
            return $"{fieldLabel} ne peut pas être dans le futur.";
        }

        return null;
    }
}
