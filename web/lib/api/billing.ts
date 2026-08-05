import { apiGet, apiHeaders, getAccessToken } from './client';
import type { ChequesDueDto, CnamCeilingDto, PatientBillingSummaryDto, ReceivableDto, ReceivablesPageDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export const billingApi = {
  /** The unified per-patient balance + CNAM split. */
  getPatientSummary: async (patientId: string): Promise<PatientBillingSummaryDto> =>
    apiGet<PatientBillingSummaryDto>(`/patients/${patientId}/billing-summary`),

  /**
   * « Plafond annuel CNAM » for one patient (L10) — ceiling, consumed, remaining.
   *
   * ⚠️ Per-patient, so it is **not** gated like the clinic-wide money reads: reception is asked
   * « combien reste-t-il ? » with the patient standing there. Every figure is an estimate — see `CnamCeilingDto`.
   *
   * @param year Omit for the current **clinic** year.
   */
  getPatientCnamCeiling: async (patientId: string, year?: number): Promise<CnamCeilingDto> =>
    apiGet<CnamCeilingDto>(`/patients/${patientId}/cnam-ceiling`, year ? { year } : undefined),

  /** The clinic-wide receivables list (patients with a positive balance, sorted by amount owed). */
  getReceivables: async (): Promise<ReceivableDto[]> =>
    (await apiGet<ReceivablesPageDto>('/billing/receivables')).items,

  /**
   * One page of créances, plus the clinic-wide `totalOutstanding` the header shows. `search` matches the patient's
   * name server-side over the whole list.
   */
  getReceivablesPaged: async (params: PageParams): Promise<ReceivablesPageDto> =>
    apiGet<ReceivablesPageDto>('/billing/receivables', params),

  /**
   * « Chèques à encaisser » (L8 slice B) — every cheque held, across both payment ledgers, soonest-due first,
   * with per-bucket counts and totals over the whole matching set.
   *
   * @param params.dueFrom Inclusive lower bound on the cheque's **due date** — not on when it was received.
   * @param params.dueTo Inclusive upper bound on the due date. ⚠️ A cheque with **no** due date is returned
   *   whatever the bounds say: it satisfies no date filter and it is the row most likely to be forgotten.
   */
  getChequesDue: async (
    params: PageParams & { dueFrom?: string; dueTo?: string } = {},
  ): Promise<ChequesDueDto> => apiGet<ChequesDueDto>('/billing/cheques', params),

  /** The receipt (reçu) PDF for a single invoice payment — a binary blob, so drop to raw fetch. */
  downloadPaymentReceipt: async (paymentId: string): Promise<Blob> => {
    const token = await getAccessToken();
    const headers = apiHeaders(token, 'none');

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
