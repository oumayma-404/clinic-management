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
  }): Promise<ProcedureTypeDto> => {
    return apiPost<ProcedureTypeDto>('/procedure-types', {
      name: data.name,
      defaultDurationMinutes: data.defaultDurationMinutes,
      defaultCost: data.defaultCost,
      colorHex: data.colorHex,
      description: data.description,
    });
  },

  update: async (id: string, data: {
    name?: string;
    defaultDurationMinutes?: number;
    defaultCost?: number | null;
    colorHex?: string;
    description?: string;
  }): Promise<ProcedureTypeDto> => {
    return apiPut<ProcedureTypeDto>(`/procedure-types/${id}`, data);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/procedure-types/${id}`);
  },
};


