namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The caisse (daily cash) view for a clinic over a date range. Defaults to the current <b>clinic-local</b> day
/// (AC-P6.3). All figures are TND rounded to the millime.
///
/// <para>
/// ⚠️ <see cref="CashIn"/> is <b>gross</b> and refunds are their own line (<see cref="Refunds"/>). It used to be
/// net-of-avoirs, with the refund silently subtracted inside it, and that had to change the moment the caisse
/// gained a statement: an « extrait » shows a refund as money leaving, so a total that had already absorbed it
/// meant the lines could not add up to the figure printed above them. The three components are now
/// independent and <see cref="Net"/> is exactly <c>CashIn − Refunds − CashOut</c>.
/// </para>
/// <para>
/// The same split is applied to the dashboard's Argent section in the same change — the two reads are held equal
/// by <c>MoneyReadConsistencyTests</c>, so they move together or not at all.
/// </para>
/// </summary>
public class CaisseSummaryDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    /// <summary>Gross encaissements: invoice payments + devis échéance collections. Excludes voided rows.</summary>
    public decimal CashIn { get; set; }

    /// <summary>Avoirs refunded to patients in the window — money out, reported separately from expenses.</summary>
    public decimal Refunds { get; set; }

    /// <summary>Clinic expenses recorded in the window.</summary>
    public decimal CashOut { get; set; }

    /// <summary><c>CashIn − Refunds − CashOut</c>.</summary>
    public decimal Net { get; set; }

    /// <summary>
    /// <see cref="CashIn"/> split by payment method — la caisse's « dont espèces » (L8 slice B). Without it four
    /// scalars summed across every method meant the owner could not separate the notes physically in the drawer
    /// from a post-dated cheque nobody has banked, which is the one distinction a till is closed against.
    ///
    /// <para>
    /// ⚠️ <b>Σ <see cref="CaisseMethodTotalDto.Amount"/> == <see cref="CashIn"/></b>, and that holds by
    /// construction: the two repository reads behind it are <c>GROUP BY</c> siblings of the very SUMs that produce
    /// <c>CashIn</c>. It is deliberately <b>not</b> derived from the « extrait »'s movement rows — those include
    /// voided payments, so a breakdown summed from them would quietly disagree with the total above it.
    /// </para>
    /// <para>
    /// All four methods are always present, in enum order, zeros included. A stable four-row shape is what lets
    /// « Espèces » stay on screen on a day the clinic happened to take only cheques — which is exactly the day the
    /// drawer figure is worth reading. Money <b>out</b> is not broken down: an expense's method is already on its
    /// own row in the dépenses table, and the question this answers is about money in that has not cleared.
    /// </para>
    /// </summary>
    public List<CaisseMethodTotalDto> CashInByMethod { get; set; } = new();
}

/// <summary>One line of <see cref="CaisseSummaryDto.CashInByMethod"/>.</summary>
public class CaisseMethodTotalDto
{
    /// <summary>The <c>PaymentMethod</c> name (<c>Cash</c>/<c>Cheque</c>/<c>Card</c>/<c>Transfer</c>) — the value the « extrait »'s <c>method</c> filter takes.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>The French label, built server-side through <c>PaymentMethodLabels</c> so the client cannot hold a second copy of it.</summary>
    public string Label { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}
