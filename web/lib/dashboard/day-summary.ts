import { format } from 'date-fns';
import { fr } from 'date-fns/locale';

import { isBusySlot } from '@/components/appointment-labels';
import type { AppointmentDto } from '@/lib/api/types';
import { parseDurationToMinutes } from '@/lib/utils';
import { DEFAULT_WORKING_HOURS, WEEKDAYS, type WorkingDay } from '@/lib/working-hours';

/**
 * Everything the day zones of the dashboard need, derived from data the page already has.
 *
 * <p><b>No backend read is involved.</b> `app/page.tsx` already fetches today's appointments for the
 * « Rendez-vous du jour » list (`useAppointments(today, today)`), and an `AppointmentDto` carries the start
 * instant, the duration, the status, the patient's name and — since `multi-act-appointments` — every act of the
 * séance with its own name, minutes and catalogue colour. Count, chair time, act mix, the free gaps, the current
 * and next patient and the day's end all fall out of that. A fifth dashboard section reader would have been a
 * serial round trip for figures already on the wire.</p>
 *
 * <p>Everything here is a pure function of its arguments — no clock read, no `Date.now()` inside. `nowMinutes` is
 * passed in for the reason `SubscriptionWarningJob` takes « today » as a parameter: « what is on screen at 11:40 »
 * is otherwise untestable, and midday is the only boundary that matters for a value that arrives by itself.</p>
 */

/**
 * The statuses that still occupy the chair.
 *
 * <p>Compared lower-cased because the wire sends the enum's own name (`"InProgress"`) while
 * `edit-appointment-dialog` really does push lower-cased forms through — the same reason
 * `appointment-labels.ts` carries a `normalizeStatus`.</p>
 */
const OCCUPYING = new Set(['scheduled', 'confirmed', 'inprogress', 'awaitingclosure', 'completed']);

/**
 * The statuses that no longer want the chair.
 *
 * <p>⚠️ `awaitingclosure` is deliberately **absent**. « Séance passée » says the slot has ended, not that the
 * visit is resolved — nobody has confirmed the patient came — so treating it as finished would drop it out of
 * `next` and out of the chair ranking, which is the one place a forgotten visit still needs to be visible.
 * `isPast` already covers the clock half by comparing the slot's own end.</p>
 */
const FINISHED = new Set(['completed']);

/** A free stretch of the working day. Only ones worth acting on are reported — see {@link MIN_GAP_MINUTES}. */
export interface DayGap {
  startMinutes: number;
  endMinutes: number;
  minutes: number;
}

/** One act type across the whole day, with how many were booked and how long they take. */
export interface DayAct {
  /** The catalogue id, or the act's own name when it is a hand-typed devis line with no catalogue entry. */
  key: string;
  name: string;
  /** `null` for a link-only row — a devis step that matches no catalogue act, so it has no colour of its own. */
  colorHex: string | null;
  count: number;
  minutes: number;
}

/** One appointment placed on the ribbon. */
export interface DaySlot {
  appointment: AppointmentDto;
  startMinutes: number;
  endMinutes: number;
  /** The lead act's colour, or `null` when the visit names no catalogue act. */
  colorHex: string | null;
  isPast: boolean;
  isCurrent: boolean;
}

export interface DaySummary {
  /**
   * Every occupying row, ordered by start — **patients and « créneaux occupés » alike**. Cancelled and no-show
   * are not today's work; a blocked hour is, which is why it is drawn on the ribbon.
   */
  slots: DaySlot[];
  /**
   * Patients booked today. **A « créneau occupé » is not one of them.**
   *
   * <p>The split below is the rule: figures about <b>people</b> count appointments, figures about <b>time and
   * shape</b> count every slot. A blocked hour is not a rendez-vous and its passing is not a patient seen — but
   * the chair really is unavailable for it, which is the whole point of blocking it out.</p>
   */
  count: number;
  /** « Créneaux occupés » today. Stated rather than hidden: it is why the load can exceed the visit count. */
  blockedCount: number;
  /** Acts, not appointments — a séance routinely carries several, so this is normally the larger number. */
  actCount: number;
  /** Act types, busiest first. */
  acts: DayAct[];
  /** Chair minutes — **blocked slots included**, since the time is genuinely spent. */
  bookedMinutes: number;
  /**
   * The clinic's open minutes for this weekday, or `null` when the day is not configured.
   *
   * <p>`null` is not zero and the two must not be conflated: an unconfigured clinic has no denominator, so
   * {@link loadPercent} is absent rather than computed against a guess.</p>
   */
  openMinutes: number | null;
  /**
   * The clinic's closing time as minutes from local midnight, or `null` when the day is not configured.
   *
   * <p>Distinct from {@link openMinutes}, which is a <i>duration</i>. This is the instant, and it is what tells the
   * greeting whether « bonne soirée » is true yet — a practice closing at 17:00 is in the evening at 17:30, one
   * closing at 20:00 is not, and a single hardcoded hour is wrong for one of them.</p>
   */
  openToMinutes: number | null;
  /** `bookedMinutes / openMinutes`, rounded. `null` whenever {@link openMinutes} is. */
  loadPercent: number | null;
  /** The ribbon's own bounds, in minutes from local midnight. */
  windowFrom: number;
  windowTo: number;
  gaps: DayGap[];
  current: DaySlot | null;
  next: DaySlot | null;
  /**
   * The séance that has ended, is past {@link CHAIR_OVERRUN_GRACE_MINUTES}, and which nobody has answered for —
   * earliest first. `null` when there is none, and always `null` while {@link current} holds the chair.
   *
   * <p>It is the third state of the now/next pair rather than a silent omission: dropping the card would take the
   * patient's <i>name</i> off the screen, and « N séances à clôturer » on the à-traiter chip cannot say who. § 0 —
   * no capability removed by a layout decision.</p>
   */
  needsClosure: DaySlot | null;
  /** How many séances are in {@link needsClosure}'s state. Drives the greeting's « — M à clôturer ». */
  unclosedCount: number;
  /** Patients whose slot has passed. **Not** « vus »: an unclosed séance is here too — see {@link unclosedCount}. */
  doneCount: number;
  /** Patients still to come. */
  remainingCount: number;
  /**
   * When the day's first **patient** is due, or `null` on a day with nothing booked.
   *
   * <p>Patient-only, and that is the point: a 07:00 « créneau occupé » for stock-taking is not when the practice
   * starts seeing people, so « le premier à 07:00 » would be a confident wrong answer to the one question the
   * greeting asks in the morning.</p>
   */
  firstStartMinutes: number | null;
  /**
   * The next patient still ahead — not in the chair, not passed — or `null` when the last one is being treated.
   *
   * <p>Distinct from {@link next}, which is the ribbon's next *slot* and may well be a blocked hour.</p>
   */
  nextPatientStartMinutes: number | null;
  /** Chair minutes still ahead: every patient slot not yet passed, the one in the chair included. */
  remainingMinutes: number;
  /** When the last occupying slot ends — blocks included, because « fin prévue » means the day's own end. */
  endsAtMinutes: number | null;
  /**
   * Every **patient** slot of the day has passed, and nobody is in the chair. Drives the closing register of the
   * greeting — but only *that* the programme is finished, never that it is evening: `day-phrases.ts` reads the clock
   * for the second half, because « le programme est terminé » and « il fait nuit » are independent facts and
   * conflating them said « Bonne soirée » at midday.
   *
   * <p>Deliberately not keyed on {@link endsAtMinutes}: a blocked hour at the end of the day is admin time, and the
   * programme is finished the moment the last patient's slot ends.</p>
   */
  isOver: boolean;
  /** The clinic does not open on this weekday at all. Distinct from « nothing was booked ». */
  isClosedToday: boolean;
}

/** Below this, a hole in the day is turnaround rather than a slot anyone can fill. */
export const MIN_GAP_MINUTES = 30;

/**
 * How long past its own end a started séance may still be called « au fauteuil ».
 *
 * <p>A visit routinely runs over, and `AppointmentProgressJob` relabels it `AwaitingClosure` within a minute of the
 * booked slot ending — so some grace is required or the ordinary long visit loses the chair. Past it, the honest
 * statement is « à clôturer », not « au fauteuil ». This is a <b>rendering</b> tolerance, not a clinical rule: how
 * long a séance may legitimately run is the practice's business, and nothing here writes a status.</p>
 */
export const CHAIR_OVERRUN_GRACE_MINUTES = 30;

/** Minutes from local midnight for a `HH:mm` string; `null` when it is not one. */
function parseClock(value: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  if (hours > 23 || minutes > 59) return null;
  return hours * 60 + minutes;
}

/** Minutes from local midnight for an instant, read in the workstation's own zone (`format.ts`'s convention). */
export function minutesOfDay(date: Date): number {
  return date.getHours() * 60 + date.getMinutes();
}

/** `510` → `"08:30"`. */
export function formatClock(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  return `${String(Math.floor(m / 60) % 24).padStart(2, '0')}:${String(m % 60).padStart(2, '0')}`;
}

/**
 * Up to two initials for an avatar or a ribbon block.
 *
 * <p>Shared by the ribbon and the day's list rather than written twice — they sit on the same screen, so two
 * implementations would be visible side by side the first time they disagreed about a compound surname.</p>
 */
export function initialsOf(name: string | null | undefined): string {
  const parts = (name ?? '').trim().split(/\s+/).filter(Boolean);
  const initials = `${parts[0]?.[0] ?? ''}${parts[1]?.[0] ?? ''}`;
  return initials.toUpperCase() || '?';
}

/** « 1 h 45 », « 45 min », « 6 h ». The French spacing the rest of the product uses. */
export function formatDuration(minutes: number): string {
  const m = Math.max(0, Math.round(minutes));
  if (m < 60) return `${m} min`;
  const hours = Math.floor(m / 60);
  const rest = m % 60;
  return rest === 0 ? `${hours} h` : `${hours} h ${String(rest).padStart(2, '0')}`;
}

const statusOf = (a: AppointmentDto) => (a.status ?? '').toLowerCase();

/** The working day for a given date, falling back to the shared default when the clinic has saved none. */
function workingDayFor(hours: WorkingDay[] | null | undefined, date: Date): WorkingDay | null {
  const source = hours && hours.length > 0 ? hours : null;
  // `Date.getDay()` is Sunday-based; WEEKDAYS is Monday-based, which is also how the agenda's week starts.
  const key = WEEKDAYS[(date.getDay() + 6) % 7];
  const found = (source ?? DEFAULT_WORKING_HOURS).find((d) => d.day === key);
  // Only a *saved* schedule may declare the clinic closed. With none saved we are guessing, and guessing
  // « fermé » would tell a practice with eight patients booked that it is shut.
  if (!found) return null;
  if (!found.enabled) return source ? null : null;
  return found;
}

/**
 * How strongly a slot claims « Au fauteuil ». Higher wins; `0` is no claim at all.
 *
 * <p><b>Ranked rather than first-match, and that is the whole point.</b> `InProgress` is a status somebody has to
 * clear, so a blocked 11:00–11:30 slot still flagged en cours at 12:39 used to win on start order — and then, by
 * holding the chair, it pushed the visit genuinely running out of `next`, which only looks *forward*. The patient
 * actually being treated appeared on neither card.</p>
 *
 * <p>A patient outranks a « créneau occupé »: a blocked hour is the practitioner's own time, not somebody in the
 * chair. Within each, a slot whose window contains now outranks one merely left open.</p>
 *
 * <p>⚠️ `started` counts `awaitingclosure` too, and it has to: that status is the successor of « InProgress past
 * its own slot » — `AppointmentProgressJob` renames it within a minute of the slot ending — so testing
 * `inprogress` alone would withdraw the chair from a visit running fifteen minutes long, which is the ordinary
 * case rather than a stale one.</p>
 *
 * <p>⚠️ <b>It is bounded, and the unbounded version was a defect.</b> This function once carried « deliberately no
 * staleness cutoff », which was right while `InProgress` was the only signal — a status somebody has to clear says
 * nothing about the clock. `AwaitingClosure` says the opposite: the slot has ended and nobody has confirmed the
 * patient came. Trusting it for ever put a visit that finished at 10:00 « au fauteuil · depuis 2 h 59 » at midday,
 * while the same séance was counted as already seen in the greeting above it. Past the grace the slot is not in the
 * chair — it is {@link DaySummary.needsClosure}, which is a different card and a different question.</p>
 */
function chairClaim(slot: DaySlot, nowMinutes: number): number {
  if (FINISHED.has(statusOf(slot.appointment))) return 0;
  const running = slot.startMinutes <= nowMinutes && nowMinutes < slot.endMinutes;
  const status = statusOf(slot.appointment);
  const started = status === 'inprogress' || status === 'awaitingclosure';
  if (!running && !started) return 0;
  if (!running && nowMinutes >= slot.endMinutes + CHAIR_OVERRUN_GRACE_MINUTES) return 0;
  return (isBusySlot(slot.appointment) ? 0 : 4) + (running ? 2 : 1);
}

/** Whether this slot has ended, is past the grace, and still has nobody saying the patient came. */
function isAwaitingClosure(slot: DaySlot, nowMinutes: number): boolean {
  if (isBusySlot(slot.appointment)) return false;
  const status = statusOf(slot.appointment);
  if (status !== 'inprogress' && status !== 'awaitingclosure') return false;
  return nowMinutes >= slot.endMinutes + CHAIR_OVERRUN_GRACE_MINUTES;
}

/**
 * Fold today's appointments into everything the day zones render.
 *
 * @param appointments every appointment the day's fetch returned, cancelled ones included — they are filtered here
 *                     so one rule decides what « today's work » means.
 * @param workingHours the clinic's saved schedule, or `null`/`[]` when it has none.
 * @param now          the instant to measure « past », « current » and « next » against.
 */
export function buildDaySummary(
  appointments: AppointmentDto[],
  workingHours: WorkingDay[] | null | undefined,
  now: Date,
): DaySummary {
  const nowMinutes = minutesOfDay(now);
  const hasSavedHours = Boolean(workingHours && workingHours.length > 0);
  const today = workingDayFor(workingHours, now);
  const openFrom = today ? parseClock(today.from) : null;
  const openTo = today ? parseClock(today.to) : null;
  const hasOpenWindow = openFrom !== null && openTo !== null && openTo > openFrom;

  const occupying = appointments
    .filter((a) => OCCUPYING.has(statusOf(a)))
    .map<DaySlot>((appointment) => {
      const start = new Date(appointment.appointmentDateTime);
      const startMinutes = minutesOfDay(start);
      const duration = Math.max(5, parseDurationToMinutes(appointment.duration));
      const endMinutes = startMinutes + duration;
      return {
        appointment,
        startMinutes,
        endMinutes,
        colorHex: leadColour(appointment),
        isPast: endMinutes <= nowMinutes || FINISHED.has(statusOf(appointment)),
        isCurrent: false,
      };
    })
    .sort((a, b) => a.startMinutes - b.startMinutes || a.endMinutes - b.endMinutes);

  let current: DaySlot | null = null;
  let bestClaim = 0;
  for (const slot of occupying) {
    const claim = chairClaim(slot, nowMinutes);
    // `>` and not `>=`, so a tie keeps the earlier slot — `occupying` is already sorted by start.
    if (claim > bestClaim) {
      bestClaim = claim;
      current = slot;
    }
  }
  if (current) current = { ...current, isCurrent: true, isPast: false };

  const slots = occupying.map((s) =>
    current && s.appointment.id === current.appointment.id ? current : s,
  );

  const next =
    slots.find((s) => s.startMinutes > nowMinutes && !s.isCurrent && !FINISHED.has(statusOf(s.appointment))) ?? null;

  const bookedMinutes = slots.reduce((sum, s) => sum + (s.endMinutes - s.startMinutes), 0);
  const firstStart = slots.length > 0 ? slots[0].startMinutes : null;
  const endsAtMinutes = slots.length > 0 ? Math.max(...slots.map((s) => s.endMinutes)) : null;

  /*
   * The ribbon's window is a UNION, exactly as the agenda's `gridWindow` is: the configured hours *and* every
   * appointment booked today. A 07:00 emergency or a visit running past closing must extend the ribbon rather
   * than be drawn outside it — § 0, no capability removed by a layout decision.
   */
  const candidatesFrom = [hasOpenWindow ? (openFrom as number) : null, firstStart].filter(
    (v): v is number => v !== null,
  );
  const candidatesTo = [hasOpenWindow ? (openTo as number) : null, endsAtMinutes].filter(
    (v): v is number => v !== null,
  );
  const windowFrom = candidatesFrom.length > 0 ? Math.min(...candidatesFrom) : 9 * 60;
  const rawTo = candidatesTo.length > 0 ? Math.max(...candidatesTo) : 17 * 60;
  // A window narrower than an hour makes every block full-width and says nothing about the shape of the day.
  const windowTo = Math.max(rawTo, windowFrom + 60);

  const openMinutes = hasOpenWindow ? (openTo as number) - (openFrom as number) : null;
  const loadPercent =
    openMinutes && openMinutes > 0 ? Math.round((bookedMinutes / openMinutes) * 100) : null;

  // Free stretches, walked in order. `cursor` is a running max because two visits can overlap — the agenda
  // allows a confirmed double-booking — and a naive walk would then report a negative gap.
  const gaps: DayGap[] = [];
  let cursor = windowFrom;
  for (const slot of slots) {
    if (slot.startMinutes - cursor >= MIN_GAP_MINUTES) {
      gaps.push({ startMinutes: cursor, endMinutes: slot.startMinutes, minutes: slot.startMinutes - cursor });
    }
    cursor = Math.max(cursor, slot.endMinutes);
  }

  // People vs. time: a « créneau occupé » holds the chair (so it is in `slots`, `bookedMinutes`, the gaps and
  // the ribbon) but is nobody's appointment, so it counts toward none of the figures below.
  const patientSlots = slots.filter((s) => !isBusySlot(s.appointment));
  const doneCount = patientSlots.filter((s) => s.isPast).length;
  const lastPatientEnds =
    patientSlots.length > 0 ? Math.max(...patientSlots.map((s) => s.endMinutes)) : null;
  const aheadSlots = patientSlots.filter((s) => !s.isPast);
  const stillToStart = aheadSlots.filter((s) => !s.isCurrent);

  // `slots` is ordered by start, so the first match is the oldest thing still owed an answer.
  const unclosed = patientSlots.filter((s) => !s.isCurrent && isAwaitingClosure(s, nowMinutes));

  return {
    slots,
    count: patientSlots.length,
    blockedCount: slots.length - patientSlots.length,
    actCount: countActs(patientSlots),
    acts: buildActMix(patientSlots),
    bookedMinutes,
    openMinutes,
    openToMinutes: hasOpenWindow ? (openTo as number) : null,
    loadPercent,
    windowFrom,
    windowTo,
    gaps,
    current,
    next,
    // Never both: whoever is in the chair is the more urgent statement, and two cards about overdue séances
    // beside each other is the nagging `VisitClosureState.NextStep` exists to avoid.
    needsClosure: current ? null : (unclosed[0] ?? null),
    unclosedCount: unclosed.length,
    doneCount,
    remainingCount: patientSlots.length - doneCount,
    // `patientSlots` inherits `slots`' ordering, so the first entry is the day's opening visit.
    firstStartMinutes: patientSlots.length > 0 ? patientSlots[0].startMinutes : null,
    nextPatientStartMinutes: stillToStart.length > 0 ? stillToStart[0].startMinutes : null,
    remainingMinutes: aheadSlots.reduce((sum, s) => sum + (s.endMinutes - s.startMinutes), 0),
    endsAtMinutes,
    // `current === null` is load-bearing, not belt-and-braces: without it « Journée terminée » rendered above a
    // « Au fauteuil » card naming the patient still being treated — the last séance of the day being the case.
    isOver: lastPatientEnds !== null && nowMinutes >= lastPatientEnds && current === null,
    // Only a schedule the clinic actually saved may say « fermé ». The shared default is a guess.
    isClosedToday: hasSavedHours && !hasOpenWindow,
  };
}

/**
 * How far ahead {@link resolveNextOpenDay} looks. A week, because the schedule itself is weekly — past seven days
 * there is nothing new to find, only the same pattern again.
 */
const MAX_LOOKAHEAD_DAYS = 7;

/**
 * The clinic's next **open** day after `from`, or `null` when it opens on none of the following seven.
 *
 * <p><b>Not « demain ».</b> A practice closed on Sunday would be told « Demain — fermé » every Saturday evening —
 * useless at exactly the moment somebody is planning. The question staff actually ask is « ma prochaine journée
 * ouvrée », which on a Friday evening is Monday.</p>
 *
 * <p>Days are composed with the local-calendar constructor (`new Date(y, m, d + n)`), which rolls over months and
 * years correctly and yields local midnight — never UTC arithmetic, for `todayLocalIso`'s reason.</p>
 */
export function resolveNextOpenDay(
  workingHours: WorkingDay[] | null | undefined,
  from: Date,
): Date | null {
  for (let offset = 1; offset <= MAX_LOOKAHEAD_DAYS; offset += 1) {
    const candidate = new Date(from.getFullYear(), from.getMonth(), from.getDate() + offset);
    if (workingDayFor(workingHours, candidate)) return candidate;
  }
  return null;
}

/**
 * What one **future** day is worth saying in a single line.
 *
 * <p><b>Deliberately not a {@link DaySummary}</b>, though it is projected from one. That type carries `current`,
 * `isCurrent`, `isOver` and `gaps`, every one of which is meaningless or actively misleading about a day that has
 * not started — so returning it would let a caller point {@link DaySlot}-shaped components like the ribbon or the
 * now/next pair at tomorrow and get a plausible-looking lie. A narrower type makes that unrepresentable rather
 * than merely discouraged.</p>
 */
export interface DayPreview {
  /** Local midnight of the day described. */
  day: Date;
  /** « Demain » only when it really is the next calendar day, else the weekday (« Lundi »). */
  label: string;
  count: number;
  /** Minutes from midnight of the first booked visit; `null` on a day with nothing booked. */
  firstStartMinutes: number | null;
  bookedMinutes: number;
  acts: DayAct[];
}

/**
 * Fold a future day's appointments into {@link DayPreview}.
 *
 * <p>It runs the same {@link buildDaySummary} the day board uses, anchored at that day's own midnight, and then
 * projects — so « combien d'actes » and « combien de temps au fauteuil » have one implementation rather than a
 * second one that agrees only by coincidence. Anchoring at midnight is also what makes the reused summary
 * correct: `workingDayFor` resolves the *target* day's weekday, and nothing is `isPast`.</p>
 */
export function buildDayPreview(
  appointments: AppointmentDto[],
  workingHours: WorkingDay[] | null | undefined,
  day: Date,
  today: Date,
): DayPreview {
  const startOfDay = new Date(day.getFullYear(), day.getMonth(), day.getDate());
  const summary = buildDaySummary(appointments, workingHours, startOfDay);

  return {
    day: startOfDay,
    label: previewLabel(startOfDay, today),
    count: summary.count,
    // Patient-only, so « dès 08:30 » names when people arrive rather than when a blocked hour opens the ribbon.
    firstStartMinutes: summary.firstStartMinutes,
    bookedMinutes: summary.bookedMinutes,
    acts: summary.acts,
  };
}

/** « Demain » or a capitalised weekday. Compared on local calendar parts — no string, no instant, no timezone. */
function previewLabel(day: Date, today: Date): string {
  const tomorrow = new Date(today.getFullYear(), today.getMonth(), today.getDate() + 1);
  const isTomorrow =
    day.getFullYear() === tomorrow.getFullYear() &&
    day.getMonth() === tomorrow.getMonth() &&
    day.getDate() === tomorrow.getDate();

  if (isTomorrow) return 'Demain';

  const weekday = format(day, 'EEEE', { locale: fr });
  return weekday.charAt(0).toUpperCase() + weekday.slice(1);
}

/** The lead act's colour — the snapshot the agenda already paints with, falling back to the first child row. */
function leadColour(appointment: AppointmentDto): string | null {
  if (appointment.procedureColorHex) return appointment.procedureColorHex;
  const first = (appointment.procedures ?? []).find((p) => p.colorHex);
  return first?.colorHex ?? null;
}

/**
 * The acts of one visit.
 *
 * <p>Falls back to the lead-act scalar when the child collection is absent, which is what keeps an appointment
 * booked before `multi-act-appointments` from contributing nothing to the mix.</p>
 */
function actsOf(appointment: AppointmentDto) {
  const children = appointment.procedures ?? [];
  if (children.length > 0) {
    return children.map((p) => ({
      key: p.procedureTypeId ?? `name:${p.name ?? ''}`,
      name: p.name?.trim() || 'Acte',
      colorHex: p.colorHex ?? null,
      minutes: p.durationMinutes ?? 0,
    }));
  }
  if (appointment.procedureTypeName) {
    // The lead-act snapshot carries no duration on the wire, so this act takes the visit's own length
    // through `buildActMix`'s share.
    return [
      {
        key: appointment.procedureTypeId ?? `name:${appointment.procedureTypeName}`,
        name: appointment.procedureTypeName,
        colorHex: appointment.procedureColorHex ?? null,
        minutes: 0,
      },
    ];
  }
  return [];
}

function countActs(slots: DaySlot[]): number {
  return slots.reduce((sum, s) => sum + actsOf(s.appointment).length, 0);
}

/**
 * The day's act mix, busiest first.
 *
 * <p>Fanned out over the acts, <b>not</b> over the appointments: « 13 actes » across « 11 rendez-vous » is the
 * honest pair, and labelling this « rendez-vous » would contradict the count beside it. A hand-typed devis line
 * has a null `procedureTypeId` and contributes no duration — it is still real work, so it is grouped under its
 * own name rather than dropped.</p>
 *
 * <p>An act with no recorded duration falls back to a share of its visit's length, so « minutes par acte » stays
 * a complete figure rather than silently under-counting the séances booked before per-act durations existed.</p>
 */
function buildActMix(slots: DaySlot[]): DayAct[] {
  const byKey = new Map<string, DayAct>();

  for (const slot of slots) {
    const acts = actsOf(slot.appointment);
    if (acts.length === 0) continue;
    const visitMinutes = slot.endMinutes - slot.startMinutes;
    const declared = acts.reduce((sum, a) => sum + a.minutes, 0);
    const share = declared > 0 ? 0 : Math.round(visitMinutes / acts.length);

    for (const act of acts) {
      const existing = byKey.get(act.key);
      const minutes = act.minutes > 0 ? act.minutes : share;
      if (existing) {
        existing.count += 1;
        existing.minutes += minutes;
        // A live catalogue colour beats an absent one — a retired act keeps rendering with its snapshot.
        if (!existing.colorHex && act.colorHex) existing.colorHex = act.colorHex;
      } else {
        byKey.set(act.key, { key: act.key, name: act.name, colorHex: act.colorHex, count: 1, minutes });
      }
    }
  }

  return [...byKey.values()].sort((a, b) => b.count - a.count || b.minutes - a.minutes || a.name.localeCompare(b.name, 'fr'));
}
