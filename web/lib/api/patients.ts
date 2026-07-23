import { apiGet, apiPost, apiPut } from './client';
import type { PatientDto } from './types';

export const patientsApi = {
  list: async (params?: { searchTerm?: string; limit?: number }): Promise<PatientDto[]> => {
    return apiGet<PatientDto[]>('/patients', params);
  },

  get: async (id: string): Promise<PatientDto> => {
    return apiGet<PatientDto>(`/patients/${id}`);
  },

  // Live, AI-generated French summary (not persisted). 404 = missing/other-clinic; 400 = AI unavailable.
  getAiSummary: async (patientId: string): Promise<{ summary: string }> => {
    return apiGet<{ summary: string }>(`/patients/${patientId}/ai-summary`);
  },

  create: async (data: {
    firstName: string;
    lastName: string;
    dateOfBirth?: string;
    gender?: string;
    email?: string;
    phoneNumber?: string;
    medicalHistory?: string;
    allergies?: string;
    address?: {
      street: string;
      city: string;
      state: string;
      zipCode: string;
      country?: string;
    };
    insuranceInfo?: {
      provider: string;
      policyNumber: string;
      groupNumber?: string;
      expiryDate?: string;
    };
    cnamInfo?: {
      identifiantUnique?: string | null;
      regime?: string | null;
      assureFirstName?: string | null;
      assureLastName?: string | null;
      assureAddress?: string | null;
      assurePostalCode?: string | null;
      maladeLien?: string | null;
      maladeLienRang?: string | null;
    };
    medicalHistoryEntries?: Array<{
      description: string;
      date?: string;
      notes?: string;
    }>;
    familyHistoryEntries?: Array<{
      relationship: string;
      condition: string;
      notes?: string;
    }>;
    isFlagged?: boolean;
    flagNotes?: string;
  }): Promise<PatientDto> => {
    return apiPost<PatientDto>('/patients', data);
  },

  update: async (
    id: string,
    data: Partial<PatientDto> & { isFlagged?: boolean; flagNotes?: string },
  ): Promise<PatientDto> => {
    return apiPut<PatientDto>(`/patients/${id}`, data);
  },
};









