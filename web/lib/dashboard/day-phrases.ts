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

/**
 * ⚠️ `done` and `evening` are two tiers because they are two facts.
 *
 * <p>There used to be one, `over`, chosen from `summary.isOver` alone — and its whole bank was written at the
 * evening register (🌙, « Rideau », « Bonne soirée »). `isOver` becomes true when the last patient's slot ends, so a
 * practice with one 09:00 visit was wished good evening from 10:00 onwards. « Le programme est terminé » is about
 * the agenda; « il fait nuit » is about the clock; nothing may state the second from the first.</p>
 */
export type DayTier =
  | 'closed'
  | 'empty'
  | 'light'
  | 'steady'
  | 'busy'
  | 'packed'
  | 'done'
  | 'evening';

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
  // The programme is finished and it is still the working day. Nothing here mentions the evening, and nothing
  // congratulates the reader on going home — there may be four hours left, and walk-ins arrive.
  done: [
    { emoji: '✅', headline: 'Programme terminé' },
    { emoji: '📋', headline: 'Plus rien au programme' },
    { emoji: '🙂', headline: 'Agenda dégagé' },
  ],
  evening: [
    { emoji: '🌙', headline: 'Journée terminée' },
    { emoji: '👏', headline: 'C’est dans la boîte' },
    { emoji: '🌆', headline: 'Rideau pour aujourd’hui' },
  ],
};

/** When « bonne soirée » becomes true for a clinic that has saved no closing time. */
const DEFAULT_EVENING_FROM_MINUTES = 18 * 60;

/**
 * Whether the clinic's own day is behind us.
 *
 * <p>Keyed on the cabinet's <b>closing time</b> rather than a fixed hour: a practice closing at 17:00 is in the
 * evening at 17:30 and one closing at 20:00 is not, so any single hardcoded hour is wrong for one of them. The
 * constant is only the fallback for a clinic that has configured none.</p>
 */
function isEvening(summary: DaySummary, nowMinutes: number): boolean {
  return nowMinutes >= (summary.openToMinutes ?? DEFAULT_EVENING_FROM_MINUTES);
}

/**
 * Which register today is in. Order matters: « fermé » and « terminé » outrank any load reading.
 *
 * @param nowMinutes minutes from local midnight — passed in, never read here, for the reason the whole module is
 *                   pure: « what does the greeting say at 11:59 » is otherwise untestable.
 */
export function resolveDayTier(summary: DaySummary, nowMinutes: number): DayTier {
  if (summary.count === 0) return summary.isClosedToday ? 'closed' : 'empty';
  if (summary.isOver) return isEvening(summary, nowMinutes) ? 'evening' : 'done';

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
    /*
     * ⚠️ `doneCount`, never `count`. `count` is patients *booked*, and it was rendered under the label « patient
     * vu » — so a day whose only séance was still open read « 1 patient vu » in the headline while the card below
     * showed that same patient as being treated. Two opposite claims about one séance, one line apart.
     *
     * And « vu » is only said when nothing is left to close: `doneCount` means the slot has passed, which is not
     * the same as somebody confirming the patient came — that is exactly what `AwaitingClosure` withholds.
     */
    case 'done':
    case 'evening': {
      const closing = tier === 'evening' ? ' Bonne soirée.' : '';
      if (summary.unclosedCount > 0) {
        const ended = `${summary.doneCount} ${plural(summary.doneCount, 'séance terminée', 'séances terminées')}`;
        return `${ended} — ${summary.unclosedCount} à clôturer.${closing}`;
      }
      const seen = `${summary.doneCount} ${plural(summary.doneCount, 'patient vu', 'patients vus')}`;
      return `${seen} aujourd’hui.${closing}`;
    }
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
 * @param dayKey     a `YYYY-MM-DD` clinic-local day. Use `todayLocalIso()`, never `toISOString().slice(0, 10)`.
 * @param nowMinutes minutes from local midnight, so the register can tell « le programme est terminé » from
 *                   « il fait nuit ». Pass the same value the day board's cards are measured against.
 *                   ⚠️ **Required, and ahead of `clinicSeed`, on purpose** — as an optional trailing parameter it
 *                   would default to midnight, which reads as « never evening » and silently restores half the
 *                   defect this split exists to fix.
 */
export function buildDayPhrase(
  summary: DaySummary,
  dayKey: string,
  nowMinutes: number,
  clinicSeed = '',
): DayPhrase {
  const tier = resolveDayTier(summary, nowMinutes);
  const options = BANK[tier];
  const chosen = options[hash(`${clinicSeed}|${dayKey}|${tier}`) % options.length];
  return { emoji: chosen.emoji, headline: chosen.headline, subline: buildSubline(tier, summary) };
}
