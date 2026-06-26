import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { PatientMedicalHistoryDto } from './types';

export const patientMedicalHistoryApi = {
  list: async (patientId: string): Promise<PatientMedicalHistoryDto[]> => {
    return apiGet<PatientMedicalHistoryDto[]>(`/patients/${patientId}/medical-history`);
  },

  create: async (patientId: string, data: {
    description: string;
    date?: string;
    notes?: string;
  }): Promise<PatientMedicalHistoryDto> => {
    return apiPost<PatientMedicalHistoryDto>(`/patients/${patientId}/medical-history`, data);
  },

  update: async (patientId: string, id: string, data: {
    description?: string;
    date?: string;
    notes?: string;
  }): Promise<PatientMedicalHistoryDto> => {
    return apiPut<PatientMedicalHistoryDto>(`/patients/${patientId}/medical-history/${id}`, data);
  },

  delete: async (patientId: string, id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${patientId}/medical-history/${id}`);
  },
};










