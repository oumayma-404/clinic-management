import type { DaySummary } from '@/lib/dashboard/day-summary';

/**
 * The line at the top of the dashboard, and the rules that keep it from being a fortune cookie.
 *
 * <p><b>The phrase must be a true statement about today, derived from figures the reader can verify one line
 * down.</b> « Journée tranquille » beside eleven appointments is a defect the user can see, and the fastest way
 * to make a medical product look unserious to the one audience that matters — a patient glancing at the
 * receptionist's screen. So the headline comes from a bank while the sub-line is <i>generated</i> from the real
 * count; the two can never disagree, because only one of them is written by hand.</p>
 *
 * <p>Every tier is positive, « vide » and « marathon » included. A full day is framed as capability, never as a
 * warning; an empty one as opportunity, never as « aucun patient », which reads as a business failing rather
 * than a free morning.</p>
 */

export type DayTier = 'closed' | 'empty' | 'light' | 'steady' | 'busy' | 'packed' | 'over';

export interface DayPhrase {
  emoji: string;
  headline: string;
  /** Built from the day's own figures, never from the bank. */
  subline: string;
}

/**
 * Where each tier starts, as a share of the clinic's open minutes.
 *
 * <p>Load, not a raw count: six appointments is a light day for a three-dentist practice and a heavy one for a
 * solo dentist working afternoons. With no configured hours there is no denominator, and
 * {@link resolveDayTier} falls back to the count thresholds below rather than inventing one.</p>
 */
const LOAD_TIERS: ReadonlyArray<{ upTo: number; tier: DayTier }> = [
  { upTo: 35, tier: 'light' },
  { upTo: 70, tier: 'steady' },
  { upTo: 90, tier: 'busy' },
  { upTo: Number.POSITIVE_INFINITY, tier: 'packed' },
];

/** The fallback ladder when the clinic has saved no working hours. */
const COUNT_TIERS: ReadonlyArray<{ upTo: number; tier: DayTier }> = [
  { upTo: 4, tier: 'light' },
  { upTo: 9, tier: 'steady' },
  { upTo: 14, tier: 'busy' },
  { upTo: Number.POSITIVE_INFINITY, tier: 'packed' },
];

const BANK: Record<DayTier, ReadonlyArray<{ emoji: string; headline: string }>> = {
  closed: [
    { emoji: '🌿', headline: 'Cabinet fermé aujourd’hui' },
    { emoji: '🔒', headline: 'Journée de fermeture' },
  ],
  empty: [
    { emoji: '☕', headline: 'Aucun rendez-vous aujourd’hui' },
    { emoji: '🌿', headline: 'Agenda au repos' },
    { emoji: '📋', headline: 'Journée libre' },
    { emoji: '🧹', headline: 'Une journée pour souffler' },
  ],
  light: [
    { emoji: '☕', headline: 'Journée tranquille' },
    { emoji: '🍃', headline: 'Au calme' },
    { emoji: '🌤️', headline: 'Ça va rouler tout seul' },
    { emoji: '🙂', headline: 'Petite journée' },
  ],
  steady: [
    { emoji: '🦷', headline: 'Belle journée en perspective' },
    { emoji: '🎯', headline: 'Programme équilibré' },
    { emoji: '✨', headline: 'C’est parti' },
    { emoji: '👍', headline: 'Une journée bien remplie' },
  ],
  busy: [
    { emoji: '🐝', headline: 'Quelle belle ruche !' },
    { emoji: '⚡', headline: 'Ça va bourdonner' },
    { emoji: '💪', headline: 'À plein régime' },
    { emoji: '🚀', headline: 'Journée bien chargée' },
  ],
  packed: [
    { emoji: '🐝', headline: 'Marathon en vue !' },
    { emoji: '🏆', headline: 'Journée record' },
    { emoji: '💫', headline: 'Quelle énergie !' },
    { emoji: '🔥', headline: 'Grande journée' },
  ],
  over: [
    { emoji: '🌙', headline: 'Journée terminée' },
    { emoji: '👏', headline: 'C’est dans la boîte' },
    { emoji: '🌆', headline: 'Rideau pour aujourd’hui' },
  ],
};

/** Which register today is in. Order matters: « fermé » and « terminé » outrank any load reading. */
export function resolveDayTier(summary: DaySummary): DayTier {
  if (summary.count === 0) return summary.isClosedToday ? 'closed' : 'empty';
  if (summary.isOver) return 'over';

  const ladder = summary.loadPercent !== null ? LOAD_TIERS : COUNT_TIERS;
  const measure = summary.loadPercent !== null ? summary.loadPercent : summary.count;
  return ladder.find((step) => measure <= step.upTo)?.tier ?? 'steady';
}

const plural = (n: number, one: string, many: string) => (n === 1 ? one : many);

/** The generated half — the fact, in French, from the day's own figures. */
function buildSubline(tier: DayTier, summary: DaySummary): string {
  const rdv = `${summary.count} ${plural(summary.count, 'rendez-vous', 'rendez-vous')}`;

  switch (tier) {
    case 'closed':
      return 'Aucune ouverture prévue. Les horaires se modifient dans « Paramètres ».';
    case 'empty':
      // « Le planning est vide » is false on a day held by blocked hours, and the ribbon below it shows them.
      return summary.blockedCount > 0
        ? `Aucun patient prévu — ${summary.blockedCount} ${plural(summary.blockedCount, 'créneau bloqué', 'créneaux bloqués')} seulement.`
        : 'Le planning est vide — de quoi rattraper les dossiers en attente.';
    case 'over':
      return `${summary.count} ${plural(summary.count, 'patient vu', 'patients vus')} aujourd’hui. Bonne soirée.`;
    default:
      break;
  }

  // Mid-day, the useful figure is what is LEFT, not the total — « 11 rendez-vous vous attendent » is false at
  // 16:00 with nine of them behind you.
  if (summary.doneCount > 0 && summary.remainingCount > 0) {
    return `${rdv} — ${summary.doneCount} ${plural(summary.doneCount, 'déjà passé', 'déjà passés')}, ${summary.remainingCount} à venir.`;
  }

  switch (tier) {
    case 'light':
      return `${rdv} au programme — vous avez de la marge.`;
    case 'steady':
      return `${rdv} au programme aujourd’hui.`;
    case 'busy':
      return `${rdv} aujourd’hui — bon courage.`;
    case 'packed':
      return `${rdv} — pensez à souffler entre deux.`;
    default:
      return `${rdv} aujourd’hui.`;
  }
}

/**
 * A small stable hash. Not a security primitive — it exists so the same day always picks the same line.
 */
function hash(seed: string): number {
  let h = 0;
  for (let i = 0; i < seed.length; i += 1) h = (h * 31 + seed.charCodeAt(i)) | 0;
  return Math.abs(h);
}

/**
 * Today's phrase.
 *
 * <p>⚠️ <b>Not `Math.random()`.</b> This page live-refreshes on nine realtime keys, so a phrase re-rolled on
 * every render would visibly flicker whenever a colleague saves anything. The pick is a hash of
 * (clinic, day, tier): stable for the whole day, identical for two staff at the same practice, and it changes
 * exactly when the day genuinely changes register — a morning that fills up moves from « tranquille » to
 * « ruche », which is correct rather than a glitch.</p>
 *
 * @param dayKey a `YYYY-MM-DD` clinic-local day. Use `todayLocalIso()`, never `toISOString().slice(0, 10)`.
 */
export function buildDayPhrase(summary: DaySummary, dayKey: string, clinicSeed = ''): DayPhrase {
  const tier = resolveDayTier(summary);
  const options = BANK[tier];
  const chosen = options[hash(`${clinicSeed}|${dayKey}|${tier}`) % options.length];
  return { emoji: chosen.emoji, headline: chosen.headline, subline: buildSubline(tier, summary) };
}
