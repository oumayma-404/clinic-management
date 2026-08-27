// Tunisian +216 E.164 phone rule — the frontend mirror of the backend `PhoneNumber.ToE164`
// (reliability-and-polish AC-5). Used to reject a non-deliverable number at entry (patient edit form +
// the inline new-patient path) instead of letting it silently fail at reminder dispatch.

/**
 * Normalizes a phone to Tunisian `+216XXXXXXXX`, or `null` when it can't be parsed as a Tunisian 8-digit
 * number. Accepts common local forms: `20 123 456`, `+216 20 123 456`, `0021620123456`, `216-20-123-456`.
 */
export function toE164Tunisian(raw: string | null | undefined): string | null {
  if (!raw || !raw.trim()) return null;
  let digits = raw.replace(/\D/g, "");
  if (digits.startsWith("00216")) {
    digits = digits.slice(5);
  } else if (digits.length === 11 && digits.startsWith("216")) {
    digits = digits.slice(3);
  }
  return digits.length === 8 ? `+216${digits}` : null;
}

/** True when `raw` is a deliverable Tunisian number (see {@link toE164Tunisian}). */
export function isDeliverablePhone(raw: string | null | undefined): boolean {
  return toE164Tunisian(raw) !== null;
}

/** French inline error message shown when a phone fails {@link isDeliverablePhone}. */
export const PHONE_ERROR_FR =
  "Numéro de téléphone invalide. Utilisez un numéro tunisien à 8 chiffres (ou +216…).";
