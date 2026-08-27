// French labels for invoice status + payment method (backend enum names → UI copy).
import { statusToneClass, type StatusTone } from "@/components/ui/status-tone";

export const INVOICE_STATUS_LABELS: Record<string, string> = {
  Draft: "Brouillon",
  Issued: "Émise",
  PartiallyPaid: "Partiellement payée",
  Paid: "Payée",
  Cancelled: "Annulée",
};

export const PAYMENT_METHOD_LABELS: Record<string, string> = {
  Cash: "Espèces",
  Cheque: "Chèque",
  Card: "Carte",
  Transfer: "Virement",
};

export const PAYMENT_METHODS = ["Cash", "Cheque", "Card", "Transfer"] as const;

/**
 * Fiscal status → tone. `PartiallyPaid` is `active` rather than `positive`: money is still owed, and a green pill on
 * a half-paid note is the kind of reassurance that gets a débiteur forgotten.
 */
export const INVOICE_STATUS_TONE: Record<string, StatusTone> = {
  Draft: "neutral",
  Issued: "pending",
  PartiallyPaid: "active",
  Paid: "positive",
  Cancelled: "negative",
};

export function invoiceStatusLabel(status: string): string {
  return INVOICE_STATUS_LABELS[status] ?? status;
}

export function invoiceStatusBadgeClass(status: string): string {
  return statusToneClass(INVOICE_STATUS_TONE[status]);
}

/**
 * The « Annulé » badge on a voided payment — `negative`, like a cancelled note: it is money that was taken back.
 *
 * <p>It lives here rather than inline in the detail modal for the same reason the status map does: the modal used
 * to render it as a bare `<Badge variant="outline">` while everything else fiscal went through a tone, so the one
 * badge that reports money leaving was the only one drawn in neutral grey.</p>
 */
export const VOIDED_PAYMENT_BADGE_CLASS = statusToneClass("negative");

export function paymentMethodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method] ?? method;
}
