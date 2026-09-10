import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalRecordDto, DentalActInput } from './types';
// The ordonnance's line shape has ONE owner and it is the document registry — the fiche and the document
// editor write the same `content.medications` array, and a private copy here would be the fourth.
import type { PrescriptionLine } from '../documents';

export interface CreateDentalRecordRequest {
  interventionDate: string;
  amountPaid: number;
  /**
   * How `amountPaid` was settled — `Cash` | `Cheque` | `Card` | `Transfer`. Omit for cash.
   *
   * <p>It reaches the note d'honoraires this save raises: the payment used to be booked as cash unconditionally,
   * so a séance settled by cheque never appeared in « Chèques à encaisser ». Build the three cheque fields with
   * `chequePaymentFields()` — it clears them when the method is not a cheque, which is what makes the server's
   * refusal of details on a cash payment unreachable rather than merely unlikely.</p>
   */
  paymentMethod?: string;
  chequeNumber?: string;
  chequeBankName?: string;
  /** A bare `YYYY-MM-DD`. ⚠️ Never `toISOString()` — that would shift a cheque due on the 1st into last month. */
  chequeDueDate?: string;
  isAdultTeeth: boolean;
  notes: string[];
  importantNotes: string[];
  // Act line items (used for both create and update). procedureType/cost/teeth are derived server-side.
  acts: DentalActInput[];
  // Optional: mark a scheduled treatment-plan step "réalisé" and link it to this record.
  treatmentPlanId?: string | null;
  treatmentPlanItemId?: string | null;
  /**
   * Which **step** of that devis act this fiche carries out — « le scellement ». Null for an act done in one
   * séance, which is every fiche written before steps existed.
   *
   * ⚠️ Sending it is what lets one devis line be evidenced by several fiches: the act-level link refuses a
   * second, different record, so without a step the second séance of a bridge is refused outright. Omitting it
   * on a stepped act is still safe — the server advances that act's next pending step.
   */
  treatmentPlanItemStepId?: string | null;
  /**
   * What the patient handed over towards the **treatment** this séance carries out — the cumulative figure for
   * this séance, not an increment. Requires `treatmentPlanId`.
   *
   * ⚠️ **A second money field, never folded into `amountPaid`.** They settle two different things: « Payé »
   * covers this séance's own acts and produces a note d'honoraires, while this draws down a multi-séance act
   * priced *once* on its devis and produces an échéance payment. A devis act sits on the fiche at 0 by rule, so
   * putting its money through `amountPaid` is refused (a payment may not exceed the séance total) — and
   * overtyping that 0 to get around it raises a second, unlinked claim for work the treatment already prices.
   *
   * ⚠️ Collecting on a treatment with no devis number **issues one**. That spends a gapless number, so the UI
   * says so before saving.
   */
  amountCollectedOnPlan?: number;
  // Optional: the appointment this record documents — completes it + dismisses its post-visit prompt.
  appointmentId?: string | null;

  /**
   * What was prescribed at this séance — médicaments and/or examens. Becomes the séance's own **ordonnance**
   * (a real `MedicalDocument`, printable and e-mailable from the patient's Documents tab), created inside the
   * fiche's own save.
   *
   * ⚠️ Tri-state, and the modal always sends it. **Absent** means « unchanged » and the server does not even
   * look for a document — which is what keeps every older caller clear of one. **Present and empty** means
   * « nothing prescribed at this séance ». Neither ever deletes an ordonnance already issued: that paper may
   * be in the patient's hand, so deletion stays on the document's own role-gated, named confirmation.
   *
   * ⚠️ A field a routine re-save forgets is how this product has lost data before (`SetActs` and
   * `SetProcedures` both replace their whole list), so send it on every save rather than only when it changed.
   */
  prescription?: PrescriptionInput;
}

/** @see CreateDentalRecordRequest.prescription */
export interface PrescriptionInput {
  lines: PrescriptionLine[];
  /**
   * The `version` of the ordonnance the modal read when it opened, round-tripped so this save cannot overwrite
   * an edit made through `/documents/prescription` in the meantime. Omit (or 0) to skip the check.
   *
   * ⚠️ **The document's own `xmin` does not cover this, and assuming it did was a real defect.** The server
   * loads the document inside the fiche's transaction, so its tracked copy always carries the current token;
   * the stale copy is *here*, in the section's state. Measured: a colleague's correction to a médicament's
   * name, made in the editor while the fiche modal sat open, was silently reverted by the next fiche save.
   * A mismatch is now a 409, which `useConflict` turns into « Recharger ».
   */
  prescriptionDocumentVersion?: number;
  /** @see prescriptionDocumentVersion — the demande d'examens' own token. */
  examensDocumentVersion?: number;
}

/**
 * One act a fiche recorded, priced exactly as the Factures module would bill it.
 *
 * ⚠️ **Served, never derived here.** `DentalRecordActDto` already carries `cost`, `unitCost`, `isPerTooth` and
 * `toothNumbers`, so the per-tooth-vs-forfait rule *could* be written in this file — and that is precisely the
 * second pricing authority `DentalRecordInvoiceLines` was moved server-side to remove. The two documents would
 * then be free to disagree about what the same séance costs.
 */
export interface BillableActLine {
  /** Identifies the offered row to the picker, and nothing else. */
  key: string;
  dentalRecordId: string;
  interventionDate: string;
  /** « Composite (dents 16, 26) » — the act, never the diagnosis. */
  designation: string;
  quantity: number;
  unitPriceHt: number;
}

export const dentalRecordsApi = {
  list: async (patientId: string): Promise<DentalRecordDto[]> => {
    return apiGet<DentalRecordDto[]>(`/patients/${patientId}/dental-records`);
  },

  /** @see BillableActLine — the note d'honoraires editor's « Reprendre des actes réalisés ». */
  billableLines: async (patientId: string): Promise<BillableActLine[]> => {
    return apiGet<BillableActLine[]>(`/patients/${patientId}/dental-records/billable-lines`);
  },

  create: async (patientId: string, data: CreateDentalRecordRequest): Promise<DentalRecordDto> => {
    return apiPost<DentalRecordDto>(`/patients/${patientId}/dental-records`, data);
  },

  update: async (
    patientId: string,
    id: string,
    /**
     * `correctionReason` opts into replacing the note d'honoraires this fiche is on: it is voided, cancelled and
     * a corrected one is raised. Omitted, an edit the note disagrees with is refused as before — retiring a
     * numbered document is never something a routine re-save should do by itself.
     */
    data: CreateDentalRecordRequest & { version?: number; correctionReason?: string },
  ): Promise<DentalRecordDto> => {
    return apiPut<DentalRecordDto>(`/patients/${patientId}/dental-records/${id}`, data);
  },

  delete: async (patientId: string, id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/dental-records/${id}`);
  },
};

