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

/** Format an ISO date string as a French short date (e.g. "17 juil. 2026"). Returns "—" when unparseable. */
export function formatDateFr(iso?: string | null): string {
  if (!iso) return "—";
  try {
    return format(parseISO(iso), "d MMM yyyy", { locale: fr });
  } catch {
    return "—";
  }
}
