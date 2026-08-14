namespace ClinicManagement.Application.Features.Dashboard;

/// <summary>
/// The closed set of dashboard KPI keys a user may hide — the server-side authority for "is this a real KPI".
///
/// <para>
/// It exists because <see cref="Domain.Entities.UserDashboardPreference"/> deliberately stores <b>opaque</b>
/// strings: which KPIs the dashboard has is a presentation concern that moves with the UI, and the Domain has no
/// business holding that list. But "opaque at rest" must not mean "unvalidated at the edge" — without this set,
/// any string a client posted would persist forever, so a typo'd or renamed key would accumulate silently and
/// the column would slowly fill with keys that hide nothing.
/// </para>
/// <para>
/// Mirrors <c>web/lib/dashboard-links.ts</c>' <c>DashboardKpiKey</c>. The frontend side is already forced to stay
/// complete by two exhaustive <c>Record&lt;DashboardKpiKey, …&gt;</c> maps (links and labels), so a KPI added
/// there without a destination or a label is a <c>tsc</c> error. This set is the matching gate on the write path:
/// <see cref="DashboardKpiKeysTests"/> pins it against that file so the two cannot drift.
/// </para>
/// <para>
/// ⚠️ Removing a key here does <b>not</b> require a data migration. Old rows may keep naming it, and
/// <see cref="Domain.Entities.UserDashboardPreference.Parse"/> is tolerant on purpose — a preference that names a
/// KPI which no longer exists simply hides nothing. What removing a key does mean is that a client can no longer
/// <i>set</i> it, which is the correct asymmetry: reads must survive history, writes must not create garbage.
/// </para>
/// </summary>
public static class DashboardKpiKeys
{
    /// <summary>Activité — the comparable per-period activity figures.</summary>
    public const string CompletedAppointments = "completedAppointments";
    public const string NewPatients = "newPatients";
    public const string AbsenceRate = "absenceRate";
    public const string AcceptedPlans = "acceptedPlans";

    /// <summary>Argent — the comparable per-period money figures.</summary>
    public const string Collected = "collected";
    public const string Invoiced = "invoiced";
    public const string Refunds = "refunds";
    public const string Expenses = "expenses";
    public const string Net = "net";
    /*
     * Receivables was here. Removed from the WRITE set when « Créances » was withdrawn, exactly as
     * PatientsToRecall was below: the dashboard no longer renders that card, so nothing can hide it, and
     * accepting the key would mean storing a preference about a block that does not exist.
     *
     * `GetReceivablesQuery` and `DashboardReceivablesDto` are untouched — this list governs the customiser,
     * not the figure — and `UserDashboardPreference.Parse` is tolerant, so a row already naming "receivables"
     * simply hides nothing.
     */

    /// <summary>À traiter — current-state operational counts, independent of the selected period.</summary>
    public const string WaitingList = "waitingList";
    public const string DraftPlans = "draftPlans";
    /*
     * PatientsToRecall was here. Removed from the WRITE set when /recalls was deleted: the dashboard no longer
     * renders that card, so nothing can hide it, and accepting the key would mean storing a preference about a
     * block that does not exist. The recall backend is untouched and `DashboardAlertsDto.PatientsToRecall` is
     * still computed — this list governs the customiser, not the figure.
     *
     * Note the asymmetry is safe: `UserDashboardPreference.Parse` is tolerant, so any row already naming
     * "patientsToRecall" simply hides nothing rather than breaking the read.
     */
    public const string OverdueLabOrders = "overdueLabOrders";
    public const string LowStock = "lowStock";
    public const string ExpiringStock = "expiringStock";

    /// <summary>The trend chart is hideable too — it is a section, but from the user's side it is one more block.</summary>
    public const string Trend = "trend";

    /// <summary>« Répartition des actes » — the period's work by act type.</summary>
    public const string ProcedureMix = "procedureMix";

    /// <summary>« Rendez-vous du jour », the appointment list under the figures.</summary>
    public const string TodayAppointments = "todayAppointments";

    /// <summary>Every hideable block, in the order the dashboard renders them.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CompletedAppointments,
        NewPatients,
        AbsenceRate,
        AcceptedPlans,
        Collected,
        Invoiced,
        Refunds,
        Expenses,
        Net,
        WaitingList,
        DraftPlans,
        OverdueLabOrders,
        LowStock,
        ExpiringStock,
        ProcedureMix,
        Trend,
        TodayAppointments,
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="key"/> names a block this dashboard actually has.</summary>
    public static bool IsKnown(string? key) => key is not null && Known.Contains(key);

    /// <summary>
    /// The canonical spelling of <paramref name="key"/>, or <c>null</c> when it is not a real key. Callers
    /// validating a request use this, so a client sending <c>"LowStock"</c> stores the same value as one sending
    /// <c>"lowStock"</c> — otherwise the same intent would produce two different rows.
    /// </summary>
    public static string? Normalize(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        return All.FirstOrDefault(k => k.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
