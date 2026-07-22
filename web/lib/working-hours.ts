// Clinic working hours (reliability-and-polish AC-7). One shared shape + default + summary so the settings
// card and the sidebar footer never carry divergent hardcoded values.

export interface WorkingDay {
  day: string;
  enabled: boolean;
  from: string;
  to: string;
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
  let run: { start: string; end: string; from: string; to: string } | null = null;
  const flush = () => {
    if (!run) return;
    const label =
      run.start === run.end
        ? FR_DAY_ABBREV[run.start] ?? run.start
        : `${FR_DAY_ABBREV[run.start] ?? run.start}–${FR_DAY_ABBREV[run.end] ?? run.end}`;
    lines.push(`${label} : ${run.from}–${run.to}`);
    run = null;
  };
  for (const h of hours) {
    if (!h.enabled) {
      flush();
      continue;
    }
    if (run && run.from === h.from && run.to === h.to) {
      run.end = h.day;
    } else {
      flush();
      run = { start: h.day, end: h.day, from: h.from, to: h.to };
    }
  }
  flush();
  return lines.length > 0 ? lines : ["Fermé"];
}
