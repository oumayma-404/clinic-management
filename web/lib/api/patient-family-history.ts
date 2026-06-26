import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { PatientFamilyHistoryDto } from './types';

export const patientFamilyHistoryApi = {
  list: async (patientId: string): Promise<PatientFamilyHistoryDto[]> => {
    return apiGet<PatientFamilyHistoryDto[]>(`/patients/${patientId}/family-history`);
  },

  create: async (patientId: string, data: {
    relationship: string;
    condition: string;
    notes?: string;
  }): Promise<PatientFamilyHistoryDto> => {
    return apiPost<PatientFamilyHistoryDto>(`/patients/${patientId}/family-history`, data);
  },

  update: async (patientId: string, id: string, data: {
    relationship?: string;
    condition?: string;
    notes?: string;
  }): Promise<PatientFamilyHistoryDto> => {
    return apiPut<PatientFamilyHistoryDto>(`/patients/${patientId}/family-history/${id}`, data);
  },

  delete: async (patientId: string, id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/family-history/${id}`);
  },
};










