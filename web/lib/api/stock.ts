import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { StockItemDto } from './types';

export interface StockItemPayload {
  name: string;
  category: string;
  unit: string;
  currentStock: number;
  minimumStockLevel: number;
  maximumStockLevel?: number | null;
  description?: string | null;
  unitPrice?: number | null;
  supplier?: string | null;
}

export const stockApi = {
  list: async (lowStockOnly: boolean = false): Promise<StockItemDto[]> => {
    return apiGet<StockItemDto[]>('/stock', { lowStockOnly });
  },

  create: async (data: StockItemPayload): Promise<StockItemDto> => {
    return apiPost<StockItemDto>('/stock', data);
  },

  update: async (id: string, data: StockItemPayload): Promise<StockItemDto> => {
    return apiPut<StockItemDto>(`/stock/${id}`, data);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/stock/${id}`);
  },
};
