import type { DashboardPeriodDto } from '@/lib/api/types';

/**
 * Every figure the dashboard renders. The union is the mechanism that makes the "every figure is clickable" contract
 * enforceable: {@link DASHBOARD_LINKS} is an exhaustive `Record` over it, so adding a KPI without giving it a
 * destination fails `tsc` rather than shipping a card that goes nowhere.
 *
 * <p>Keys are English (the storage/wire convention); the French labels live in
 * `components/dashboard/dashboard-labels.ts`.</p>
 */
export type DashboardKpiKey =
  | 'completedAppointments'
  | 'newPatients'
  | 'absenceRate'
  | 'acceptedPlans'
  | 'collected'
  | 'invoiced'
  | 'refunds'
  | 'expenses'
  | 'net'
  | 'visitsToClose'
  | 'waitingList'
  | 'draftPlans'
  | 'overdueLabOrders'
  | 'lowStock'
  | 'expiringStock';

/**
 * The API returns UTC instants; the destination pages take calendar days (`<input type="date">` values and the
 * `?from=`/`?to=` params). Converting through the *local* date parts is correct here rather than `toISOString()`:
 * the bounds are already clinic-local midnights expressed as UTC, so slicing the UTC string would hand back the
 * previous day for a UTC+1 clinic.
 */
function toCalendarDay(iso: string): string {
  const d = new Date(iso);
  const month = `${d.getMonth() + 1}`.padStart(2, '0');
  const day = `${d.getDate()}`.padStart(2, '0');
  return `${d.getFullYear()}-${month}-${day}`;
}

function range(period: DashboardPeriodDto): { from: string; to: string } {
  return { from: toCalendarDay(period.from), to: toCalendarDay(period.toInclusive) };
}

function query(params: Record<string, string | undefined>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
  }
  const rendered = search.toString();
  return rendered ? `?${rendered}` : '';
}

/**
 * The single authority mapping a KPI to the filtered view of the records it counted.
 *
 * <p>Kept in one module rather than as `href` props scattered through the page for the same reason
 * `appointment-labels.ts` and `invoice-labels.ts` exist: the mapping is a contract with nine other screens, and a
 * card whose link drifts from what it counted is worse than no link — it quietly asserts a number the destination
 * contradicts.</p>
 */
export const DASHBOARD_LINKS: Record<DashboardKpiKey, (period: DashboardPeriodDto) => string> = {
  // Honoured visits over the window.
  completedAppointments: (p) => `/appointments${query({ ...range(p), status: 'Completed' })}`,

  // Registered in the window. /patients filters on the same inclusive created-date bounds the KPI counted.
  newPatients: (p) => `/patients${query({ createdFrom: range(p).from, createdTo: range(p).to })}`,

  // BOTH statuses, comma-separated: the rate's numerator is NoShow + Cancelled, so landing on no-shows alone would
  // show a fraction of what the card counted.
  absenceRate: (p) => `/appointments${query({ ...range(p), status: 'NoShow,Cancelled' })}`,

  // acceptedFrom/acceptedTo, NOT from/to — the card counts by the date the patient said yes, while /treatment-plans'
  // from/to bound the creation date. Filtering on the wrong one lists a different set of devis.
  acceptedPlans: (p) =>
    `/treatment-plans${query({ status: 'Accepted', acceptedFrom: range(p).from, acceptedTo: range(p).to })}`,

  collected: (p) => `/factures${query(range(p))}`,
  invoiced: (p) => `/factures${query({ ...range(p), status: 'Issued' })}`,

  // An avoir lives on the invoice it credits, and la caisse's « extrait » is the only screen that lists refunds
  // as movements in their own right — which is exactly what a reader clicking this figure wants to see.
  refunds: (p) => `/caisse${query(range(p))}`,

  // La caisse nets expenses against collections over a range, which is where both figures come from.
  expenses: (p) => `/caisse${query(range(p))}`,
  net: (p) => `/caisse${query(range(p))}`,

  // No `receivables`: the « Créances » screen was withdrawn, so the figure it linked to is off the dashboard too.
  // `DashboardReceivablesDto` is still served — this union governs the cards, not the read.
  // The worklist itself, not a filtered agenda: « à clôturer » is not an appointment *status* the calendar can
  // filter on — it is the absence of a fiche or a note d'honoraires, which only this screen computes. No date
  // params either; the window is the server's and defaults to the same 7 days the count was taken over.
  visitsToClose: () => '/a-cloturer',

  waitingList: () => '/waiting-list',
  draftPlans: () => `/treatment-plans${query({ status: 'Draft' })}`,
  overdueLabOrders: () => `/lab-orders${query({ status: 'Sent' })}`,
  lowStock: () => `/stock${query({ filter: 'low' })}`,
  expiringStock: () => `/stock${query({ filter: 'expiring' })}`,
};

export function dashboardLink(key: DashboardKpiKey, period: DashboardPeriodDto): string {
  return DASHBOARD_LINKS[key](period);
}
