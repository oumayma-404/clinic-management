import { apiGet, apiPost, apiPut } from './client';
import type { PatientDto } from './types';

export const patientsApi = {
  list: async (params?: { searchTerm?: string; limit?: number }): Promise<PatientDto[]> => {
    return apiGet<PatientDto[]>('/patients', params);
  },

  get: async (id: string): Promise<PatientDto> => {
    return apiGet<PatientDto>(`/patients/${id}`);
  },

  create: async (data: {
    firstName: string;
    lastName: string;
    dateOfBirth?: string;
    gender?: string;
    email?: string;
    phoneNumber?: string;
  }): Promise<PatientDto> => {
    return apiPost<PatientDto>('/patients', data);
  },

  update: async (id: string, data: Partial<PatientDto>): Promise<PatientDto> => {
    return apiPut<PatientDto>(`/patients/${id}`, data);
  },
};


