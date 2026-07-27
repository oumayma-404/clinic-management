import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { PatientDto, PatientDeletionCheckDto } from './types';

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
    /** Omit or send null when the patient gave none — the API no longer substitutes a placeholder. */
    email?: string | null;
    phoneNumber?: string | null;
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
    emergencyContactName?: string;
    emergencyContactPhone?: string;
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

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/patients/${id}`);
  },

  /**
   * What blocks this patient's deletion, and whether archiving is available instead. Called when the confirm
   * dialog opens so the user learns the answer before clicking, not after.
   */
  deletionCheck: async (id: string): Promise<PatientDeletionCheckDto> => {
    return apiGet<PatientDeletionCheckDto>(`/patients/${id}/deletion-check`);
  },

  /** Hide a patient from lists, search, recall and every picker without destroying anything. Reversible. */
  archive: async (id: string, reason?: string): Promise<PatientDto> => {
    return apiPost<PatientDto>(`/patients/${id}/archive`, { reason });
  },

  /** Restore an archived patient everywhere. */
  unarchive: async (id: string): Promise<PatientDto> => {
    return apiPost<PatientDto>(`/patients/${id}/unarchive`, {});
  },
};









