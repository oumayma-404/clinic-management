import { format, parseISO } from "date-fns";
import { fr } from "date-fns/locale";

/**
 * Format a Tunisian dinar amount to millimes (3 decimals) with the "DT" suffix, French grouping
 * (e.g. 1234.5 → "1 234,500 DT"). The app targets Tunisia; money is stored in millimes (decimal(18,3)).
 */
export function formatDT(amount: number | null | undefined): string {
  const value = amount ?? 0;
  const formatted = new Intl.NumberFormat("fr-TN", {
    minimumFractionDigits: 3,
    maximumFractionDigits: 3,
  }).format(value);
  return `${formatted} DT`;
}

/**
 * Round a dinar amount to the millime (3 decimals), away from zero — mirroring the backend's single rounding
 * authority (`InvoiceCalculator.RoundMoney`, `decimal(18,3)`). Apply it to any client-side money arithmetic
 * before displaying or sending a total, so float noise (110.001 × 3 = 330.00299999…) never surfaces.
 */
export function roundMillimes(value: number): number {
  if (!Number.isFinite(value)) return 0;
  const scaled = value * 1000;
  // Math.round breaks midpoints toward +∞, so negate to keep negatives away-from-zero too.
  const rounded = scaled < 0 ? -Math.round(-scaled) : Math.round(scaled);
  return rounded / 1000;
}

/**
 * True when an ISO date falls strictly before today — i.e. its calendar day has passed.
 *
 * Deliberately a CALENDAR-DAY comparison, not an instant one. Due dates are stored at midnight, so comparing
 * `new Date(iso) < Date.now()` reports a date as past from 00:00 on the day itself — a full day early, which is
 * how échéances due today were being badged « En retard ». Something due today is not late yet.
 *
 * Compares the `YYYY-MM-DD` prefixes as strings, so neither the viewer's timezone nor whether the API
 * serialises the value with a `Z` can shift which day it lands on.
 */
export function isBeforeToday(iso?: string | null): boolean {
  if (!iso) return false;
  const now = new Date();
  const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(
    now.getDate(),
  ).padStart(2, "0")}`;
  return iso.slice(0, 10) < today;
}

/** Format an ISO date string as a French short date (e.g. "17 juil. 2026"). Returns "—" when unparseable. */
export function formatDateFr(iso?: string | null): string {
  if (!iso) return "—";
  try {
    return format(parseISO(iso), "d MMM yyyy", { locale: fr });
  } catch {
    return "—";
  }
}

/**
 * Format an ISO date string as a numeric Tunisian/French date (dd/MM/yyyy, e.g. "17/07/2026").
 * Returns `fallback` ("Non renseigné") when the value is missing or unparseable.
 */
export function formatDate(iso?: string | null, fallback = "Non renseigné"): string {
  if (!iso) return fallback;
  try {
    return format(parseISO(iso), "dd/MM/yyyy", { locale: fr });
  } catch {
    try {
      return format(new Date(iso), "dd/MM/yyyy", { locale: fr });
    } catch {
      return fallback;
    }
  }
}

/**
 * Format an ISO date-time string as "dd/MM/yyyy HH:mm" (24-hour, French).
 * Returns `fallback` ("Non renseigné") when the value is missing or unparseable.
 */
export function formatDateTime(iso?: string | null, fallback = "Non renseigné"): string {
  if (!iso) return fallback;
  try {
    return format(parseISO(iso), "dd/MM/yyyy HH:mm", { locale: fr });
  } catch {
    try {
      return format(new Date(iso), "dd/MM/yyyy HH:mm", { locale: fr });
    } catch {
      return fallback;
    }
  }
}
