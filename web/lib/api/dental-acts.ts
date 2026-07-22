import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalActDto } from './types';

export interface DentalActInput {
  codeActe: string;
  designationFr: string;
  lettreCle: string;
  coefficient?: number | null;
  category: string;
  defaultFee?: number | null;
  requiresAccordPrealable: boolean;
}

export const dentalActsApi = {
  // DB-backed dental act catalog. `q`/`category` optional (empty → full list). `includeInactive` is used
  // by the admin screen to also show deactivated rows.
  list: async (q?: string, category?: string, includeInactive?: boolean): Promise<DentalActDto[]> => {
    return apiGet<DentalActDto[]>('/dental-acts', { q, category, includeInactive });
  },

  // ── Admin writes ──────────────────────────────────────────────────────────────────────────────
  create: async (data: DentalActInput): Promise<DentalActDto> => {
    return apiPost<DentalActDto>('/dental-acts', data);
  },

  update: async (id: string, data: DentalActInput): Promise<DentalActDto> => {
    return apiPut<DentalActDto>(`/dental-acts/${id}`, data);
  },

  deactivate: async (id: string): Promise<void> => {
    return apiDelete<void>(`/dental-acts/${id}`);
  },

  // Clears the provisional "à vérifier" flag on every catalog entry.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/dental-acts/confirm', {});
  },
};
