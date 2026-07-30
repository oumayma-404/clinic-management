import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { MedicationDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export const medicationsApi = {
  // DB-backed medication catalog. `q` optional (empty → full list). `includeInactive` is used by the
  // admin screen to also show deactivated rows; the ordonnance picker uses the default (active only).
  list: async (q?: string, includeInactive?: boolean): Promise<MedicationDto[]> => {
    return unwrapPaged(await apiGet<PagedResponse<MedicationDto>>('/medications', { q, includeInactive }));
  },

  /**
   * One page of the drug catalog. `search` maps to the endpoint's `q` and matches marque / forme / dosage **and
   * the DCI rows** server-side — prescribers search by molecule as often as by brand.
   */
  listPaged: async (
    params: PageParams & { includeInactive?: boolean },
  ): Promise<PagedResponse<MedicationDto>> => {
    const { search, ...rest } = params;
    return apiGet<PagedResponse<MedicationDto>>('/medications', { ...rest, q: search });
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
