/**
 * French formatting for the console, in the clinic's own timezone.
 *
 * ⚠️ **Every instant is rendered in Tunisia's zone (UTC+1), not the viewer's.** The vendor may be reading this
 * from anywhere, and « dernière écriture le 9 août » has to mean the day it was at the practice — otherwise a
 * figure measured over the cabinet's days is labelled with somebody else's, and the two disagree for the first
 * hour of every day. This is the client-side half of the rule `ClinicClock` states on the server.
 */

const CLINIC_TIME_ZONE = "Africa/Tunis";

const DATE = new Intl.DateTimeFormat("fr-FR", {
  timeZone: CLINIC_TIME_ZONE,
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
});

const DATE_TIME = new Intl.DateTimeFormat("fr-FR", {
  timeZone: CLINIC_TIME_ZONE,
  day: "2-digit",
  month: "2-digit",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
});

const MONEY = new Intl.NumberFormat("fr-FR", {
  minimumFractionDigits: 3,
  maximumFractionDigits: 3,
});

/** A date, or the em dash. ⚠️ Never « 0 » or « — » decided per call site: one wording, one place. */
export function formatDate(value: string | null | undefined): string {
  const date = parse(value);
  return date ? DATE.format(date) : EM_DASH;
}

export function formatDateTime(value: string | null | undefined): string {
  const date = parse(value);
  return date ? DATE_TIME.format(date) : EM_DASH;
}

/** Millimes, always three decimals — the product's money convention everywhere else. */
export function formatMoney(value: number | null | undefined): string {
  return value === null || value === undefined ? EM_DASH : `${MONEY.format(value)} DT`;
}

export function formatCount(value: number | null | undefined): string {
  return value === null || value === undefined ? EM_DASH : new Intl.NumberFormat("fr-FR").format(value);
}

/**
 * « il y a 3 jours » in the clinic's days — how the freshness line reads at a glance. Deliberately coarse: the
 * exact instant is shown beside it, and a figure measured this morning does not become more trustworthy for
 * being described to the minute.
 */
export function formatFreshness(value: string | null | undefined): string {
  const date = parse(value);
  if (!date) return "jamais";

  const days = Math.floor((Date.now() - date.getTime()) / 86_400_000);
  if (days <= 0) return "aujourd'hui";
  if (days === 1) return "hier";
  return `il y a ${days} jours`;
}

export const EM_DASH = "—";

function parse(value: string | null | undefined): Date | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}
