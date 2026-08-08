import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalRecordDto, DentalActInput } from './types';

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
  // Optional: the appointment this record documents — completes it + dismisses its post-visit prompt.
  appointmentId?: string | null;
}

export const dentalRecordsApi = {
  list: async (patientId: string): Promise<DentalRecordDto[]> => {
    return apiGet<DentalRecordDto[]>(`/patients/${patientId}/dental-records`);
  },

  create: async (patientId: string, data: CreateDentalRecordRequest): Promise<DentalRecordDto> => {
    return apiPost<DentalRecordDto>(`/patients/${patientId}/dental-records`, data);
  },

  update: async (
    patientId: string,
    id: string,
    data: CreateDentalRecordRequest & { version?: number },
  ): Promise<DentalRecordDto> => {
    return apiPut<DentalRecordDto>(`/patients/${patientId}/dental-records/${id}`, data);
  },

  delete: async (patientId: string, id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/dental-records/${id}`);
  },
};

