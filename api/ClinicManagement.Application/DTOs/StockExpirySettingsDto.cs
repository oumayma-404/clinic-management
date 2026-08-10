namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The clinic's approaching-expiry window, in days. <b>Zero means the alert is switched off</b> — both readers
/// (<c>StockExpiryJob</c>, <c>DashboardAlertsReader</c>) treat a non-positive value that way, so the client must
/// render « alerte désactivée » rather than « 0 jours », which reads as "warn me the day it expires".
/// </summary>
public class StockExpirySettingsDto
{
    public int LeadDays { get; set; }
}
