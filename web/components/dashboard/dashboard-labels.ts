import type { DashboardKpiKey } from '@/lib/dashboard-links';
import type { DashboardPeriodKey } from '@/lib/api/types';

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
  receivables: 'Créances',
  waitingList: 'Salle d’attente',
  draftPlans: 'Devis en attente de réponse',
  patientsToRecall: 'Patients à rappeler',
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
  receivables: 'Restant dû par les patients',
  waitingList: 'Patients en attente',
  draftPlans: 'Sans réponse du patient',
  patientsToRecall: 'Échéance, devis ou contrôle en attente',
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
 * How the previous period is named in a delta's tooltip / screen-reader text. Written as « vs. hier » rather than
 * « vs. la période précédente » so the comparison is concrete — a reader should never have to guess what a −20 % is
 * measured against.
 */
export const PREVIOUS_PERIOD_LABELS: Record<DashboardPeriodKey, string> = {
  Today: 'hier',
  Week: 'la semaine dernière',
  Month: 'le mois dernier',
};

export const SECTION_LABELS = {
  activity: 'Activité',
  money: 'Argent',
  alerts: 'À traiter',
  trend: 'Tendance',
} as const;

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
