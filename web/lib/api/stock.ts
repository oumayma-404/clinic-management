import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { StockItemDto } from './types';

export interface StockMovementDto {
  id: string;
  stockItemId: string;
  type: string; // "Consume" | "Restock"
  quantity: number;
  resultingStock: number;
  reason?: string | null;
  createdAt: string;
}

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

  // Movement-based adjustments (finding #14): a sortie (consume) or entrée (restock) by delta.
  consume: async (id: string, quantity: number): Promise<StockItemDto> => {
    return apiPost<StockItemDto>(`/stock/${id}/consume`, { quantity });
  },

  restock: async (
    id: string,
    data: { quantity: number; expiryDate?: string | null; batchNumber?: string | null },
  ): Promise<StockItemDto> => {
    return apiPost<StockItemDto>(`/stock/${id}/restock`, data);
  },

  movements: async (id: string): Promise<StockMovementDto[]> => {
    return apiGet<StockMovementDto[]>(`/stock/${id}/movements`);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/stock/${id}`);
  },
};
