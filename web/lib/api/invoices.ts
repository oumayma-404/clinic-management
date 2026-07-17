import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { InvoiceDto, InvoiceRevenueDto } from './types';

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

  update: async (
    id: string,
    data: CreateInvoiceRequest,
  ): Promise<InvoiceDto> => apiPut<InvoiceDto>(`/invoices/${id}`, data),

  issue: async (id: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/issue`, {}),

  recordPayment: async (id: string, data: RecordPaymentRequest): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/payments`, data),

  cancel: async (id: string, reason: string): Promise<InvoiceDto> =>
    apiPost<InvoiceDto>(`/invoices/${id}/cancel`, { reason }),

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
      throw new Error(text || `Échec du téléchargement (HTTP ${response.status})`);
    }
    return response.blob();
  },

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
