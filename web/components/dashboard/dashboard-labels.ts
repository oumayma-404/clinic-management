import { periodCalendarRange, type DashboardKpiKey } from '@/lib/dashboard-links';
import type { DashboardPeriodDto, DashboardPeriodKey } from '@/lib/api/types';

/**
 * French labels for the dashboard's figures — display-time mapping over the English wire keys, the same convention as
 * `appointment-labels.ts` / `invoice-labels.ts` / `treatment-plan-labels.ts`.
 */
export const KPI_LABELS: Record<DashboardKpiKey, string> = {
  completedAppointments: 'Rendez-vous honorés',
  newPatients: 'Nouveaux patients',
  absenceRate: 'Taux d’absence',
  acceptedPlans: 'Devis acceptés',
  collected: 'Encaissé',
  invoiced: 'Facturé',
  refunds: 'Avoirs remboursés',
  expenses: 'Dépenses',
  net: 'Net',
  visitsToClose: 'Séances à clôturer',
  waitingList: 'Salle d’attente',
  draftPlans: 'Devis en attente de réponse',
  overdueLabOrders: 'Prothèses en retard',
  lowStock: 'Stock bas',
  expiringStock: 'Périment bientôt',
};

/** The one-line explanation under each figure — what it counts, so the number is not read as something else. */
export const KPI_DESCRIPTIONS: Record<DashboardKpiKey, string> = {
  completedAppointments: 'Séances réalisées',
  newPatients: 'Dossiers créés',
  absenceRate: 'Absences et annulations',
  acceptedPlans: 'Devis validés par le patient',
  collected: 'Paiements reçus (brut)',
  invoiced: 'Notes d’honoraires émises',
  refunds: 'Remboursés aux patients',
  expenses: 'Sorties de caisse',
  net: 'Encaissé moins avoirs et dépenses',
  visitsToClose: 'Venue, fiche ou paiement à renseigner',
  waitingList: 'Patients en attente',
  draftPlans: 'Sans réponse du patient',
  overdueLabOrders: 'Au laboratoire, délai dépassé',
  lowStock: 'Sous le seuil de réapprovisionnement',
  expiringStock: 'Lots proches de la péremption',
};

export const PERIOD_LABELS: Record<DashboardPeriodKey, string> = {
  Today: 'Aujourd’hui',
  Week: 'Cette semaine',
  Month: 'Ce mois',
};

/**
 * The same three periods, short enough for a 320 px track.
 *
 * <p>A *visual* abbreviation only: `PeriodSelector` keeps {@link PERIOD_LABELS} as each button's accessible name,
 * so nothing announces « Jour ». The three full labels together measure ~355 px against 288 px of content box at
 * the narrowest supported width.</p>
 */
export const PERIOD_LABELS_SHORT: Record<DashboardPeriodKey, string> = {
  Today: 'Jour',
  Week: 'Semaine',
  Month: 'Mois',
};

/**
 * How the previous period is named in a delta's tooltip / screen-reader text. Written as « vs. hier » rather than
 * « vs. la période précédente » so the comparison is concrete — a reader should never have to guess what a −20 % is
 * measured against.
 */
export const PREVIOUS_PERIOD_LABELS: Record<DashboardPeriodKey, string> = {
  Today: 'hier',
  Week: 'la semaine dernière',
  Month: 'le mois dernier',
};

/**
 * The whole « comparé à … » sentence, built here rather than interpolated at the call site.
 *
 * ⚠️ **`à` + `le` contracts to `au`, and interpolation cannot know that.** The labels above were written for
 * « vs. hier », where they read correctly on their own; dropped into `Comparé à ${label}` they produced
 * « Comparé à le mois dernier » on the dashboard's Activité and Argent sections. The fix is not a second,
 * contracted copy of the map — two maps drift — but one function that owns the phrase, so the words and the
 * grammar rule that binds them live together and a fourth period cannot reintroduce the defect.
 */
export function comparedToLabel(period: DashboardPeriodKey): string {
  const contracted: Record<DashboardPeriodKey, string> = {
    Today: 'à hier',
    Week: 'à la semaine dernière',
    Month: 'au mois dernier',
  };
  return `Comparé ${contracted[period]}`;
}

/**
 * The headings the page draws.
 *
 * <p>⚠️ `trend` is gone: the chart carries its own `CardTitle` (« Encaissé — 6 derniers mois ») and this entry had
 * no consumer left. `activity` and `money` are now possessive — « L'argent », « L'activité » — because they name a
 * *card about a question* rather than a category of figure.</p>
 */
export const SECTION_LABELS = {
  day: 'La journée',
  appointments: 'Les rendez-vous',
  alerts: 'À traiter',
  period: 'Sur cette période',
  activity: 'L’activité',
  money: 'L’argent',
} as const;

/**
 * The window a period actually covers, in French — « 17 août », « 11 – 17 août », « 28 juillet – 3 août ».
 *
 * <p>Built from {@link periodCalendarRange}, i.e. the **server's own bounds** through the same conversion
 * `DASHBOARD_LINKS` uses for its query params — so the sentence above the figures and the filter a card links to
 * can never name different days. Before this the dashboard stated its window nowhere at all: « Ce mois » is a
 * button, not a claim about dates.</p>
 */
export function periodWindowLabel(period: DashboardPeriodDto): string {
  const { from, to } = periodCalendarRange(period);
  const start = splitDay(from);
  const end = splitDay(to);

  if (from === to) return `${start.day} ${monthName(start)}`;
  // Same month: name it once. « 1 – 17 août » rather than « 1 août – 17 août ».
  if (start.year === end.year && start.month === end.month) {
    return `${start.day} – ${end.day} ${monthName(end)}`;
  }
  return `${start.day} ${monthName(start)} – ${end.day} ${monthName(end)}`;
}

/** A `YYYY-MM-DD` day string in parts. No `Date` is built, so no timezone can shift it. */
function splitDay(day: string): { year: number; month: number; day: number } {
  const [year, month, date] = day.split('-');
  return { year: Number(year), month: Number(month), day: Number(date) };
}

/** « août ». Constructed locally from the parts, the same shape `formatMonthShort` already uses. */
function monthName(parts: { year: number; month: number }): string {
  return new Date(parts.year, parts.month - 1, 1).toLocaleDateString('fr-TN', { month: 'long' });
}

/** Formats a French month label from the API's locale-free `yyyy-MM`. */
export function formatMonthShort(month: string): string {
  const [year, monthPart] = month.split('-');
  const date = new Date(Number(year), Number(monthPart) - 1, 1);
  return date.toLocaleDateString('fr-TN', { month: 'short' });
}

/** Formats a full French month label (chart tooltip, table view). */
export function formatMonthLong(month: string): string {
  const [year, monthPart] = month.split('-');
  const date = new Date(Number(year), Number(monthPart) - 1, 1);
  return date.toLocaleDateString('fr-TN', { month: 'long', year: 'numeric' });
}
