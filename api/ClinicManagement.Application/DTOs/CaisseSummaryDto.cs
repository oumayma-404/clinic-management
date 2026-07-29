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
}
