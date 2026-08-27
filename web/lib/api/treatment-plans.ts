import { apiGet, apiGetBlob, apiPost, apiPut, apiDelete } from './client';
import type { TreatmentPlanDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

export interface TreatmentPlanItemInput {
  /**
   * The existing act this line stands for, echoed back when editing so the server preserves its id — without
   * which any appointment or dental-record link to that act is orphaned by the edit.
   */
  id?: string | null;
  /** Catalog act id when picked from the dental-act catalog; omitted for a free-text line. */
  dentalActCodeId?: string | null;
  /** Snapshot of the catalog code (or omitted for free text). */
  codeActe?: string | null;
  /**
   * The clinic's own procedure this act will be performed as, when picked from « Mes actes ». Persisted so
   * booking the act later preselects the procedure — giving the appointment its colour and default duration,
   * and letting the dental-record modal propose the act. Independent of `dentalActCodeId`.
   */
  procedureTypeId?: string | null;
  designationFr: string;
  plannedCost: number;
  toothNumbers: number[];
}

export interface TreatmentPlanInstallmentInput {
  /**
   * The existing échéance this line revises. A row that has collected money MUST be echoed back — dropping
   * it would erase that cash from the plan's balance, and the server refuses it.
   */
  id?: string | null;
  dueDate: string;
  amount: number;
}

/**
 * Amend an accepted devis: add acts, edit the acts already on it, remove acts, retitle it and re-spread the
 * échéancier — in one call, so the schedule can never be left out of sync with a total that just changed.
 */
export interface AmendTreatmentPlanRequest {
  addItems?: TreatmentPlanItemInput[];
  /**
   * Acts already on the plan to correct in place. Each entry's `id` is required and must name an act on this
   * plan; the act keeps that id, so every appointment and fiche link pointing at it survives the amendment.
   * This is what makes "fix a wrong price" possible on an act that is done or booked — remove-then-add is
   * refused for exactly those acts.
   */
  updateItems?: TreatmentPlanItemInput[];
  removeItemIds?: string[];
  /** Omitted or blank leaves the title untouched. */
  title?: string;
  /**
   * Tri-state server-side: omit the key to leave the notes alone, send `null` to clear them. The form always
   * sends the field, and the server compares it against the stored value, so re-submitting unchanged notes
   * does not count as an amendment.
   */
  notes?: string | null;
  /** Required whenever the amendment changes the total; the server rejects a mismatch. */
  installments?: TreatmentPlanInstallmentInput[];
  /** The `version` the client read, so a concurrent edit 409s instead of silently overwriting a fee. */
  version?: number;
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
  /**
   * Cheque identity (L8) — only for `method: "Cheque"`. Same contract as the invoice side's
   * `RecordPaymentRequest`; build with `chequePaymentFields()`.
   */
  chequeNumber?: string;
  /** @see chequeNumber */
  chequeBankName?: string;
  /** A bare `YYYY-MM-DD` calendar day — see the invoice-side note on why not an ISO instant. */
  chequeDueDate?: string;
}

export const treatmentPlansApi = {
  /**
   * `from`/`to` bound the CREATION date; `acceptedFrom`/`acceptedTo` bound `acceptedDate`. Both exist because the
   * dashboard's « Devis acceptés » counts by acceptance, so drilling into it with the created-date range would list a
   * different set of devis than the card counted.
   */
  list: async (params?: {
    patientId?: string;
    status?: string;
    from?: string;
    to?: string;
    acceptedFrom?: string;
    acceptedTo?: string;
  }): Promise<TreatmentPlanDto[]> =>
    unwrapPaged(await apiGet<PagedResponse<TreatmentPlanDto>>('/treatment-plans', params)),

  /**
   * One page of devis. `search` matches the devis number, title, notes **and the patient's name**, server-side
   * over the whole clinic (same EXISTS-against-Patients reasoning as the invoice list).
   */
  listPaged: async (
    params: PageParams & {
      patientId?: string;
      status?: string;
      from?: string;
      to?: string;
      acceptedFrom?: string;
      acceptedTo?: string;
    },
  ): Promise<PagedResponse<TreatmentPlanDto>> =>
    apiGet<PagedResponse<TreatmentPlanDto>>('/treatment-plans', params),

  get: async (id: string): Promise<TreatmentPlanDto> => apiGet<TreatmentPlanDto>(`/treatment-plans/${id}`),

  create: async (data: CreateTreatmentPlanRequest): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>('/treatment-plans', data),

  update: async (
    id: string,
    data: UpdateTreatmentPlanRequest & { version?: number },
  ): Promise<TreatmentPlanDto> =>
    apiPut<TreatmentPlanDto>(`/treatment-plans/${id}`, data),

  accept: async (id: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/accept`, {}),

  complete: async (id: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/complete`, {}),

  recordInstallmentPayment: async (
    id: string,
    installmentId: string,
    data: RecordInstallmentPaymentRequest,
  ): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/installments/${installmentId}/payments`, data),

  /**
   * Return a « réalisé » act to « prévu » and detach its fiche de soins, reopening the devis if that act had
   * closed it. Takes no body — the act to correct is fully identified by the route. Server-side: AdminOrDoctor,
   * and refused once a live invoice bills the plan or the act's own fiche.
   *
   * There is deliberately **no** `markItemDone` counterpart here: an act is marked réalisé by saving the fiche
   * de soins that evidences it (`dentalRecordsApi`), never by a manual toggle, so a client function for
   * `POST .../done` would be a second, unevidenced way into the same state. The uncalled one was deleted
   * rather than wired (AC-P2.11).
   */
  markItemUndone: async (id: string, itemId: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/items/${itemId}/undone`, {}),

  /** Add/edit/remove acts on an accepted devis (+ title, notes and the matching échéancier). AdminOrDoctor. */
  amend: async (id: string, data: AmendTreatmentPlanRequest): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/amend`, data),

  /** Re-spread the échéancier without touching the acts. Server-side: AdminOrDoctor. */
  reviseInstallments: async (
    id: string,
    installments: TreatmentPlanInstallmentInput[],
  ): Promise<TreatmentPlanDto> =>
    apiPut<TreatmentPlanDto>(`/treatment-plans/${id}/installments`, { installments }),

  /** Reorder the acts. Cosmetic — no role policy, no revision bump. Send every act id, once. */
  reorderItems: async (id: string, itemIds: string[]): Promise<TreatmentPlanDto> =>
    apiPut<TreatmentPlanDto>(`/treatment-plans/${id}/items/order`, { itemIds }),

  cancel: async (id: string, reason: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/cancel`, { reason }),

  remove: async (id: string): Promise<void> => apiDelete<void>(`/treatment-plans/${id}`),

  downloadDevisPdf: async (id: string): Promise<Blob> =>
    apiGetBlob(`/treatment-plans/${id}/devis-pdf`),

  /**
   * Void a payment recorded against an échéance — "this was never received". The ledger row is kept and
   * marked; the installment's totals are re-derived. The plan's status is NOT walked back, because it tracks
   * clinical progress rather than payment. AdminOrDoctor only.
   */
  voidInstallmentPayment: async (
    id: string,
    installmentId: string,
    paymentId: string,
    reason: string,
  ): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(
      `/treatment-plans/${id}/installments/${installmentId}/payments/${paymentId}/void`,
      { reason },
    ),

  /**
   * The échéancier's half of « marquer un chèque encaissé » — see `invoicesApi.setPaymentBanked`. Three ids
   * because an échéance payment is only addressable as {plan, installment, payment}.
   */
  setInstallmentPaymentBanked: async (
    id: string,
    installmentId: string,
    paymentId: string,
    banked: boolean,
  ): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(
      `/treatment-plans/${id}/installments/${installmentId}/payments/${paymentId}/banked`,
      { banked },
    ),

  /**
   * Download the receipt for ONE installment payment. The payment id is required: an échéance can hold
   * several payments, and the receipt used to print the cumulative total rather than the money handed over.
   * A voided payment still renders, over-stamped « REÇU ANNULÉ ».
   */
  downloadInstallmentReceipt: async (id: string, installmentId: string, paymentId: string): Promise<Blob> =>
    apiGetBlob(`/treatment-plans/${id}/installments/${installmentId}/payments/${paymentId}/receipt-pdf`),
};
