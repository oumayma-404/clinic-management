import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalActDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface DentalActInput {
  codeActe: string;
  designationFr: string;
  lettreCle: string;
  coefficient?: number | null;
  category: string;
  defaultFee?: number | null;
  requiresAccordPrealable: boolean;
  /**
   * The version read from the server, on an update. Omitted (or 0) the server skips the concurrency check —
   * see `PatientDto.version`. Absent on create, where there is nothing to conflict with.
   */
  version?: number;
}

export const dentalActsApi = {
  // DB-backed dental act catalog. `q`/`category` optional (empty → full list). `includeInactive` is used
  // by the admin screen to also show deactivated rows.
  list: async (q?: string, category?: string, includeInactive?: boolean): Promise<DentalActDto[]> => {
    return unwrapPaged(
      await apiGet<PagedResponse<DentalActDto>>('/dental-acts', { q, category, includeInactive }),
    );
  },

  /** One page of the DCH catalog. `search` maps to `q` and matches code / désignation server-side. */
  listPaged: async (
    params: PageParams & { category?: string; includeInactive?: boolean },
  ): Promise<PagedResponse<DentalActDto>> => {
    const { search, ...rest } = params;
    return apiGet<PagedResponse<DentalActDto>>('/dental-acts', { ...rest, q: search });
  },

  // ── Admin writes ──────────────────────────────────────────────────────────────────────────────
  create: async (data: DentalActInput): Promise<DentalActDto> => {
    return apiPost<DentalActDto>('/dental-acts', data);
  },

  update: async (id: string, data: DentalActInput): Promise<DentalActDto> => {
    return apiPut<DentalActDto>(`/dental-acts/${id}`, data);
  },

  /**
   * Reactivate an entry switched off by mistake.
   *
   * ⚠️ It had no client and no route: the entity's `Activate()` existed and nothing could reach it, so a acte
   * deactivated by accident stayed deactivated for ever — a soft delete whose inverse is unreachable is a hard
   * delete with extra steps.
   */
  reactivate: async (id: string): Promise<void> => {
    return apiPost<void>(`/dental-acts/${id}/activate`, {});
  },

  deactivate: async (id: string): Promise<void> => {
    return apiDelete<void>(`/dental-acts/${id}`);
  },

  // Clears the provisional "à vérifier" flag on every catalog entry.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/dental-acts/confirm', {});
  },
};
