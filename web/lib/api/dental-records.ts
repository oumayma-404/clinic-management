import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalRecordDto, DentalActInput } from './types';

export interface CreateDentalRecordRequest {
  interventionDate: string;
  amountPaid: number;
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

