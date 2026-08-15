import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { SupplierDto, SupplierPageDto } from './types';
import type { PageParams } from './paging';

export interface SupplierPayload {
  /** The only required field (AC-1). */
  name: string;
  category?: string | null;
  phoneNumber?: string | null;
  address?: string | null;
  notes?: string | null;
  /**
   * Omit to leave the flag alone — the update command reads an absent key as « unchanged », which is what lets
   * « Désactiver » post one field instead of echoing the whole record back.
   */
  isActive?: boolean;
  /** The `version` read from the server. Echoed back so a concurrent edit 409s rather than being overwritten. */
  version?: number;
}

export interface SupplierListParams extends PageParams {
  /** Free-text over nom / catégorie / téléphone / adresse, matched server-side across the whole clinic. */
  q?: string;
  category?: string;
  /** The list screen sends true so a deactivated fournisseur stays reachable; the pickers leave it off. */
  includeInactive?: boolean;
}

export const suppliersApi = {
  /** One page, plus the catégorie options (the canonical suggestions unioned with the clinic's own). */
  listPaged: async (params: SupplierListParams): Promise<SupplierPageDto> =>
    apiGet<SupplierPageDto>('/suppliers', params),

  /**
   * Every active fournisseur — what the pickers read. Sends no paging, which the server models as a first-class
   * « read everything » case rather than as a huge page.
   */
  list: async (): Promise<SupplierDto[]> => (await apiGet<SupplierPageDto>('/suppliers', {})).items,

  create: async (data: SupplierPayload): Promise<SupplierDto> => apiPost<SupplierDto>('/suppliers', data),

  update: async (id: string, data: SupplierPayload): Promise<SupplierDto> =>
    apiPut<SupplierDto>(`/suppliers/${id}`, data),

  /**
   * Refused with `supplier_in_use` when stock articles or bons de prothèse reference it — the message names the
   * counts and points at « Désactiver ».
   */
  delete: async (id: string): Promise<void> => apiDelete<void>(`/suppliers/${id}`),
};

/** The refusal codes this feature can return, so a caller branches on the code and never on the French prose. */
export const SUPPLIER_DUPLICATE_CODE = 'supplier_duplicate';
export const SUPPLIER_IN_USE_CODE = 'supplier_in_use';
