import type { DashboardKpiKey } from '@/lib/dashboard-links';

/**
 * Everything on the dashboard a user can switch off.
 *
 * <p>A superset of `DashboardKpiKey`, and deliberately a *separate* type rather than an extension of it. The 15 KPI
 * keys are the ones with a filtered destination behind them, and `DASHBOARD_LINKS` is an exhaustive
 * `Record<DashboardKpiKey, …>` precisely so a card cannot exist without a place to click through to. Folding the
 * charts and the appointment list into that union would force all three to invent a destination they do not have,
 * and would weaken the guarantee for the 15 that genuinely need it.</p>
 *
 * <p>Mirrors the server's `DashboardKpiKeys.All`, which is the write-side authority — an unknown key is refused by
 * `UpdateDashboardPreferencesCommand`, so these two lists must agree. `DashboardKpiKeysTests` pins them.</p>
 */
export type DashboardBlockKey =
  | DashboardKpiKey
  | 'procedureMix'
  | 'trend'
  | 'todayAppointments';

/**
 * The groups the customiser lists blocks under, **in the order the page renders them**.
 *
 * <p>⚠️ These used to be `activity · money · alerts · other`, which described the dashboard as it was before the
 * day-first rearrangement: « À traiter » was a section of KPI cards, and the charts were filed under a heading
 * called « Autres blocs ». Afterwards those six counts rendered as chips at the <i>top</i> of the page and the
 * sections below were recut into l'argent and l'activité — so the panel was grouping the screen it used to
 * command. A user who opened it read « À traiter », looked for that section, and found chips 800 px higher under
 * a different name.</p>
 *
 * <p>⚠️ They are groups for the <b>panel</b> only. The keys are unchanged, because the server validates the key
 * set (`DashboardKpiKeys.All`) and knows nothing about grouping — so this is a labelling change with no API
 * consequence at all.</p>
 */
export const DASHBOARD_SECTION_KEYS = ['journee', 'activity', 'money'] as const;
export type DashboardSectionKey = (typeof DASHBOARD_SECTION_KEYS)[number];

/**
 * What shape this block takes on the page.
 *
 * <p>Rendered as each customiser row's sub-line, so a switch says what it commands and where to look for it. Six
 * rows reading « Séances à clôturer » with nothing to say they are the chips above is how the panel drifted from
 * the page in the first place.</p>
 */
export type DashboardBlockForm = 'chip' | 'list' | 'figure' | 'chart';

/** What each form is called, in the panel. */
export const DASHBOARD_FORM_LABELS: Record<DashboardBlockForm, string> = {
  chip: 'Pastille, en haut de page',
  list: 'Liste, sous le ruban',
  figure: 'Chiffre',
  chart: 'Graphe',
};

interface DashboardBlockMeta {
  section: DashboardSectionKey;
  form: DashboardBlockForm;
  /** Short label for the customiser row. The dashboard itself uses `KPI_LABELS`. */
  label: string;
  /**
   * Hidden for a user who has never opened the customiser.
   *
   * <p>This is how the dashboard got shorter without anything being deleted. Each of these is a real figure that
   * some clinic wants — but for the practitioner-owner this dashboard is built for, each is either usually zero
   * (`refunds`), reception's job rather than theirs (`waitingList`), or already implied by a neighbour they can see
   * (`invoiced`, next to « Encaissé »). Switching them on is one click, and nothing about the data path changed,
   * so a clinic that lives by any of them loses nothing permanently.</p>
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
 *
 * <p>⚠️ The four remaining day-board zones — the greeting, « Au fauteuil », the ruban and la prochaine journée —
 * are deliberately <b>absent</b>. They are what makes the day board a day board, and offering to switch them off
 * is offering to switch the screen off.</p>
 */
export const DASHBOARD_BLOCKS: Record<DashboardBlockKey, DashboardBlockMeta> = {
  // ── La journée ────────────────────────────────────────────────────────────────────────────────────────────────
  todayAppointments: { section: 'journee', form: 'list', label: 'Les rendez-vous du jour' },
  // First, and shown by default: it is the only entry here about work the practice has already DONE, so it is the
  // one whose neglect quietly costs the clinic money and leaves the record incomplete.
  visitsToClose: { section: 'journee', form: 'chip', label: 'Séances à clôturer' },
  // Reception's screen, not the practitioner's — and /waiting-list is one nav click away.
  waitingList: { section: 'journee', form: 'chip', label: 'Salle d’attente', hiddenByDefault: true },
  draftPlans: { section: 'journee', form: 'chip', label: 'Devis en attente' },
  overdueLabOrders: { section: 'journee', form: 'chip', label: 'Prothèses en retard' },
  lowStock: { section: 'journee', form: 'chip', label: 'Stock bas' },
  expiringStock: { section: 'journee', form: 'chip', label: 'Périment bientôt' },

  // ── L'activité ────────────────────────────────────────────────────────────────────────────────────────────────
  completedAppointments: { section: 'activity', form: 'figure', label: 'Rendez-vous honorés' },
  absenceRate: { section: 'activity', form: 'figure', label: 'Taux d’absence' },
  newPatients: { section: 'activity', form: 'figure', label: 'Nouveaux patients' },
  acceptedPlans: { section: 'activity', form: 'figure', label: 'Devis acceptés' },
  procedureMix: { section: 'activity', form: 'chart', label: 'Répartition des actes' },

  // ── L'argent ──────────────────────────────────────────────────────────────────────────────────────────────────
  net: { section: 'money', form: 'figure', label: 'Net' },
  collected: { section: 'money', form: 'figure', label: 'Encaissé' },
  // For an owner « Encaissé » (what actually came in) is the load-bearing half of the pair, and it is shown.
  invoiced: { section: 'money', form: 'figure', label: 'Facturé', hiddenByDefault: true },
  expenses: { section: 'money', form: 'figure', label: 'Dépenses' },
  // Usually zero, and it matters enormously in the month it isn't — which is exactly why it is default-hidden
  // rather than deleted. Note the visibility is NOT overridden when the figure is non-zero: quietly re-showing a
  // block the user switched off would make their own setting untrustworthy. « Net » stays explainable regardless,
  // because its description names the formula it comes from.
  refunds: { section: 'money', form: 'figure', label: 'Avoirs remboursés', hiddenByDefault: true },
  trend: { section: 'money', form: 'chart', label: 'Encaissé — 6 derniers mois' },
};

/** Every block key, in the order the customiser and the page render them. */
export const DASHBOARD_BLOCK_KEYS = Object.keys(DASHBOARD_BLOCKS) as DashboardBlockKey[];

/** The keys hidden for a user who has never customised anything. */
export const DEFAULT_HIDDEN_BLOCKS: DashboardBlockKey[] = DASHBOARD_BLOCK_KEYS.filter(
  (key) => DASHBOARD_BLOCKS[key].hiddenByDefault,
);

export const DASHBOARD_SECTION_TITLES: Record<DashboardSectionKey, string> = {
  journee: 'La journée',
  activity: 'L’activité',
  money: 'L’argent',
};

/**
 * The blocks belonging to one customiser group, in render order — optionally narrowed to one {@link
 * DashboardBlockForm}.
 *
 * <p>The `form` filter is what lets the page ask « does any *figure* of l'argent survive this user's choices? »
 * separately from « is the chart on? ». Without it a group whose only visible block is its chart would render an
 * empty hairline grid under a card header.</p>
 */
export function blocksInSection(
  section: DashboardSectionKey,
  form?: DashboardBlockForm,
): DashboardBlockKey[] {
  return DASHBOARD_BLOCK_KEYS.filter(
    (key) =>
      DASHBOARD_BLOCKS[key].section === section &&
      (form === undefined || DASHBOARD_BLOCKS[key].form === form),
  );
}
