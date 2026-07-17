// French labels for invoice status + payment method (backend enum names → UI copy).

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

// Tailwind badge classes per e-invoicing status (light + dark), mirroring the fiscal-status palette.
export const EINVOICE_STATUS_BADGE_CLASS: Record<string, string> = {
  NotSubmitted: "bg-muted text-muted-foreground",
  Queued: "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
  Signed: "bg-sky-100 text-sky-800 dark:bg-sky-950 dark:text-sky-200",
  Submitted: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  Validating: "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
  Valid: "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
  Rejected: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
  Failed: "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
};

export function invoiceStatusLabel(status: string): string {
  return INVOICE_STATUS_LABELS[status] ?? status;
}

export function eInvoiceStatusLabel(status: string): string {
  return EINVOICE_STATUS_LABELS[status] ?? status;
}

export function eInvoiceStatusBadgeClass(status: string): string {
  return EINVOICE_STATUS_BADGE_CLASS[status] ?? "bg-muted text-muted-foreground";
}

export function paymentMethodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method] ?? method;
}
