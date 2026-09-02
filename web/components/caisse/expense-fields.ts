/**
 * The vocabulary a dépense is written in — the payment methods and the categories — shared by the one-off
 * dépense form on la caisse and by the monthly-dépense form beside it.
 *
 * ⚠️ **One authority on purpose.** These lived inside `app/caisse/page.tsx` while it was the only writer; a
 * second copy for « Dépenses mensuelles » would be two French labels for one enum value and two category lists,
 * and the page's own comment already names the failure — « two lookups for one word is how they drift ». A series
 * writes into the same `Expenses` table its occurrences land in, so it has to speak the same words.
 */

export type PaymentMethod = "Cash" | "Cheque" | "Card" | "Transfer"

/** French label ↔ `PaymentMethod` enum value (the API stores the enum name). */
export const PAYMENT_METHODS: { value: PaymentMethod; label: string }[] = [
  { value: "Cash", label: "Espèces" },
  { value: "Cheque", label: "Chèque" },
  { value: "Card", label: "Carte" },
  { value: "Transfer", label: "Virement" },
]

export const EXPENSE_CATEGORIES = [
  "Loyer",
  "Salaires",
  "Fournitures",
  "Laboratoire",
  "Électricité/Eau",
  "Équipement",
  "Maintenance",
  "Taxes",
  "Autre",
]

/** An unknown value is rendered as itself rather than hidden — a row's mode is never blank. */
export const methodLabel = (value: string): string =>
  PAYMENT_METHODS.find((m) => m.value === value)?.label ?? value
