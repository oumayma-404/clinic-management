namespace ClinicManagement.API.Models;

/// <summary>
/// The body of « enregistrer un paiement » (<c>platform-console</c> AC-4.1). The cabinet comes from the route, not
/// from here — one identity per request, and a body that could name a different cabinet from the URL is a
/// disagreement waiting to be resolved the wrong way.
/// </summary>
/// <param name="IdempotencyKey">
/// The console mints one per opened sheet, so the second tap of a double-click carries the first tap's key and
/// produces one entry (AC-4.6). Optional: an unkeyed submission is honoured, it simply has no replay protection.
/// </param>
/// <param name="Complimentary">« Offert » (AC-4.8) — recorded as such, never as a payment of 0,000 DT.</param>
/// <param name="EndsOn">An inclusive last day named outright, for what a duration cannot express.</param>
public record RecordSubscriptionPeriodRequest(
    string? IdempotencyKey,
    bool Complimentary,
    int? DurationMonths,
    int? DurationDays,
    DateTime? EndsOn,
    string? Plan,
    decimal? AmountDt,
    string? Method,
    string? Reference,
    string? Note);
