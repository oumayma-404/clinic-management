import { formatClock, formatDuration, type DaySummary } from '@/lib/dashboard/day-summary';

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
 *
 * <p><b>The French is written, not translated.</b> Every line here is a sentence a Tunisian practice would say out
 * loud — present tense while the day runs, future while it waits, and never the calqued register (« Programme
 * équilibré », « Journée de fermeture ») that gives a product away as a localisation.</p>
 */

/**
 * ⚠️ A tier is (<b>moment</b> × <b>load</b>), not load alone, and the moment half is why there are thirteen.
 *
 * <p>Load on its own gave one headline for the whole day: « Belle journée en perspective » was still on screen at
 * 16:00 with nine of the eleven visits behind the reader, and « C'est parti » greeted an empty waiting room at
 * 07:00. The register a day is in is genuinely two facts — how heavy it is, and where in it we are — so
 * `ahead-*` speaks in the future, `underway-*` in the present, and `last-one` names the one state everybody
 * counts down to.</p>
 *
 * <p>⚠️ `done` and `evening` are likewise two tiers because they are two facts. There used to be one, `over`,
 * chosen from `summary.isOver` alone — and its whole bank was written at the evening register (🌙, « Rideau »,
 * « Bonne soirée »). `isOver` becomes true when the last patient's slot ends, so a practice with one 09:00 visit
 * was wished good evening from 10:00 onwards. « Le programme est terminé » is about the agenda; « il fait nuit »
 * is about the clock; nothing may state the second from the first.</p>
 */
export type DayTier =
  | 'closed'
  | 'empty'
  | 'ahead-light'
  | 'ahead-steady'
  | 'ahead-busy'
  | 'ahead-packed'
  | 'underway-light'
  | 'underway-steady'
  | 'underway-busy'
  | 'underway-packed'
  | 'last-one'
  | 'done'
  | 'evening';

/** How heavy today is, once « fermé », « terminé » and « plus qu'un » have had their say. */
type DayLoad = 'light' | 'steady' | 'busy' | 'packed';

export interface DayPhrase {
  emoji: string;
  headline: string;
  /** Built from the day's own figures, never from the bank. */
  subline: string;
}

/**
 * Where each load band starts, as a share of the clinic's open minutes.
 *
 * <p>Load, not a raw count: six appointments is a light day for a three-dentist practice and a heavy one for a
 * solo dentist working afternoons. With no configured hours there is no denominator, and {@link resolveLoad}
 * falls back to the count thresholds below rather than inventing one.</p>
 */
const LOAD_TIERS: ReadonlyArray<{ upTo: number; load: DayLoad }> = [
  { upTo: 35, load: 'light' },
  { upTo: 70, load: 'steady' },
  { upTo: 90, load: 'busy' },
  { upTo: Number.POSITIVE_INFINITY, load: 'packed' },
];

/** The fallback ladder when the clinic has saved no working hours. */
const COUNT_TIERS: ReadonlyArray<{ upTo: number; load: DayLoad }> = [
  { upTo: 4, load: 'light' },
  { upTo: 9, load: 'steady' },
  { upTo: 14, load: 'busy' },
  { upTo: Number.POSITIVE_INFINITY, load: 'packed' },
];

/**
 * ⚠️ Headlines stay under ~30 characters.
 *
 * <p>`text-title` is 26 px, and at 320 px the greeting column is about 230 px — some seventeen characters a line.
 * A longer line is not clipped, it wraps, and a three-line headline pushes the now/next pair — the most
 * actionable thing on the screen — off the first screenful.</p>
 */
const BANK: Record<DayTier, ReadonlyArray<{ emoji: string; headline: string }>> = {
  // The clinic does not open on this weekday at all. Nothing here scolds and nothing implies a lost day.
  closed: [
    { emoji: '🌿', headline: 'Cabinet fermé aujourd’hui' },
    { emoji: '🛌', headline: 'Jour de repos' },
  ],
  // Open, and nobody booked. Framed as a free day, never as « aucun patient ».
  empty: [
    { emoji: '☕', headline: 'Journée sans rendez-vous' },
    { emoji: '🌿', headline: 'Agenda au repos' },
    { emoji: '🌤️', headline: 'Journée libre' },
    { emoji: '😌', headline: 'Une journée pour souffler' },
  ],
  // Nothing has started yet: the future tense is the whole point of this half of the bank.
  'ahead-light': [
    { emoji: '☕', headline: 'Journée tranquille' },
    { emoji: '🍃', headline: 'Tout en douceur' },
    { emoji: '🙂', headline: 'Petite journée' },
    { emoji: '🌤️', headline: 'Ça va rouler tout seul' },
  ],
  'ahead-steady': [
    { emoji: '🦷', headline: 'Belle journée en perspective' },
    { emoji: '✨', headline: 'Tout est prêt' },
    { emoji: '👍', headline: 'Ça se présente bien' },
    { emoji: '📆', headline: 'Journée bien remplie' },
  ],
  'ahead-busy': [
    { emoji: '🐝', headline: 'Belle ruche aujourd’hui' },
    { emoji: '💪', headline: 'Grosse journée devant vous' },
    { emoji: '⚡', headline: 'Ça va bourdonner' },
    { emoji: '🚀', headline: 'Journée bien chargée' },
  ],
  'ahead-packed': [
    { emoji: '🏃', headline: 'Marathon en vue' },
    { emoji: '🏆', headline: 'Journée record' },
    { emoji: '🔥', headline: 'Grande journée' },
    { emoji: '💪', headline: 'Ça va être costaud' },
  ],
  // The day is running. Present tense, and never a wish for a start that already happened.
  'underway-light': [
    { emoji: '☕', headline: 'On avance au calme' },
    { emoji: '🍃', headline: 'Journée tranquille' },
    { emoji: '🙂', headline: 'Ça se déroule bien' },
  ],
  'underway-steady': [
    { emoji: '🦷', headline: 'Bon rythme' },
    { emoji: '🎯', headline: 'Ça avance bien' },
    { emoji: '👍', headline: 'La journée suit son cours' },
  ],
  'underway-busy': [
    { emoji: '🐝', headline: 'Ça bourdonne !' },
    { emoji: '⚡', headline: 'À plein régime' },
    { emoji: '💪', headline: 'On tient le rythme' },
  ],
  'underway-packed': [
    { emoji: '🔥', headline: 'Ça carbure !' },
    { emoji: '💫', headline: 'Quelle énergie !' },
    { emoji: '🏃', headline: 'En pleine course' },
  ],
  // One patient left, and some already behind — the state a practice counts down to out loud.
  'last-one': [
    { emoji: '🏁', headline: 'Dernier patient du jour' },
    { emoji: '🌅', headline: 'Plus qu’un patient' },
    { emoji: '✨', headline: 'On voit le bout' },
  ],
  // The programme is finished and it is still the working day. Nothing here mentions the evening, and nothing
  // congratulates the reader on going home — there may be four hours left, and walk-ins arrive.
  done: [
    { emoji: '✅', headline: 'Programme terminé' },
    { emoji: '🙂', headline: 'Agenda dégagé' },
    { emoji: '☕', headline: 'La suite est à vous' },
  ],
  evening: [
    { emoji: '🌙', headline: 'Journée terminée' },
    { emoji: '👏', headline: 'C’est dans la boîte' },
    { emoji: '🌆', headline: 'Rideau pour aujourd’hui' },
    { emoji: '⭐', headline: 'Belle journée de travail' },
  ],
};

/** When « bonne soirée » becomes true for a clinic that has saved no closing time. */
const DEFAULT_EVENING_FROM_MINUTES = 18 * 60;

/** Below this, « encore N h au fauteuil » is a rounding error rather than a useful figure. */
const MIN_REMAINING_MINUTES_WORTH_SAYING = 60;

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

/** How heavy today is — by chair load where the clinic has hours, by head count where it has none. */
function resolveLoad(summary: DaySummary): DayLoad {
  const ladder = summary.loadPercent !== null ? LOAD_TIERS : COUNT_TIERS;
  const measure = summary.loadPercent !== null ? summary.loadPercent : summary.count;
  return ladder.find((step) => measure <= step.upTo)?.load ?? 'steady';
}

/**
 * Which register today is in. Order matters: « fermé », « terminé » and « plus qu'un » outrank any load reading.
 *
 * @param nowMinutes minutes from local midnight — passed in, never read here, for the reason the whole module is
 *                   pure: « what does the greeting say at 11:59 » is otherwise untestable.
 */
export function resolveDayTier(summary: DaySummary, nowMinutes: number): DayTier {
  if (summary.count === 0) return summary.isClosedToday ? 'closed' : 'empty';
  if (summary.isOver) return isEvening(summary, nowMinutes) ? 'evening' : 'done';
  // ⚠️ `doneCount > 0` is required. Without it a practice with a single visit read « Dernier patient du jour »
  // from midnight, which is true only in the arithmetic sense and reads as a day already spent.
  if (summary.remainingCount === 1 && summary.doneCount > 0) return 'last-one';
  // The chair, not only the clock: the first patient of the day being treated means the day has begun, even
  // though nothing has passed yet.
  const moment = summary.doneCount > 0 || summary.current !== null ? 'underway' : 'ahead';
  return `${moment}-${resolveLoad(summary)}` as DayTier;
}

const plural = (n: number, one: string, many: string) => (n === 1 ? one : many);

/** « 4 rendez-vous » — invariable in the plural, which is why it is written once rather than pluralised. */
const rdv = (n: number) => `${n} rendez-vous`;

/** « — encore 2 h 30 au fauteuil », or nothing when the figure is too small to be worth a clause. */
function chairTimeLeft(summary: DaySummary): string {
  return summary.remainingMinutes >= MIN_REMAINING_MINUTES_WORTH_SAYING
    ? ` — encore ${formatDuration(summary.remainingMinutes)} au fauteuil`
    : '';
}

/** The morning line: how much, and when it starts. */
function buildAheadSubline(load: DayLoad, summary: DaySummary): string {
  const nudge: Record<DayLoad, string> = {
    light: ' Vous avez de la marge.',
    steady: '',
    busy: ' Bon courage.',
    packed: ' Pensez à souffler entre deux.',
  };

  const start =
    summary.firstStartMinutes !== null
      ? summary.count === 1
        ? `, à ${formatClock(summary.firstStartMinutes)}`
        : `, le premier à ${formatClock(summary.firstStartMinutes)}`
      : '';

  return `${rdv(summary.count)} aujourd’hui${start}.${nudge[load]}`;
}

/** The mid-day line: what is LEFT, because the total stopped being the useful figure hours ago. */
function buildUnderwaySubline(summary: DaySummary): string {
  if (summary.doneCount === 0) {
    // Somebody holds the chair but nothing has passed — the total is still the only honest figure.
    return `${rdv(summary.count)} aujourd’hui, la journée vient de commencer.`;
  }
  // What is LEFT leads the sentence: « 11 rendez-vous » is true all day and stops being the useful figure the
  // moment the first patient walks out.
  const passed = `${summary.doneCount} déjà ${plural(summary.doneCount, 'passé', 'passés')}`;
  return `${rdv(summary.remainingCount)} à venir, ${passed}${chairTimeLeft(summary)}.`;
}

/** « Plus qu'un » deserves the clock time, since it is the only thing left to plan around. */
function buildLastOneSubline(summary: DaySummary): string {
  // `doneCount >= 1` is guaranteed by the tier, so « les autres » always has an antecedent.
  const others =
    summary.doneCount === 1
      ? 'le premier est déjà passé'
      : `les ${summary.doneCount} autres sont déjà passés`;

  const base =
    summary.nextPatientStartMinutes !== null
      ? `Dernier rendez-vous à ${formatClock(summary.nextPatientStartMinutes)} — ${others}.`
      : `Le dernier patient est au fauteuil — ${others}.`;

  return summary.unclosedCount > 0
    ? `${base} Il reste ${summary.unclosedCount} ${plural(summary.unclosedCount, 'séance', 'séances')} à clôturer.`
    : base;
}

/**
 * The closing line.
 *
 * ⚠️ `doneCount`, never `count`. `count` is patients *booked*, and it was rendered under the label « patient
 * vu » — so a day whose only séance was still open read « 1 patient vu » in the headline while the card below
 * showed that same patient as being treated. Two opposite claims about one séance, one line apart.
 *
 * And « vu » is only said when nothing is left to close: `doneCount` means the slot has passed, which is not the
 * same as somebody confirming the patient came — that is exactly what `AwaitingClosure` withholds.
 */
function buildClosingSubline(tier: 'done' | 'evening', summary: DaySummary, nowMinutes: number): string {
  if (summary.unclosedCount > 0) {
    const ended = `${summary.doneCount} ${plural(summary.doneCount, 'séance terminée', 'séances terminées')}`;
    const tail = tier === 'evening' ? ' Bonne soirée.' : '';
    return `${ended} — ${summary.unclosedCount} à clôturer.${tail}`;
  }

  const seen = `${summary.doneCount} ${plural(summary.doneCount, 'patient vu', 'patients vus')} aujourd’hui.`;
  if (tier === 'evening') return `${seen} Bonne soirée.`;
  // The programme is finished but the cabinet is not: saying until when is what stops « terminé » reading as
  // « rentrez chez vous » to whoever still has to answer the door.
  return summary.openToMinutes !== null && nowMinutes < summary.openToMinutes
    ? `${seen} Le cabinet reste ouvert jusqu’à ${formatClock(summary.openToMinutes)}.`
    : seen;
}

/** The generated half — the fact, in French, from the day's own figures. */
function buildSubline(tier: DayTier, summary: DaySummary, nowMinutes: number): string {
  switch (tier) {
    case 'closed':
      return 'Aucune ouverture prévue aujourd’hui — les horaires se règlent dans « Paramètres ».';
    case 'empty':
      // « Le planning est vide » is false on a day held by blocked hours, and the ribbon below it shows them.
      return summary.blockedCount > 0
        ? `Aucun patient prévu, seulement ${summary.blockedCount} ${plural(summary.blockedCount, 'créneau bloqué', 'créneaux bloqués')}.`
        : 'Le planning est vide — de quoi avancer sur les dossiers en attente.';
    case 'last-one':
      return buildLastOneSubline(summary);
    case 'done':
    case 'evening':
      return buildClosingSubline(tier, summary, nowMinutes);
    default:
      return tier.startsWith('underway-')
        ? buildUnderwaySubline(summary)
        : buildAheadSubline(resolveLoad(summary), summary);
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
 * (clinic, day, tier): stable for as long as the day stays in one register, identical for two staff at the same
 * practice, and it changes exactly when the day genuinely changes register — a morning that fills up moves from
 * « tranquille » to « ruche », and an afternoon moves from « devant vous » to « on tient le rythme », which is
 * correct rather than a glitch.</p>
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
  return {
    emoji: chosen.emoji,
    headline: chosen.headline,
    subline: buildSubline(tier, summary, nowMinutes),
  };
}
