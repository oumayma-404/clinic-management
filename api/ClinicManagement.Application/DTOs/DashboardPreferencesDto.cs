namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One user's dashboard layout choices.
/// </summary>
/// <param name="HiddenKpis">
/// The blocks this user has hidden. Empty means "show everything", which is also what a user who has never opened
/// the customiser gets — the read path does not distinguish "no row yet" from "nothing hidden", because to the
/// person looking at the dashboard they are the same state.
/// </param>
/// <param name="AvailableKpis">
/// Every block the dashboard *can* show, so the customiser does not have to keep its own copy of the list.
/// <para>
/// This is the reason the endpoint returns more than the user's choices. The customiser has to render a row per
/// hideable block; if it derived that list client-side it would be a second authority on what the dashboard
/// contains, and the first KPI added without touching it would be invisible in the panel — present on the
/// dashboard, impossible to hide. Sending the set the server validates against means the panel can only ever
/// offer exactly what a write would accept.
/// </para>
/// </param>
public record DashboardPreferencesDto(
    IReadOnlyList<string> HiddenKpis,
    IReadOnlyList<string> AvailableKpis);
