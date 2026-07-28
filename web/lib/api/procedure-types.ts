import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ProcedureTypeDto } from './types';

export const procedureTypesApi = {
  list: async (includeInactive: boolean = false): Promise<ProcedureTypeDto[]> => {
    return apiGet<ProcedureTypeDto[]>('/procedure-types', { includeInactive });
  },

  get: async (id: string): Promise<ProcedureTypeDto> => {
    return apiGet<ProcedureTypeDto>(`/procedure-types/${id}`);
  },

  create: async (data: {
    name: string;
    defaultDurationMinutes: number;
    defaultCost?: number | null;
    colorHex: string;
    description?: string;
    resultingCondition?: string | null;
  }): Promise<ProcedureTypeDto> => {
    return apiPost<ProcedureTypeDto>('/procedure-types', {
      name: data.name,
      defaultDurationMinutes: data.defaultDurationMinutes,
      defaultCost: data.defaultCost,
      colorHex: data.colorHex,
      description: data.description,
      resultingCondition: data.resultingCondition,
    });
  },

  update: async (id: string, data: {
    name?: string;
    defaultDurationMinutes?: number;
    defaultCost?: number | null;
    colorHex?: string;
    description?: string;
    resultingCondition?: string | null;
  }): Promise<ProcedureTypeDto> => {
    return apiPut<ProcedureTypeDto>(`/procedure-types/${id}`, data);
  },

  /**
   * AC-P2.36: the palette the backend `ColorHex` value object actually accepts. Returns **bare hex strings with
   * no names** (A-14), which is why the French labels stay client-side — the endpoint is the authority on
   * *which* colours are valid, not on how they are called.
   *
   * It had zero callers, so the frontend carried its own hardcoded copy under a "must match backend" comment:
   * the two could drift, and a colour added or retired server-side either vanished from the picker or was
   * offered and then rejected with `ArgumentException`.
   */
  getColors: async (): Promise<string[]> => {
    return apiGet<string[]>('/procedure-types/colors');
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/procedure-types/${id}`);
  },

  // Idempotently seeds the clinic's ProcedureType menu with ~42 common Tunisian dental procedures,
  // skipping names already present. Returns the number of newly-added entries.
  initializeDefaults: async (): Promise<{ added: number }> => {
    return apiPost<{ added: number }>('/procedure-types/initialize-defaults', {});
  },
};


