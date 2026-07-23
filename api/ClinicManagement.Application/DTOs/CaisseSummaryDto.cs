namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The caisse (daily cash) view for a clinic over a date range: cash collected from invoice payments
/// (encaissements) minus recorded expenses (dépenses), and the resulting net. Defaults to the current day.
/// All figures are TND rounded to the millime.
/// </summary>
public class CaisseSummaryDto
{
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }
    public decimal Net { get; set; }
}
