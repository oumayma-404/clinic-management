namespace ClinicManagement.Application.DTOs;

/// <summary>
/// One user's dashboard layout choices.
/// </summary>
/// <param name="HiddenKpis">
/// The blocks this user has hidden. Empty means « show everything » — but see <paramref name="IsCustomised"/> for
/// why that alone is not enough to know what to render.
/// </param>
/// <param name="IsCustomised">
/// Whether this user has ever saved a layout.
///
/// <para>⚠️ <b>Without it, « Tout afficher » could not be saved.</b> The two states « no row yet » and « a row that
/// hides nothing » both serialised as an empty <paramref name="HiddenKpis"/>, and the client — which applies a
/// default hidden set to a fresh account — could only tell them apart by guessing that empty means fresh. So
/// pressing « Tout afficher » wrote <c>HiddenKpisCsv = ''</c> perfectly well and the next load re-applied the
/// defaults over it: the write landed and the setting did not. The distinction is the server's to make; it is the
/// only side that can see whether a row exists.</para>
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
    IReadOnlyList<string> AvailableKpis,
    bool IsCustomised);
