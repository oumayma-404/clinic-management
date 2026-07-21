import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { MedicationDto } from './types';

export const medicationsApi = {
  // DB-backed medication catalog. `q` optional (empty → full list). `includeInactive` is used by the
  // admin screen to also show deactivated rows; the ordonnance picker uses the default (active only).
  list: async (q?: string, includeInactive?: boolean): Promise<MedicationDto[]> => {
    return apiGet<MedicationDto[]>('/medications', { q, includeInactive });
  },

  // ── Admin writes ──────────────────────────────────────────────────────────────────────────────
  create: async (data: {
    brandName: string;
    form: string;
    strength: string;
    dcis: string[];
  }): Promise<MedicationDto> => {
    return apiPost<MedicationDto>('/medications', data);
  },

  update: async (id: string, data: {
    brandName: string;
    form: string;
    strength: string;
    dcis: string[];
  }): Promise<MedicationDto> => {
    return apiPut<MedicationDto>(`/medications/${id}`, data);
  },

  deactivate: async (id: string): Promise<void> => {
    return apiDelete<void>(`/medications/${id}`);
  },

  // Clears the provisional "à vérifier" flag on every catalog entry.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/medications/confirm', {});
  },
};
