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

export function invoiceStatusLabel(status: string): string {
  return INVOICE_STATUS_LABELS[status] ?? status;
}

export function paymentMethodLabel(method: string): string {
  return PAYMENT_METHOD_LABELS[method] ?? method;
}
