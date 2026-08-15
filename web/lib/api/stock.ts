import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { StockItemDto, StockPageDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export interface StockMovementDto {
  id: string;
  stockItemId: string;
  /** "Consume" | "Restock" | "Adjustment" — Adjustment is a manual stock-take correction (AC-P4.16). */
  type: string;
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
  /**
   * The fournisseur this article is ordered from.
   *
   * ⚠️ **Tri-state on the wire** (AC-5): omit the key to leave the link alone, send `null` to clear it. The form
   * always sends it, which is what makes clearing a supplier work — `|| undefined` would be read as "unchanged"
   * and the clear would silently do nothing.
   */
  supplierId?: string | null;
  /**
   * Why on-hand was corrected, recorded on the resulting `Adjustment` movement (AC-P4.17). The update path
   * writes a real ledger row now — it used to overwrite the quantity with no trace of what changed or why.
   */
  stockChangeReason?: string | null;
  /**
   * The `version` read from the server (AC-P4.18). Echoed back so a concurrent consume is refused with a 409
   * rather than silently overwritten. Omitted / 0 skips the check.
   */
  version?: number;
}

export const stockApi = {
  list: async (lowStockOnly: boolean = false): Promise<StockItemDto[]> => {
    return (await apiGet<StockPageDto>('/stock', { lowStockOnly })).items;
  },

  /**
   * One page of stock items, plus the clinic-wide low-stock / expiring counts and the category options.
   * `search` matches name / catégorie / fournisseur server-side over the whole clinic; `category` and
   * `expiringOnly` are server-side filters too (they used to be applied in the browser).
   */
  listPaged: async (
    params: PageParams & { lowStockOnly?: boolean; category?: string; expiringOnly?: boolean },
  ): Promise<StockPageDto> => apiGet<StockPageDto>('/stock', params),

  create: async (data: StockItemPayload): Promise<StockItemDto> => {
    return apiPost<StockItemDto>('/stock', data);
  },

  update: async (id: string, data: StockItemPayload): Promise<StockItemDto> => {
    return apiPut<StockItemDto>(`/stock/${id}`, data);
  },

  // Movement-based adjustments (finding #14): a sortie (consume) or entrée (restock) by delta.
  consume: async (id: string, quantity: number, reason?: string | null): Promise<StockItemDto> => {
    return apiPost<StockItemDto>(`/stock/${id}/consume`, { quantity, reason });
  },

  // A restock creates a LOT carrying its own expiry/batch (AC-P4.1/4.2) — it no longer overwrites the item's
  // single scalar date, which is what made a second delivery destroy the first one's expiry.
  restock: async (
    id: string,
    data: {
      quantity: number;
      expiryDate?: string | null;
      batchNumber?: string | null;
      reason?: string | null;
    },
  ): Promise<StockItemDto> => {
    return apiPost<StockItemDto>(`/stock/${id}/restock`, data);
  },

  movements: async (id: string): Promise<StockMovementDto[]> => {
    return apiGet<StockMovementDto[]>(`/stock/${id}/movements`);
  },

  delete: async (id: string): Promise<void> => {
    return apiDelete<void>(`/stock/${id}`);
  },

  /**
   * The clinic's approaching-expiry window, in days (AC-20).
   *
   * ⚠️ **`0` means the alert is switched off**, not « prévenez-moi le jour même ». Both server readers
   * (`StockExpiryJob`, `DashboardAlertsReader`) have always treated a non-positive value that way; until this
   * feature the domain guard refused `0`, so the one value meaning "off" was the one value unreachable — and the
   * setter had no caller at all, so every clinic ran on the 30-day default for the life of the product.
   */
  getExpirySettings: async (): Promise<StockExpirySettingsDto> => {
    return apiGet<StockExpirySettingsDto>(`/stock/expiry-settings`);
  },

  /** Admin-only server-side. Refuses anything outside 0–365 with a French message. */
  setExpirySettings: async (leadDays: number): Promise<StockExpirySettingsDto> => {
    return apiPut<StockExpirySettingsDto>(`/stock/expiry-settings`, { leadDays });
  },
};

export interface StockExpirySettingsDto {
  /** Days of warning before a lot expires. `0` = alerte désactivée. */
  leadDays: number;
}
