import { apiGet, apiGetBlob, apiPost, apiPut, apiDelete } from './client';
import type { TreatmentPlanDto, TreatmentInProgressDto, ContinuableActDto } from './types';
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
  /**
   * The séances this act will be carried out over, **as the dentist confirmed them** before accepting the
   * devis — the procedure's protocol with whatever they unticked or edited.
   *
   * ⚠️ **Tri-state, and all three states are real.** `undefined` means « nothing was decided » and the server
   * applies the procedure's catalogue protocol; `[]` is an explicit « cet acte se fait en une séance » and
   * refuses that protocol; a non-empty list is the confirmed sequence. Sending `undefined` where the dentist
   * unticked every step would silently re-propose the protocol they had just declined.
   */
  steps?: TreatmentPlanItemStepInput[];
}

/**
 * One step as `setItemSteps` takes it.
 *
 * Echo `id` back to keep an existing step's identity (its réalisé date, its fiche link, any séance booked for
 * it); omit it for a new step. The order of the array **is** the clinical order.
 */
export interface TreatmentPlanItemStepInput {
  id?: string | null;
  label: string;
  /** Chair time for the step, or null when nobody has estimated it. */
  estimatedDurationMinutes?: number | null;
  /**
   * Calendar days to wait after the previous séance, or null when the interval is clinically free. A different
   * quantity from the chair time above: that one sizes the appointment, this one decides when it is due.
   */
  minDaysAfterPrevious?: number | null;
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

  /**
   * Detach one **step** of an act from the fiche that evidenced it. The step-level twin of `markItemUndone`,
   * with the same absence beside it: there is deliberately **no** `markStepDone`, because a step becomes
   * réalisée by saving the fiche de soins, never by a toggle.
   */
  markStepUndone: async (id: string, itemId: string, stepId: string): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/items/${itemId}/steps/${stepId}/undone`, {}),

  /**
   * Set the clinical steps of one act — « Préparation, Empreinte, Scellement définitif ».
   *
   * **Replace semantics**, and that is why it is its own call rather than a field on `amend`: an empty list
   * means « cet acte se fait en une séance », not « unchanged », and replace-semantics inside a
   * null-means-unchanged patch is how a list gets silently wiped by a partial body.
   *
   * Echo an existing step's `id` back and it keeps its identity — its réalisé date, the fiche that evidences
   * it and any séance already booked for it all survive the edit. Omit the id for a new step.
   *
   * ⚠️ Moves **no money** (the act's price, the devis total and the échéancier are untouched) and bumps no
   * revision, which is why the server allows it on a devis that is already facturé — a dentist has to be able
   * to correct the protocol of a bridge he is halfway through.
   */
  setItemSteps: async (
    id: string,
    itemId: string,
    steps: TreatmentPlanItemStepInput[],
    version?: number,
  ): Promise<TreatmentPlanDto> =>
    apiPut<TreatmentPlanDto>(`/treatment-plans/${id}/items/${itemId}/steps`, { steps, version }),

  /**
   * « Traitements en cours » — the acts this cabinet has started and not finished, with the next step and
   * whether a séance is booked for it.
   *
   * Ask for page 1 of size 1 and read `totalCount` to render the journée's chip: the total is exact whatever
   * page was requested, so the chip and the list it opens cannot disagree.
   */
  treatmentsInProgress: async (
    // `search` matches the patient's name (either order) and the devis number — server-side, whole clinic, the
    // same field the devis list searches. Filtering the returned page instead would search 25 acts and report
    // « aucun résultat » for a patient whose treatment sits on page 2.
    params?: PageParams & { search?: string },
  ): Promise<PagedResponse<TreatmentInProgressDto>> =>
    apiGet<PagedResponse<TreatmentInProgressDto>>('/treatment-plans/treatments-in-progress', params),

  /**
   * The patient's recent fiches whose acts could turn out to need another séance.
   *
   * ⚠️ Candidates, not a diagnosis: a fiche records what was *done* and never what remains, so the dentist
   * picks. Each row carries the note d'honoraires already billing it, if there is one — which is what decides
   * whether the devis this creates owns the money or is merely attached to the note that does.
   */
  continuableActs: async (patientId: string): Promise<ContinuableActDto[]> =>
    apiGet<ContinuableActDto[]>('/treatment-plans/continuable-acts', { patientId }),

  /**
   * Turn an act already carried out into a multi-séance treatment. AdminOrDoctor — it consumes a devis number
   * and may attach an issued note to the plan.
   */
  continueRecordedAct: async (data: {
    dentalRecordId: string
    actId: string
    nextStepLabel?: string
    /**
     * What the work still to come is worth, as its own act on the devis. Omit for « rien de plus à facturer ».
     *
     * ⚠️ Without it the devis carries the *original* act's fee and nothing else, so every retroactive
     * continuation under-prices by the value of the work that remains — live data shows « Extraction simple,
     * 120 DT » whose next séance is « Pose de la prothèse ».
     */
    remainingWorkCost?: number
    remainingWorkLabel?: string
  }): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>('/treatment-plans/continue-recorded-act', data),

  /**
   * « Arrêter le traitement » — the patient is not continuing. Parks the acts with no delivered work, keeps the
   * rest, re-spreads the échéancier onto the kept total and closes the devis. AdminOrDoctor.
   *
   * ⚠️ **One call, and it replaces two.** This used to be `amend` (to remove the acts) followed by `complete`,
   * with the client deciding which acts to drop — and that shape destroyed delivered work three ways: the
   * filter asked for an act's *next-step* état so a bridge two séances in was offered for deletion; the acts
   * were deleted rather than parked, taking their step rows and the links to the fiches that evidenced them;
   * and the clôture threw *after* the removals had committed, leaving the plan half-stopped with no way back.
   */
  stopTreatment: async (id: string, version?: number): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/stop`, { version }),

  /**
   * « Reprendre le traitement » — the patient came back. Reopens a stopped devis and restores every parked act
   * at the état its own steps derive. AdminOrDoctor.
   */
  reopenTreatment: async (id: string, version?: number): Promise<TreatmentPlanDto> =>
    apiPost<TreatmentPlanDto>(`/treatment-plans/${id}/reopen`, { version }),

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
