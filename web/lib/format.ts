import { format, parseISO } from "date-fns";
import { fr } from "date-fns/locale";

/**
 * Format a Tunisian dinar amount to millimes (3 decimals) with the "DT" suffix, French grouping
 * (e.g. 1234.5 → "1 234,500 DT"). The app targets Tunisia; money is stored in millimes (decimal(18,3)).
 */
export function formatDT(amount: number | null | undefined): string {
  return `${formatAmount(amount)} DT`;
}

/**
 * The same millime-precise fr-TN figure **without the unit** — for a table column that states « (DT) » in its
 * header once.
 *
 * <p>It exists so a money column can be pure number. « DT » repeated down fifteen rows is fifteen copies of one
 * fact, and because the suffix varies in width it also pushes the digits away from the right edge, undoing the
 * `tabular-nums` alignment that makes three money columns comparable at a glance.</p>
 *
 * <p>⚠️ Still the one rounding/grouping authority — three decimals, fr-TN grouping, decimal comma. Never
 * hand-format a dinar amount: `toFixed(2)` drops the millime and `toFixed(3)` prints a period where the rest of
 * the product prints a comma. Use this only where the unit is stated elsewhere on screen; otherwise
 * {@link formatDT}.</p>
 */
export function formatAmount(amount: number | null | undefined): string {
  return new Intl.NumberFormat("fr-TN", {
    minimumFractionDigits: 3,
    maximumFractionDigits: 3,
  }).format(amount ?? 0);
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
 * Format a byte count with French units — « o / Ko / Mo », not « B / KB / MB » (AC-P3.51). Decimal separator
 * is a comma, matching every other number the app prints.
 *
 * One shared function rather than a copy per screen: the patient page and the files manager each carried a
 * byte-identical English `formatFileSize`, which is how the two drifted from the rest of the French UI in the
 * first place.
 */
export function formatFileSize(bytes: number | null | undefined): string {
  const value = bytes ?? 0;
  if (!Number.isFinite(value) || value < 0) return "0 o";
  if (value < 1024) return `${Math.round(value)} o`;

  const unit = value < 1024 * 1024 ? "Ko" : "Mo";
  const scaled = value < 1024 * 1024 ? value / 1024 : value / (1024 * 1024);
  return `${new Intl.NumberFormat("fr-TN", { maximumFractionDigits: 1 }).format(scaled)} ${unit}`;
}

/**
 * Today's calendar date as `YYYY-MM-DD`, read from the **viewer's own clock** — the single authority for
 * pre-filling a date input (AC-P6.5).
 *
 * Never use `new Date().toISOString().slice(0, 10)` for this. `toISOString` converts to **UTC** first, so
 * between 00:00 and 01:00 in Tunis (UTC+1) it returns *yesterday* — which is how a payment taken at 00:30 was
 * booked to the previous day, and the previous month on the 1st. The defect was un-overridable in the sense that
 * matters: the value lands in the form as a plausible date nobody re-reads.
 *
 * `getFullYear`/`getMonth`/`getDate` are the local-calendar accessors, so the string is the day the user is
 * actually having. The server-side counterpart is `ClinicClock.ClinicToday`.
 */
export function todayLocalIso(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(
    now.getDate(),
  ).padStart(2, "0")}`;
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
  return iso.slice(0, 10) < todayLocalIso();
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
