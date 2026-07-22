import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalRecordDto, ToothConditionInput } from './types';

export interface CreateDentalRecordRequest {
  interventionDate: string;
  procedureType: string;
  cost: number;
  amountPaid: number;
  isAdultTeeth: boolean;
  toothNumbers: number[];
  notes: string[];
  importantNotes: string[];
  // Per-tooth conditions recorded with this act (used for both create and update).
  toothConditions: ToothConditionInput[];
}

export const dentalRecordsApi = {
  list: async (patientId: string): Promise<DentalRecordDto[]> => {
    return apiGet<DentalRecordDto[]>(`/patients/${patientId}/dental-records`);
  },

  create: async (patientId: string, data: CreateDentalRecordRequest): Promise<DentalRecordDto> => {
    return apiPost<DentalRecordDto>(`/patients/${patientId}/dental-records`, data);
  },

  update: async (patientId: string, id: string, data: CreateDentalRecordRequest): Promise<DentalRecordDto> => {
    return apiPut<DentalRecordDto>(`/patients/${patientId}/dental-records/${id}`, data);
  },

  delete: async (patientId: string, id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/dental-records/${id}`);
  },
};

