namespace ClinicManagement.API.Models;

/// <summary>
/// The body of « annuler cette période » (<c>platform-console</c> AC-5.1). The cabinet <b>and</b> the entry come from
/// the route, so the only thing a client supplies is why — a body able to name a different entry from the URL is a
/// disagreement waiting to be resolved the wrong way, and here the wrong way shortens a practice's cover.
/// </summary>
/// <param name="Reason">
/// Mandatory. Nullable on the wire only so an omitted key produces the handler's own French refusal rather than a
/// model-binding error in English, and so « blank » and « absent » are refused identically.
/// </param>
public record CancelSubscriptionPeriodRequest(string? Reason);
