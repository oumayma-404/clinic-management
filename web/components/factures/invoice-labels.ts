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

// TTN « El Fatoora » e-invoicing status (backend EInvoiceStatus enum names → UI copy).
export const EINVOICE_STATUS_LABELS: Record<string, string> = {
  NotSubmitted: "Non envoyée",
  Queued: "En file d'attente",
  Signed: "Signée",
  Submitted: "Transmise",
  Validating: "En validation",
  Valid: "Validée",
  Rejected: "Rejetée",
  Failed: "Échec",
};

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

/** El Fatoora status → tone. Everything mid-flight is `pending`/`active`; only TTN's `Valid` is an outcome. */
export const EINVOICE_STATUS_TONE: Record<string, StatusTone> = {
  NotSubmitted: "neutral",
  Queued: "active",
  Signed: "pending",
  Submitted: "pending",
  Validating: "active",
  Valid: "positive",
  Rejected: "negative",
  Failed: "negative",
};

export function invoiceStatusLabel(status: string): string {
  return INVOICE_STATUS_LABELS[status] ?? status;
}

export function eInvoiceStatusLabel(status: string): string {
  return EINVOICE_STATUS_LABELS[status] ?? status;
}

export function invoiceStatusBadgeClass(status: string): string {
  return statusToneClass(INVOICE_STATUS_TONE[status]);
}

export function eInvoiceStatusBadgeClass(status: string): string {
  return statusToneClass(EINVOICE_STATUS_TONE[status]);
}

export function paymentMethodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method] ?? method;
}
