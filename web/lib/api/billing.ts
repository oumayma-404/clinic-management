import { apiGet } from './client';
import type { PatientBillingSummaryDto, ReceivableDto } from './types';

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

export const billingApi = {
  /** The unified per-patient balance + CNAM split. */
  getPatientSummary: async (patientId: string): Promise<PatientBillingSummaryDto> =>
    apiGet<PatientBillingSummaryDto>(`/patients/${patientId}/billing-summary`),

  /** The clinic-wide receivables list (patients with a positive balance, sorted by amount owed). */
  getReceivables: async (): Promise<ReceivableDto[]> => apiGet<ReceivableDto[]>('/billing/receivables'),

  /** The receipt (reçu) PDF for a single invoice payment — a binary blob, so drop to raw fetch. */
  downloadPaymentReceipt: async (paymentId: string): Promise<Blob> => {
    const token = await getAccessToken();
    const headers: HeadersInit = {};
    if (token) headers['Authorization'] = `Bearer ${token}`;

    const base = typeof window !== 'undefined' ? window.location.origin : undefined;
    const url = new URL(`${API_BASE_URL}/payments/${paymentId}/receipt-pdf`, base);

    const response = await fetch(url.toString(), { method: 'GET', headers, credentials: 'include' });
    if (!response.ok) {
      const text = await response.text();
      let message = text;
      try { message = JSON.parse(text)?.error ?? text; } catch { /* body is not JSON */ }
      throw new Error(message || `Échec du téléchargement du reçu (HTTP ${response.status})`);
    }
    return response.blob();
  },
};
