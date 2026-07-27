import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { CreditNoteDto, InvoiceDto, InvoiceRevenueDto } from './types';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

async function getAccessToken(): Promise<string | null> {
  try {
    const response = await fetch('/bff/auth/token', { credentials: 'include' });
    if (response.ok) {
      const data = await response.json();
      return data.accessToken || null;
    }
  } catch {
    // Token endpoint unavailable
  }
  return null;
}

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
}

/** Authenticated GET returning a Blob — the PDF/artifact routes can't go through `client.ts`. */
async function downloadInvoiceBlob(path: string, failureLabel: string): Promise<Blob> {
  const token = await getAccessToken();
  const headers: HeadersInit = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;

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
  }): Promise<InvoiceDto[]> => apiGet<InvoiceDto[]>('/invoices', params),

  get: async (id: string): Promise<InvoiceDto> => apiGet<InvoiceDto>(`/invoices/${id}`),

  revenue: async (params?: { from?: string; to?: string }): Promise<InvoiceRevenueDto> =>
    apiGet<InvoiceRevenueDto>('/invoices/revenue', params),

  create: async (data: CreateInvoiceRequest): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>('/invoices', data),

  // Devis→facture bridge: create a draft invoice from an accepted treatment plan (seeds lines + links back).
  createFromPlan: async (planId: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/from-plan/${planId}`, {}),

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
    const headers: HeadersInit = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;

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
    const headers: HeadersInit = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;

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
