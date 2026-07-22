import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { TreatmentPlanDto } from './types';

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

export interface TreatmentPlanItemInput {
  /** Catalog act id when picked from the dental-act catalog; omitted for a free-text line. */
  dentalActCodeId?: string | null;
  /** Snapshot of the catalog code (or omitted for free text). */
  codeActe?: string | null;
  designationFr: string;
  plannedCost: number;
  toothNumbers: number[];
}

export interface TreatmentPlanInstallmentInput {
  dueDate: string;
  amount: number;
}

export interface CreateTreatmentPlanRequest {
  patientId: string;
  title: string;
  notes?: string | null;
  items: TreatmentPlanItemInput[];
  installments: TreatmentPlanInstallmentInput[];
}

export interface UpdateTreatmentPlanRequest {
  title: string;
  notes?: string | null;
  items: TreatmentPlanItemInput[];
  installments: TreatmentPlanInstallmentInput[];
}

export interface RecordInstallmentPaymentRequest {
  amount: number;
  /** Cash | Cheque | Card | Transfer */
  method: string;
  paidOn: string;
}

export interface MarkItemDoneRequest {
  doneOn?: string | null;
  linkedDentalRecordId?: string | null;
}

export const treatmentPlansApi = {
  list: async (params?: {
    patientId?: string;
    status?: string;
    from?: string;
    to?: string;
  }): Promise<TreatmentPlanDto[]> => apiGet<TreatmentPlanDto[]>('/treatment-plans', params),

  get: async (id: string): Promise<TreatmentPlanDto> => apiGet<TreatmentPlanDto>(`/treatment-plans/${id}`),

  create: async (data: CreateTreatmentPlanRequest): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>('/treatment-plans', data),

  update: async (id: string, data: UpdateTreatmentPlanRequest): Promise<TreatmentPlanDto> =>
    apiPut<TreatmentPlanDto>(`/treatment-plans/${id}`, data),

  accept: async (id: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/accept`, {}),

  recordInstallmentPayment: async (
    id: string,
    installmentId: string,
    data: RecordInstallmentPaymentRequest,
  ): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/installments/${installmentId}/payments`, data),

  markItemDone: async (id: string, itemId: string, data: MarkItemDoneRequest): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/items/${itemId}/done`, data),

  cancel: async (id: string, reason: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/cancel`, { reason }),

  remove: async (id: string): Promise<void> => apiDelete<void>(`/treatment-plans/${id}`),

  // The devis PDF is a binary blob — drop to raw fetch and attach the bearer token ourselves.
  downloadDevisPdf: async (id: string): Promise<Blob> => {
    const token = await getAccessToken();
    const headers: HeadersInit = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const base = typeof window !== 'undefined' ? window.location.origin : undefined;
    const url = new URL(`${API_BASE_URL}/treatment-plans/${id}/devis-pdf`, base);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers,
      credentials: 'include',
    });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || `Échec du téléchargement du devis (HTTP ${response.status})`);
    }
    return response.blob();
  },

  // The installment receipt PDF is a binary blob — drop to raw fetch and attach the bearer token ourselves.
  downloadInstallmentReceipt: async (id: string, installmentId: string): Promise<Blob> => {
    const token = await getAccessToken();
    const headers: HeadersInit = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const base = typeof window !== 'undefined' ? window.location.origin : undefined;
    const url = new URL(`${API_BASE_URL}/treatment-plans/${id}/installments/${installmentId}/receipt-pdf`, base);

    const response = await fetch(url.toString(), {
      method: 'GET',
      headers,
      credentials: 'include',
    });
    if (!response.ok) {
      const text = await response.text();
      let message = text;
      try { message = JSON.parse(text)?.error ?? text; } catch { /* body is not JSON */ }
      throw new Error(message || `Échec du téléchargement du reçu (HTTP ${response.status})`);
    }
    return response.blob();
  },
};
