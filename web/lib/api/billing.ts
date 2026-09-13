import { apiGet, apiGetBlob } from './client';
import type { ChequesDueDto, PatientBillingSummaryDto, ReceivableDto, ReceivablesPageDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export const billingApi = {
  /**
   * The unified per-patient balance.
   *
   * ⚠️ The response still carries the server's reimbursable/out-of-pocket split, and nothing here reads it —
   * the annual-ceiling read that used to sit beside this one (`GET /patients/{id}/cnam-ceiling`) went with the
   * CNAM interface. See `features/cnam-ui-withdrawal/notes.md`; the route is still served.
   */
  getPatientSummary: async (patientId: string): Promise<PatientBillingSummaryDto> =>
    apiGet<PatientBillingSummaryDto>(`/patients/${patientId}/billing-summary`),

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
  /**
   * « Chèques à encaisser ». `banked` omitted (or false) lists what the clinic still holds; true lists the ones
   * already taken to the bank. The bucket figures describe the outstanding set either way.
   */
  getChequesDue: async (
    params: PageParams & { dueFrom?: string; dueTo?: string; banked?: boolean } = {},
  ): Promise<ChequesDueDto> => apiGet<ChequesDueDto>('/billing/cheques', params),

  /** The receipt (reçu) PDF for a single invoice payment. */
  downloadPaymentReceipt: async (paymentId: string): Promise<Blob> =>
    apiGetBlob(`/payments/${paymentId}/receipt-pdf`),
};
