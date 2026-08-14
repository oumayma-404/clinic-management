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
const OCCUPYING = new Set(['scheduled', 'confirmed', 'inprogress', 'completed']);
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
  /** Occupying appointments only, ordered by start. Cancelled and no-show are not today's work. */
  slots: DaySlot[];
  count: number;
  /** Acts, not appointments — a séance routinely carries several, so this is normally the larger number. */
  actCount: number;
  /** Act types, busiest first. */
  acts: DayAct[];
  bookedMinutes: number;
  /**
   * The clinic's open minutes for this weekday, or `null` when the day is not configured.
   *
   * <p>`null` is not zero and the two must not be conflated: an unconfigured clinic has no denominator, so
   * {@link loadPercent} is absent rather than computed against a guess.</p>
   */
  openMinutes: number | null;
  /** `bookedMinutes / openMinutes`, rounded. `null` whenever {@link openMinutes} is. */
  loadPercent: number | null;
  /** The ribbon's own bounds, in minutes from local midnight. */
  windowFrom: number;
  windowTo: number;
  gaps: DayGap[];
  current: DaySlot | null;
  next: DaySlot | null;
  doneCount: number;
  remainingCount: number;
  /** When the last booked visit ends, or `null` on an empty day. */
  endsAtMinutes: number | null;
  /** Every booked visit has finished. Drives the closing register of the greeting. */
  isOver: boolean;
  /** The clinic does not open on this weekday at all. Distinct from « nothing was booked ». */
  isClosedToday: boolean;
}

/** Below this, a hole in the day is turnaround rather than a slot anyone can fill. */
export const MIN_GAP_MINUTES = 30;

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

  /*
   * « Au fauteuil » prefers the recorded status over the clock.
   *
   * A visit the dentist actually started is `InProgress` whether or not it is running late, and a booking whose
   * slot merely contains the current minute may be a patient who has not arrived. The clock is the fallback for
   * the common case where nobody presses « Démarrer ».
   */
  let current =
    occupying.find((s) => statusOf(s.appointment) === 'inprogress') ??
    occupying.find(
      (s) => s.startMinutes <= nowMinutes && nowMinutes < s.endMinutes && !FINISHED.has(statusOf(s.appointment)),
    ) ??
    null;
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

  const doneCount = slots.filter((s) => s.isPast).length;

  return {
    slots,
    count: slots.length,
    actCount: countActs(slots),
    acts: buildActMix(slots),
    bookedMinutes,
    openMinutes,
    loadPercent,
    windowFrom,
    windowTo,
    gaps,
    current,
    next,
    doneCount,
    remainingCount: slots.length - doneCount,
    endsAtMinutes,
    isOver: slots.length > 0 && endsAtMinutes !== null && nowMinutes >= endsAtMinutes,
    // Only a schedule the clinic actually saved may say « fermé ». The shared default is a guess.
    isClosedToday: hasSavedHours && !hasOpenWindow,
  };
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
