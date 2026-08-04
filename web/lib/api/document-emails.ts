import { apiGet, apiPost } from './client';

/**
 * The document kinds that can be emailed. Mirrors the closed set declared on the backend `DocumentEmail`
 * entity — English storage keys, French at display time, the standing convention for a persisted value set.
 */
export const DOCUMENT_EMAIL_KINDS = {
  MedicalDocument: 'medical-document',
  Invoice: 'invoice',
  CreditNote: 'credit-note',
  TreatmentPlan: 'treatment-plan',
  InvoicePaymentReceipt: 'invoice-payment-receipt',
  InstallmentPaymentReceipt: 'installment-payment-receipt',
} as const;

export type DocumentEmailKind = (typeof DOCUMENT_EMAIL_KINDS)[keyof typeof DOCUMENT_EMAIL_KINDS];

/** Lifecycle of one send (mirrors the backend enum name). */
export type DocumentEmailStatus = 'Queued' | 'Sent' | 'Failed';

/**
 * One recorded send. Carries no attachment and no storage key — what a practitioner needs is who it went to,
 * when, and whether it left.
 */
export interface DocumentEmailDto {
  id: string;
  documentKind: DocumentEmailKind;
  documentId: string;
  recipientEmail: string;
  subject: string;
  status: DocumentEmailStatus;
  attempts: number;
  queuedAt: string;
  sentAt: string | null;
  failureReason: string | null;
  attachmentFileName: string;
}

/**
 * What to send. The PDF is **never** uploaded from here: the server re-renders it from `documentId` through
 * that document's own PDF query, so an emailed document cannot differ from the downloaded one.
 *
 * `installmentId` / `paymentId` are only meaningful for the two receipt kinds — a receipt is identified by its
 * payment, not by its parent invoice or plan.
 */
export interface QueueDocumentEmailRequest {
  documentKind: DocumentEmailKind;
  documentId: string;
  installmentId?: string;
  paymentId?: string;
  recipientEmail: string;
  subject: string;
  body: string;
}

/** French labels for a send's status. */
export const DOCUMENT_EMAIL_STATUS_LABELS_FR: Record<DocumentEmailStatus, string> = {
  Queued: "En attente d'envoi",
  Sent: 'Envoyé',
  Failed: 'Échec',
};

export const documentEmailsApi = {
  /**
   * Queues a document for delivery. Throws `ApiError` carrying the server's French message — the refusals are
   * meaningful (« l'envoi par email n'est pas configuré… », « Émettez la facture avant de générer le PDF. »)
   * and must reach the practitioner rather than becoming a generic failure.
   */
  queue: (data: QueueDocumentEmailRequest): Promise<DocumentEmailDto> =>
    apiPost<DocumentEmailDto>('/document-emails', data),

  /** The send history of one document, newest first. */
  listForDocument: (documentKind: DocumentEmailKind, documentId: string): Promise<DocumentEmailDto[]> =>
    apiGet<DocumentEmailDto[]>('/document-emails', { documentKind, documentId }),
};
