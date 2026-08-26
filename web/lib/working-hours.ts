// Clinic working hours (reliability-and-polish AC-7). One shared shape + default + summary so the settings
// card and the sidebar footer never carry divergent hardcoded values.

export interface WorkingDay {
  day: string;
  enabled: boolean;
  from: string;
  to: string;
  /**
   * Optional mid-day closure (the Tunisian lunch break), `HH:mm` inside `[from, to]`. Both ends or neither.
   *
   * One contiguous window per day used to be the only shape the model could express, so a cabinet closing
   * 12:00–14:00 had to say 09:00–17:00 — and the booking guard then accepted 12:30, the agenda drew the
   * closure as open, and the reminder said the cabinet was expecting the patient.
   */
  breakFrom?: string | null;
  breakTo?: string | null;
}

export const WEEKDAYS = [
  "Monday",
  "Tuesday",
  "Wednesday",
  "Thursday",
  "Friday",
  "Saturday",
  "Sunday",
] as const;

/** The single default used when a clinic has no saved hours (Mon–Sat 09:00–17:00, Sunday closed). */
export const DEFAULT_WORKING_HOURS: WorkingDay[] = WEEKDAYS.map((day) => ({
  day,
  enabled: day !== "Sunday",
  from: "09:00",
  to: "17:00",
}));

/** Full French weekday labels. One copy: `clinic-settings` and `doctor-working-hours-card` each had their own. */
export const WEEKDAY_LABELS_FR: Record<string, string> = {
  Monday: "Lundi",
  Tuesday: "Mardi",
  Wednesday: "Mercredi",
  Thursday: "Jeudi",
  Friday: "Vendredi",
  Saturday: "Samedi",
  Sunday: "Dimanche",
};

const HHMM = /^\d{2}:\d{2}$/;

/** True when this day carries a mid-day closure — both ends present. */
export function hasBreak(day: WorkingDay): boolean {
  return Boolean(day.breakFrom && day.breakTo);
}

/**
 * Mirrors the server's `WorkingHoursSerializer.Validate` so an invalid row is refused before the round-trip and
 * the message **names the day**. The server stays the authority; this only means the user is told which day.
 *
 * ⚠️ One copy, called by both editors. `doctor-working-hours-card` had this and the clinic-wide editor had
 * nothing — so the same inverted-hours entry was caught by name on one screen and met a bare
 * « Horaires de travail invalides. » on the other, in the same file's sibling card.
 */
export function validateWorkingHours(days: WorkingDay[]): string | null {
  for (const day of days) {
    if (!day.enabled) continue;
    const label = WEEKDAY_LABELS_FR[day.day] ?? day.day;

    if (!HHMM.test(day.from) || !HHMM.test(day.to)) {
      return `${label} : heures invalides (format attendu HH:mm).`;
    }
    if (day.from >= day.to) {
      return `${label} : l'heure de fermeture doit être postérieure à l'ouverture.`;
    }

    // Half a break is not a break: accepting one end would store a closure with no other side, which
    // `IsWithin` cannot enforce and the summary cannot render.
    const openings = [day.breakFrom, day.breakTo].filter((value) => Boolean(value)).length;
    if (openings === 1) {
      return `${label} : indiquez le début ET la fin de la pause, ou laissez les deux vides.`;
    }
    if (openings === 0) continue;

    const breakFrom = day.breakFrom as string;
    const breakTo = day.breakTo as string;
    if (!HHMM.test(breakFrom) || !HHMM.test(breakTo)) {
      return `${label} : pause invalide (format attendu HH:mm).`;
    }
    if (breakFrom >= breakTo) {
      return `${label} : la fin de la pause doit être postérieure à son début.`;
    }
    if (breakFrom < day.from || breakTo > day.to) {
      return `${label} : la pause doit être comprise entre ${day.from} et ${day.to}.`;
    }
  }
  return null;
}

const FR_DAY_ABBREV: Record<string, string> = {
  Monday: "Lun",
  Tuesday: "Mar",
  Wednesday: "Mer",
  Thursday: "Jeu",
  Friday: "Ven",
  Saturday: "Sam",
  Sunday: "Dim",
};

/**
 * Compact French summary lines, grouping consecutive enabled days that share the same hours
 * (e.g. `["Lun–Sam : 09:00–17:00"]`). Returns `["Fermé"]` when nothing is enabled.
 */
export function summarizeWorkingHours(hours: WorkingDay[]): string[] {
  const lines: string[] = [];
  // ⚠️ The break is part of the run key. Two days sharing 09:00–17:00 where only one closes at midday are not
  // the same hours, and grouping them would print the break onto a day that does not have one.
  let run: { start: string; end: string; from: string; to: string; breakFrom: string; breakTo: string } | null = null;
  const flush = () => {
    if (!run) return;
    const label =
      run.start === run.end
        ? FR_DAY_ABBREV[run.start] ?? run.start
        : `${FR_DAY_ABBREV[run.start] ?? run.start}–${FR_DAY_ABBREV[run.end] ?? run.end}`;
    const window =
      run.breakFrom && run.breakTo
        ? `${run.from}–${run.breakFrom} · ${run.breakTo}–${run.to}`
        : `${run.from}–${run.to}`;
    lines.push(`${label} : ${window}`);
    run = null;
  };
  for (const h of hours) {
    if (!h.enabled) {
      flush();
      continue;
    }
    const breakFrom = hasBreak(h) ? (h.breakFrom as string) : "";
    const breakTo = hasBreak(h) ? (h.breakTo as string) : "";
    if (run && run.from === h.from && run.to === h.to && run.breakFrom === breakFrom && run.breakTo === breakTo) {
      run.end = h.day;
    } else {
      flush();
      run = { start: h.day, end: h.day, from: h.from, to: h.to, breakFrom, breakTo };
    }
  }
  flush();
  return lines.length > 0 ? lines : ["Fermé"];
}
