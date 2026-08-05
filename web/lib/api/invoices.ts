import { apiGet, apiPost, apiPut, apiDelete, apiHeaders, getAccessToken } from './client';
import type { CreditNoteDto, InvoiceDto, InvoiceRevenueDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export interface InvoiceLineInput {
  designation: string;
  quantity: number;
  unitPriceHt: number;
  dentalRecordId?: string | null;
  /** Optional catalog CNAM/DCH act this line bills (drives the reimbursable split); omit for free text. */
  dentalActCodeId?: string | null;
  codeActe?: string | null;
}

export interface CreateInvoiceRequest {
  patientId: string;
  dentalRecordId?: string | null;
  appointmentId?: string | null;
  lines: InvoiceLineInput[];
}

export interface RecordPaymentRequest {
  amount: number;
  /** Cash | Cheque | Card | Transfer */
  method: string;
  paidOn: string;
  /**
   * Cheque identity (L8) — only for `method: "Cheque"`; the server **refuses** them on any other method.
   * Build them with `chequePaymentFields()` rather than by hand, which is what guarantees that.
   */
  chequeNumber?: string;
  /** @see chequeNumber */
  chequeBankName?: string;
  /**
   * A bare `YYYY-MM-DD` calendar day, **not** an ISO instant: the day a cheque may be banked is a fact about a
   * paper document, and `toISOString()` would shift it a day for the Tunisian offset.
   */
  chequeDueDate?: string;
}

/** Authenticated GET returning a Blob — the PDF/artifact routes can't go through `client.ts`. */
async function downloadInvoiceBlob(path: string, failureLabel: string): Promise<Blob> {
  const token = await getAccessToken();
  const headers = apiHeaders(token, 'none');

  const base = typeof window !== 'undefined' ? window.location.origin : undefined;
  const url = new URL(`${API_BASE_URL}${path}`, base);

  const response = await fetch(url.toString(), { method: 'GET', headers, credentials: 'include' });
  if (!response.ok) {
    const text = await response.text();
    // The API returns the { error } JSON contract — surface that message, not the raw JSON body.
    let message = text;
    try { message = JSON.parse(text)?.error ?? text; } catch { /* body is not JSON */ }
    throw new Error(message || `${failureLabel} (HTTP ${response.status})`);
  }
  return response.blob();
}

export const invoicesApi = {
  list: async (params?: {
    from?: string;
    to?: string;
    patientId?: string;
    status?: string;
  }): Promise<InvoiceDto[]> => unwrapPaged(await apiGet<PagedResponse<InvoiceDto>>('/invoices', params)),

  /**
   * One page of notes d'honoraires. `search` matches the invoice number **and the patient's name**, server-side
   * over the whole clinic — the patient half is an EXISTS against Patients, because the names shown on the rows
   * are resolved after the page is cut and so cannot be filtered here.
   */
  listPaged: async (
    params: PageParams & {
      from?: string
      to?: string
      patientId?: string
      status?: string
      /**
       * L9 — only the notes attributed to this practitioner. Applied server-side; ⚠️ an **unattributed** note is
       * excluded when it is supplied, which is what keeps two practitioners' filtered lists from overlapping the
       * clinic's total. Historical rows are unattributed, so a practice that has just upgraded will see fewer rows
       * under a filter than it expects — that is the truth, not a bug.
       */
      doctorId?: string
    },
  ): Promise<PagedResponse<InvoiceDto>> => apiGet<PagedResponse<InvoiceDto>>('/invoices', params),

  get: async (id: string): Promise<InvoiceDto> => apiGet<InvoiceDto>(`/invoices/${id}`),

  revenue: async (params?: { from?: string; to?: string }): Promise<InvoiceRevenueDto> =>
    apiGet<InvoiceRevenueDto>('/invoices/revenue', params),

  create: async (data: CreateInvoiceRequest): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>('/invoices', data),

  // Devis→facture bridge: create a draft invoice from an accepted treatment plan (seeds lines + links back).
  createFromPlan: async (planId: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/from-plan/${planId}`, {}),

  /**
   * Bill a fiche de soins: the server prices the session's acts, **issues** the note (consuming a number) and,
   * when `paidNow` is supplied, records that payment — atomically.
   *
   * ⚠️ Unlike `createFromPlan` this does NOT produce a draft. A payment can only exist on an issued invoice, so
   * a mis-keyed amount is corrected with an avoir, not an edit — confirm before calling.
   *
   * The line pricing (per-tooth acts bill as quantity × unit price) is deliberately **server-side**: it used to
   * be computed in the patient page to seed a form, which made the browser a second authority over how recorded
   * work becomes money.
   */
  createFromDentalRecord: async (
    dentalRecordId: string,
    paidNow: { amount: number; method: string; paidOn: string } | null,
  ): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/from-dental-record/${dentalRecordId}`, paidNow),

  update: async (
    id: string,
    data: CreateInvoiceRequest & { version?: number },
  ): Promise<InvoiceDto> => apiPut<InvoiceDto>(`/invoices/${id}`, data),

  issue: async (id: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/issue`, {}),

  recordPayment: async (id: string, data: RecordPaymentRequest): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/payments`, data),

  /**
   * Void a recorded payment — "this was never received". The row is kept and marked with a motif, the actor
   * and the moment; the collected total is recomputed and the invoice status walks back. Not reversible: to
   * correct a correction, record the right payment again. AdminOrDoctor only.
   *
   * A void is a correction, not a refund — money actually returned to the patient is an avoir.
   */
  voidPayment: async (id: string, paymentId: string, reason: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/payments/${paymentId}/void`, { reason }),

  cancel: async (id: string, reason: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/cancel`, { reason }),

  // Establish an avoir (credit note) against an issued invoice with collected money — the lawful correction
  // path for cash already received (a void says "never received"; an avoir says "given back").
  createAvoir: async (
    id: string,
    data: { amount: number; reason: string; method?: string; refundedOn?: string },
  ): Promise<CreditNoteDto> => apiPost<CreditNoteDto>(`/invoices/${id}/avoir`, data),

  // The avoirs established against an invoice, newest first. `invoicesApi.get` already embeds these; this
  // exists for callers that hold an invoice id but not the aggregate.
  listAvoirs: async (id: string): Promise<CreditNoteDto[]> =>
    apiGet<CreditNoteDto[]>(`/invoices/${id}/avoirs`),

  // Send (or retry sending) an issued invoice to TTN « El Fatoora ». Idempotent per invoice.
  submitToElFatoora: async (id: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/e-invoice/submit`, {}),

  delete: async (id: string): Promise<void> => apiDelete<void>(`/invoices/${id}`),

  // e-invoicing artifacts are binary — drop to raw fetch and attach the bearer token ourselves.
  downloadEInvoiceArtifact: async (id: string, artifact: 'xml' | 'receipt'): Promise<Blob> => {
    const token = await getAccessToken();
    const headers = apiHeaders(token, 'none');

    const base = typeof window !== 'undefined' ? window.location.origin : undefined;
    const url = new URL(`${API_BASE_URL}/invoices/${id}/e-invoice/${artifact}`, base);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers,
      credentials: 'include',
    });
    if (!response.ok) {
      const text = await response.text();
      // The API returns the { error } JSON contract — surface that message, not the raw JSON body.
      let message = text;
      try { message = JSON.parse(text)?.error ?? text; } catch { /* body is not JSON */ }
      throw new Error(message || `Échec du téléchargement (HTTP ${response.status})`);
    }
    return response.blob();
  },

  // The avoir's own PDF — the patient's proof of the refund. Note the route is keyed by the AVOIR's id,
  // not the invoice's.
  downloadAvoirPdf: async (creditNoteId: string): Promise<Blob> =>
    downloadInvoiceBlob(`/invoices/avoirs/${creditNoteId}/pdf`, 'Échec du téléchargement du PDF'),

  // PDF is a binary blob — drop to raw fetch and attach the bearer token ourselves.
  downloadPdf: async (id: string): Promise<Blob> => {
    const token = await getAccessToken();
    const headers = apiHeaders(token, 'none');

    const base = typeof window !== 'undefined' ? window.location.origin : undefined;
    const url = new URL(`${API_BASE_URL}/invoices/${id}/pdf`, base);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers,
      credentials: 'include',
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Échec du téléchargement du PDF (HTTP ${response.status})`);
    }
    return response.blob();
  },
};
