using ClinicManagement.Application.Features.Dashboard;

namespace ClinicManagement.Application.DTOs;

/// <summary>
/// The whole dashboard in one response. Grouped into sections rather than flattened into a bag of scalars: the
/// grouping is what tells a consumer which figures are comparable (<see cref="DashboardActivityDto"/>,
/// <see cref="DashboardMoneyDto"/>) and which are a point-in-time reading with no previous value
/// (<see cref="Receivables"/>, <see cref="Alerts"/>).
///
/// <para>Property names are English on the wire; the French labels live in the frontend's
/// <c>dashboard-labels.ts</c>, following the repo's standing display-time-mapping convention.</para>
/// </summary>
public class DashboardDto
{
    public DashboardPeriodDto Period { get; set; } = new();
    public DashboardActivityDto Activity { get; set; } = new();
    public DashboardMoneyDto Money { get; set; } = new();
    public DashboardReceivablesDto Receivables { get; set; } = new();
    public DashboardAlertsDto Alerts { get; set; } = new();

    /// <summary>Six months of collected cash, oldest first, gaps filled with zero.</summary>
    public List<MonthlyCollectedPointDto> Trend { get; set; } = new();

    /// <summary>
    /// What the period's work was made of, by act type — busiest first, capped.
    ///
    /// <para>The one figure on this response counted over <b>acts</b> rather than appointments or money, which is
    /// why it is its own section rather than a field on <see cref="Activity"/>: « 62 détartrages » and
    /// « 184 RDV honorés » are different denominators and putting them in one grid invites the reader to subtract
    /// them.</para>
    /// </summary>
    public List<ProcedureMixPointDto> ProcedureMix { get; set; } = new();
}

/// <summary>
/// The resolved window, echoed back so the client can build its drill-through links from the <b>same</b> bounds the
/// figures were computed over instead of recomputing them and risking a different answer.
/// </summary>
public class DashboardPeriodDto
{
    public string Key { get; set; } = nameof(DashboardPeriodKey.Month);
    public DateTime From { get; set; }
    public DateTime ToInclusive { get; set; }
    public DateTime PreviousFrom { get; set; }
    public DateTime PreviousToInclusive { get; set; }
}

/// <summary>Clinical throughput over the period, each figure against the preceding equivalent period.</summary>
public class DashboardActivityDto
{
    /// <summary>Appointments that actually happened (<c>Completed</c>).</summary>
    public PeriodComparison CompletedAppointments { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary>Patients registered in the period (archived excluded, matching the patients list).</summary>
    public PeriodComparison NewPatients { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary>
    /// Percentage of the period's appointments that did not happen — <c>(NoShow + Cancelled) ÷ total</c>. Null when
    /// the period held no appointments at all: « — », not « 0 % ».
    /// </summary>
    public PeriodComparison AbsenceRate { get; set; } = PeriodComparison.Rate(null, null);

    /// <summary>Devis the patient said yes to in the period, dated by <c>AcceptedDate</c>.</summary>
    public PeriodComparison AcceptedPlans { get; set; } = PeriodComparison.Of(0m, 0m);
}

/// <summary>
/// Cash over the period, each figure against the preceding equivalent period. Every figure here is produced from the
/// same repository calls la caisse uses, through the same <c>PlanBillingRules</c> de-duplication — the two screens
/// reporting different money for the same window is the specific failure this section is written to avoid.
/// </summary>
public class DashboardMoneyDto
{
    /// <summary>
    /// <b>Gross</b> encaissements: invoice payments + treatment-plan installment collections. Refunds are
    /// <see cref="Refunds"/>, not a subtraction hidden in here — see <c>CaisseSummaryDto</c> for why the split
    /// arrived with the caisse statement.
    /// </summary>
    public PeriodComparison Collected { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary>Issued (numbered, non-cancelled) invoice totals TTC.</summary>
    public PeriodComparison Invoiced { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary>
    /// L9 — true when a practitioner filter is active, in which case <b>Dépenses, Net and Créances remain
    /// clinic-wide</b>. An expense has no practitioner (rent and salaries belong to the practice), so a narrowed
    /// « Net » would be one dentist's income minus everybody's costs. The client must label the two money-out lines
    /// rather than presenting them as that practitioner's.
    /// </summary>
    public bool ClinicWideOutgoings { get; set; }

    /// <summary>
    /// L9 — true when <see cref="Collected"/> counts <b>invoice payments only</b>, because a practitioner filter is
    /// active and échéance collections are not attributable in this slice. Stated rather than silently mixed: see
    /// <c>DashboardMoneyReader.CollectedAsync</c>.
    /// </summary>
    public bool CollectedInvoicesOnly { get; set; }

    /// <summary>Avoirs refunded to patients in the window — money out, kept distinct from expenses.</summary>
    public PeriodComparison Refunds { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary>Recorded clinic expenses (caisse cash-out).</summary>
    public PeriodComparison Expenses { get; set; } = PeriodComparison.Of(0m, 0m);

    /// <summary><c>Collected − Refunds − Expenses</c>. Can legitimately be negative.</summary>
    public PeriodComparison Net { get; set; } = PeriodComparison.Of(0m, 0m);
}

/// <summary>
/// What patients currently owe the clinic across both money tracks. Deliberately <b>not</b> a
/// <see cref="PeriodComparison"/>: this is a live balance as of now, and « les créances du mois dernier » is not a
/// figure that exists.
/// </summary>
public class DashboardReceivablesDto
{
    public decimal Total { get; set; }
}

/// <summary>
/// Standing state that needs attention — the app's other subsystems, none of which previously reached the first
/// screen. All point-in-time counts, so none carries a comparison.
///
/// <para>The stock entries overlap the in-app notification feed by design: the feed is a transient, per-user-read
/// stream of events, whereas these are a persistent answer to « what is wrong right now ». An item can legitimately
/// appear in both.</para>
/// </summary>
public class DashboardAlertsDto
{
    /// <summary>Patients still <c>Waiting</c> in the salle d'attente.</summary>
    public int WaitingList { get; set; }

    /// <summary>Devis awaiting the patient's answer (<c>Draft</c>).</summary>
    public int DraftPlans { get; set; }

    /// <summary>Patients due a relance.</summary>
    public int PatientsToRecall { get; set; }

    /// <summary>Prostheses still at the lab past their expected return date.</summary>
    public int OverdueLabOrders { get; set; }

    /// <summary>Items at or below their reorder threshold.</summary>
    public int LowStock { get; set; }

    /// <summary>Items holding a lot at or inside the clinic's expiry lead window.</summary>
    public int ExpiringStock { get; set; }

    /// <summary>
    /// False when the clinic has the approaching-expiry alert switched off (<c>StockExpiryLeadDays &lt;= 0</c>), the
    /// same reading <c>StockExpiryJob</c> applies. The UI hides the figure entirely rather than showing a zero, which
    /// would claim nothing is expiring when in truth nothing was checked.
    /// </summary>
    public bool ExpiryAlertEnabled { get; set; }

    /// <summary>
    /// Séances whose slot has passed and which still owe one of the three answers — est-il venu, qu'a-t-on fait,
    /// combien a-t-il payé.
    ///
    /// <para>Counted over the same window and through the same <c>VisitClosureRules</c> the « À clôturer » list
    /// itself uses, so the chip and the page it opens cannot report different numbers — the rule this whole
    /// section already follows.</para>
    ///
    /// <para>⚠️ It is deliberately <b>not</b> the only surface: <c>GET /api/dashboard</c> is <c>AdminOrDoctor</c>
    /// and a secretary is redirected off the dashboard entirely, so reception — who knows whether the patient came
    /// and who takes the money — would never see it. The worklist and the agenda strip are the primary surfaces
    /// and are <c>AnyClinicRole</c>; this chip is the owner's morning view of the same figure.</para>
    /// </summary>
    public int VisitsToClose { get; set; }
}

/// <summary>One point on the « Tendance » sparkline.</summary>
/// <summary>
/// One act type's share of the period.
///
/// <para><see cref="ProcedureTypeId"/> is null for a hand-typed devis line, which has no catalogue act behind it
/// — real work, so it is listed under its own name rather than dropped. <see cref="ColorHex"/> may be null for
/// the same reason, and a client must render a neutral swatch rather than invent one.</para>
/// </summary>
public class ProcedureMixPointDto
{
    public Guid? ProcedureTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    /// <summary>Acts, not appointments — a séance carries several, so this sums above the visit count.</summary>
    public int ActCount { get; set; }
    /// <summary>
    /// Total booked minutes for this act type, or <b>0</b> when none of its rows carried a duration.
    ///
    /// <para>Zero is a real answer here and is not the same as « no acts »: a link-only devis line contributes
    /// none, so a chart switched to « durée » must be able to show a bar of zero beside a real count.</para>
    /// </summary>
    public int Minutes { get; set; }
}

public class MonthlyCollectedPointDto
{
    /// <summary>Clinic-local calendar month as <c>yyyy-MM</c> — sortable, locale-free, formatted by the client.</summary>
    public string Month { get; set; } = string.Empty;

    public decimal Collected { get; set; }
}
