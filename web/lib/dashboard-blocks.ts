import type { DashboardKpiKey } from '@/lib/dashboard-links';

/**
 * Everything on the dashboard a user can switch off.
 *
 * <p>A superset of `DashboardKpiKey`, and deliberately a *separate* type rather than an extension of it. The 16 KPI
 * keys are the ones with a filtered destination behind them, and `DASHBOARD_LINKS` is an exhaustive
 * `Record<DashboardKpiKey, …>` precisely so a card cannot exist without a place to click through to. Folding the
 * trend chart and the appointment list into that union would force both to invent a destination they do not have,
 * and would weaken the guarantee for the 16 that genuinely need it.</p>
 *
 * <p>Mirrors the server's `DashboardKpiKeys.All`, which is the write-side authority — an unknown key is refused by
 * `UpdateDashboardPreferencesCommand`, so these two lists must agree. `DashboardKpiKeysTests` pins them.</p>
 */
export type DashboardBlockKey = DashboardKpiKey | 'trend' | 'todayAppointments';

/** The sections the customiser groups blocks under, in render order. */
export const DASHBOARD_SECTION_KEYS = ['activity', 'money', 'alerts', 'other'] as const;
export type DashboardSectionKey = (typeof DASHBOARD_SECTION_KEYS)[number];

interface DashboardBlockMeta {
  section: DashboardSectionKey;
  /** Short label for the customiser row. The dashboard itself uses `KPI_LABELS`. */
  label: string;
  /**
   * Hidden for a user who has never opened the customiser.
   *
   * <p>This is how the dashboard got shorter without anything being deleted. Each of these is a real figure that
   * some clinic wants — but for the practitioner-owner this dashboard is built for, each is either usually zero
   * (`refunds`), reception's job rather than theirs (`waitingList`), or already implied by a neighbour they can see
   * (`invoiced`, next to « Encaissé » and « Créances »). Switching them on is one click, and nothing about the
   * data path changed, so a clinic that lives by any of them loses nothing permanently.</p>
   *
   * <p>Default-hidden rather than removed, on purpose: removing a card takes it away from every user in every
   * clinic with no way back, and the whole point of shipping a customiser is that that trade is no longer
   * necessary. A default is an opinion; a deletion is a decision made on someone else's behalf.</p>
   */
  hiddenByDefault?: true;
}

/**
 * The single authority on what the dashboard contains and how the customiser lists it.
 *
 * <p>Exhaustive `Record<DashboardBlockKey, …>` for the same reason `DASHBOARD_LINKS` is: a block added without a
 * customiser entry would be a figure on the page that the panel cannot show you, so it could never be switched
 * off. That is a `tsc` error here rather than something a user discovers.</p>
 */
export const DASHBOARD_BLOCKS: Record<DashboardBlockKey, DashboardBlockMeta> = {
  // Activité
  completedAppointments: { section: 'activity', label: 'Rendez-vous honorés' },
  newPatients: { section: 'activity', label: 'Nouveaux patients' },
  absenceRate: { section: 'activity', label: 'Taux d’absence' },
  acceptedPlans: { section: 'activity', label: 'Devis acceptés' },

  // Argent
  collected: { section: 'money', label: 'Encaissé' },
  net: { section: 'money', label: 'Net' },
  expenses: { section: 'money', label: 'Dépenses' },
  receivables: { section: 'money', label: 'Créances' },
  // Usually zero, and it matters enormously in the month it isn't — which is exactly why it is default-hidden
  // rather than deleted. Note the visibility is NOT overridden when the figure is non-zero: quietly re-showing a
  // block the user switched off would make their own setting untrustworthy. « Net » stays explainable regardless,
  // because its description names the formula it comes from.
  refunds: { section: 'money', label: 'Avoirs remboursés', hiddenByDefault: true },
  // « Facturé » sits between « Encaissé » (what came in) and « Créances » (what is still owed), both of which are
  // shown. For an owner it is the least load-bearing of the three.
  invoiced: { section: 'money', label: 'Facturé', hiddenByDefault: true },

  // À traiter
  draftPlans: { section: 'alerts', label: 'Devis en attente' },
  overdueLabOrders: { section: 'alerts', label: 'Prothèses en retard' },
  lowStock: { section: 'alerts', label: 'Stock bas' },
  expiringStock: { section: 'alerts', label: 'Périment bientôt' },
  // Reception's screen, not the practitioner's — and /waiting-list is one nav click away.
  waitingList: { section: 'alerts', label: 'Salle d’attente', hiddenByDefault: true },

  // Autres blocs
  trend: { section: 'other', label: 'Tendance des encaissements' },
  todayAppointments: { section: 'other', label: 'Rendez-vous du jour' },
};

/** Every block key, in the order the customiser and the page render them. */
export const DASHBOARD_BLOCK_KEYS = Object.keys(DASHBOARD_BLOCKS) as DashboardBlockKey[];

/** The keys hidden for a user who has never customised anything. */
export const DEFAULT_HIDDEN_BLOCKS: DashboardBlockKey[] = DASHBOARD_BLOCK_KEYS.filter(
  (key) => DASHBOARD_BLOCKS[key].hiddenByDefault,
);

export const DASHBOARD_SECTION_TITLES: Record<DashboardSectionKey, string> = {
  activity: 'Activité',
  money: 'Argent',
  alerts: 'À traiter',
  other: 'Autres blocs',
};

/** The blocks belonging to one customiser section, in render order. */
export function blocksInSection(section: DashboardSectionKey): DashboardBlockKey[] {
  return DASHBOARD_BLOCK_KEYS.filter((key) => DASHBOARD_BLOCKS[key].section === section);
}
