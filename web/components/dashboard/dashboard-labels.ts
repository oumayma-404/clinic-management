import { periodCalendarRange, type DashboardKpiKey } from '@/lib/dashboard-links';
import type {
  AppointmentBucketGranularity,
  DashboardPeriodDto,
  DashboardPeriodKey,
} from '@/lib/api/types';

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
  treatmentsInProgress: 'Traitements en cours',
  waitingList: 'Salle d’attente',
  draftPlans: 'Devis en attente de réponse',
  overdueLabOrders: 'Prothèses en retard',
  lowStock: 'Stock bas',
  expiringStock: 'Péremption proche',
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
  treatmentsInProgress: 'Une étape reste à planifier',
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

/**
 * The five appointment-status classes the chart stacks, **in stack order, bottom to top**.
 *
 * <p>⚠️ The order is not cosmetic. In a stacked column only *adjacent* pairs touch, and this sequence is the one
 * that maximises the weakest of them: it keeps green away from amber (which collapse under protanopia) and green
 * away from red (which collapse under deuteranopia). Reordering these five re-breaks the palette silently — the
 * measured numbers are in `globals.css` beside the tokens.</p>
 *
 * <p>It also reads: the work done at the base, the losses on top, so the « cap » of cancellations and absences on
 * each column can be compared across the window at a glance.</p>
 */
export const APPOINTMENT_STATUS_CLASSES = [
  { key: 'done', label: 'Terminé', token: 'appt-done' },
  { key: 'upcoming', label: 'À venir', token: 'appt-upcoming' },
  { key: 'toClose', label: 'À clôturer', token: 'appt-toclose' },
  { key: 'cancelled', label: 'Annulé', token: 'appt-cancelled' },
  { key: 'absent', label: 'Absent', token: 'appt-absent' },
] as const;

export type AppointmentStatusClassKey = (typeof APPOINTMENT_STATUS_CLASSES)[number]['key'];

/**
 * What each class actually counts, for the table view's header tooltip and the legend's title attribute.
 *
 * <p>Stated because two of the five are folds of two statuses each, and « À clôturer » in particular is *not* the
 * same population as the « Séances à clôturer » chip at the top of the page — that chip counts what a visit still
 * owes (a presence, a fiche, a payment) and a `Terminé` visit can be on it. Here it is the visit's own status.</p>
 */
export const APPOINTMENT_STATUS_CLASS_HINTS: Record<AppointmentStatusClassKey, string> = {
  done: 'Séances terminées',
  upcoming: 'Planifiées ou confirmées, pas encore passées',
  toClose: 'En cours, ou passées sans réponse sur la venue',
  cancelled: 'Annulées avant la séance',
  absent: 'Le patient ne s’est pas présenté',
};

/** How the chart says how wide one column is. */
export const GRANULARITY_LABELS: Record<AppointmentBucketGranularity, string> = {
  Day: 'par jour',
  Week: 'par semaine',
  Month: 'par mois',
};

/**
 * The window a bucket covers, in French — « lun 17 », « 17 – 23 août », « août 2026 ».
 *
 * <p>Built from the two day keys the SERVER sent, never from a recomputed week or month: a bucket clamped to the
 * edge of the window covers fewer days than its calendar unit, and labelling it « semaine du 17 » when it holds
 * four days would be a label that contradicts its own column.</p>
 */
export function bucketLabel(
  start: string,
  endInclusive: string,
  granularity: AppointmentBucketGranularity,
): string {
  if (granularity === 'Day') {
    const d = splitDay(start);
    const date = new Date(d.year, d.month - 1, d.day);
    const weekday = date.toLocaleDateString('fr-TN', { weekday: 'short' }).replace('.', '');
    return `${weekday} ${d.day}`;
  }

  if (granularity === 'Month') {
    const s = splitDay(start);
    // A clamped month bucket is named by its days, not by the month — « 15 – 31 janvier » is honest where
    // « janvier » would claim a whole month the read does not cover.
    const isWholeMonth =
      s.day === 1 && splitDay(endInclusive).day === new Date(s.year, s.month, 0).getDate();
    if (isWholeMonth) return `${monthName(s)} ${String(s.year).slice(2)}`;
  }

  const from = splitDay(start);
  const to = splitDay(endInclusive);
  if (from.month === to.month && from.year === to.year) {
    return `${from.day} – ${to.day} ${monthName(to)}`;
  }
  return `${from.day} ${monthName(from)} – ${to.day} ${monthName(to)}`;
}

/**
 * The sentence naming the card's own window — « du 17 au 23 août », above the columns.
 *
 * <p>The card states this because it is the one block whose window is not the page's, so « Cette semaine » on its
 * own control is a button rather than a claim about dates.</p>
 */
export function windowLabel(from: string, toInclusive: string): string {
  const a = splitDay(from);
  const b = splitDay(toInclusive);
  if (from === toInclusive) return `le ${a.day} ${monthName(a)}`;
  if (a.year === b.year && a.month === b.month) return `du ${a.day} au ${b.day} ${monthName(b)}`;
  if (a.year === b.year) return `du ${a.day} ${monthName(a)} au ${b.day} ${monthName(b)}`;
  return `du ${a.day} ${monthName(a)} ${a.year} au ${b.day} ${monthName(b)} ${b.year}`;
}

/**
 * How the appointment card names what it is comparing against.
 *
 * <p>⚠️ Deliberately « aux N jours précédents » and never « au mois dernier », even when the window happens to be
 * a calendar month: the server compares against the same *number of days* immediately before, which for August is
 * the 31 days ending 31 July — not the month of July. Naming the calendar unit would describe a comparison the
 * figure is not making.</p>
 */
export function comparedToDaysLabel(dayCount: number): string {
  if (dayCount === 1) return 'Comparé à la veille';
  if (dayCount === 7) return 'Comparé aux 7 jours précédents';
  return `Comparé aux ${dayCount.toLocaleString('fr-TN')} jours précédents`;
}

/** Inclusive day count between two day keys. Parsed as parts, so no `Date` and no timezone can shift it. */
export function dayCountBetween(from: string, toInclusive: string): number {
  const a = splitDay(from);
  const b = splitDay(toInclusive);
  const ms = Date.UTC(b.year, b.month - 1, b.day) - Date.UTC(a.year, a.month - 1, a.day);
  return Math.round(ms / 86_400_000) + 1;
}
