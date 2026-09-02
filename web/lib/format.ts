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
 * A dinar amount **as the user typed it**, as a number — the single authority for reading a money field.
 *
 * <p>Every money input in this app is `type="text" inputMode="decimal"`, not `type="number"`, and that pairing
 * is the fix rather than a preference. This product prints « 120,500 » everywhere; a `type="number"` input
 * **refuses a comma**, and when the browser rejects a keystroke `e.target.value` comes back **empty** — so the
 * dentist typed an amount, saw a filled field, and the submit sent nothing. `step="0.01"` compounded it by
 * making the millime unreachable on the very field that seeds every invoice line. `inputMode="decimal"` still
 * raises the numeric keypad on a phone, so nothing is lost.</p>
 *
 * <p>Accepted: a comma or a dot as the decimal mark, and any whitespace as grouping — including the
 * **non-breaking** and narrow-no-break spaces `Intl.NumberFormat("fr-TN")` itself emits, which is what a user
 * pastes back after copying a figure out of this app (« 1 200,500 » → `1200.5`).</p>
 *
 * <p>⚠️ Returns **`NaN`** for anything malformed rather than a plausible-looking number: a bare « , », a double
 * separator (« 1,2,3 »), a dot-grouped « 1.200,500 ». Truncating those to `1.2` would be worse than refusing
 * them — it is a wrong amount that looks deliberate. Callers must keep their own `> 0` validation, which
 * `NaN` fails; this function never throws.</p>
 */
export function parseAmountInput(value: string): number {
  const normalized = value.replace(/\s/g, "").replace(/,/g, ".");
  // At least one digit, at most one decimal point. Anything else is a typo, not an amount.
  if (!/^-?(\d+(\.\d*)?|\.\d+)$/.test(normalized)) return Number.NaN;
  return Number.parseFloat(normalized);
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
 * Wrap a value in French guillemets, bound to it by a narrow no-break space (`U+202F`) on each side.
 *
 * <p>⚠️ **Never write `« ${value} »` with ordinary spaces.** An ordinary space is a break opportunity, so the
 * closing guillemet is free to wrap onto a line of its own — which is exactly what `/fichiers` did at 320 px:
 * « Aucun résultat pour « zzzznope » rendered with a final line containing nothing but `»`. The quoted value's
 * width is unknown at authoring time (it is a search term, a file name, a patient's name), so unlike static
 * prose this cannot be eye-checked once and left alone.</p>
 *
 * <p>`U+202F` rather than `U+00A0`: both are unbreakable, and the narrow one is the typographically correct
 * space inside guillemets in French.</p>
 */
export function quoteFr(value: string): string {
  return `«\u202F${value}\u202F»`;
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
  return toLocalIso(new Date())
}

/**
 * The same `YYYY-MM-DD`, for a date that is **not** today — the helper {@link todayLocalIso} now delegates to.
 *
 * It exists for the other half of the same defect. `todayLocalIso` fixed the *pre-fill*; this fixes the
 * *round-trip*. Reopening a stored record ran `new Date(record.interventionDate).toISOString().split("T")[0]`,
 * which converts to **UTC** first — so a fiche whose stored instant lands past midnight UTC reopens showing the
 * *previous* calendar day, and re-saving then writes that wrong day back. A date input round-tripped through UTC
 * is not the date the user chose.
 *
 * Local-calendar accessors for the same reason as its neighbour: the string must be the day the viewer is
 * actually having. Returns `""` for an unparseable date rather than `"NaN-NaN-NaN"`, so a bad value leaves the
 * input visibly unset instead of filling it with a plausible-looking string.
 */
export function toLocalIso(date: Date): string {
  if (Number.isNaN(date.getTime())) return ""
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(
    date.getDate(),
  ).padStart(2, "0")}`
}

/**
 * An ISO **instant** → the `YYYY-MM-DD` day it falls on, in the viewer's own calendar.
 *
 * ⚠️ **Never `iso.slice(0, 10)`, and this is the third face of the same defect** — the one that bites when the
 * value came off the wire rather than out of a `Date`. Money instants are stored as the start of a *Tunisian*
 * day, so the 1st of September is serialised `2026-08-31T23:00:00Z`: slicing the string yields **the 31st of
 * August**. It cost a « Corriger la date » that pre-filled the day before the payment, and a « Modifier la
 * dépense » on l'extrait that looked the row up on the wrong day and reported it as deleted.
 *
 * Delegates to {@link toLocalIso}, so an unparseable value returns `""` rather than a plausible wrong day.
 */
export function localDayIso(iso?: string | null): string {
  if (!iso) return "";
  return toLocalIso(new Date(iso));
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
 * Format a **calendar day** the server sent — a date that means the same in every timezone because it names a day
 * rather than an instant (a subscription's inclusive end, the stretch a payment covered).
 *
 * ⚠️ **Never `formatDate` for one of these.** Such a value reaches the browser as UTC midnight, and `formatDate`
 * parses it into a `Date` and renders it in the *workstation's* zone — so anywhere west of UTC it prints the day
 * before, disagreeing with the server's own French sentence about the same date. This reads the date part of the
 * ISO string and never builds a `Date` at all: the same defect class `todayLocalIso()` exists to prevent, and the
 * reason `ChequeDueDate` travels as a bare `YYYY-MM-DD`.
 */
export function formatCalendarDay(iso?: string | null, fallback = "Non renseigné"): string {
  if (!iso) return fallback;
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso);
  if (!match) return formatDate(iso, fallback);
  const [, year, month, day] = match;
  return `${day}/${month}/${year}`;
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
