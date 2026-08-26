import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { CnamNomenclatureEntryDto, CnamLetterValueDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

export const cnamNomenclatureApi = {
  // DB-backed CNAM dental nomenclature. `q`/`category` optional (empty → full list). `includeInactive`
  // is used by the admin screen to also show deactivated rows.
  list: async (q?: string, category?: string, includeInactive?: boolean): Promise<CnamNomenclatureEntryDto[]> => {
    return unwrapPaged(
      await apiGet<PagedResponse<CnamNomenclatureEntryDto>>('/cnam-nomenclature', { q, category, includeInactive }),
    );
  },

  /** One page of the nomenclature. `search` maps to `q` and matches code / désignation / lettre clé server-side. */
  listPaged: async (
    params: PageParams & { category?: string; includeInactive?: boolean },
  ): Promise<PagedResponse<CnamNomenclatureEntryDto>> => {
    const { search, ...rest } = params;
    return apiGet<PagedResponse<CnamNomenclatureEntryDto>>('/cnam-nomenclature', { ...rest, q: search });
  },

  // Valeurs de la lettre clé (VLC). Readable by any authenticated user.
  listLetterValues: async (): Promise<CnamLetterValueDto[]> => {
    return apiGet<CnamLetterValueDto[]>('/cnam-nomenclature/letter-values');
  },

  // ── Admin writes ──────────────────────────────────────────────────────────────────────────────
  create: async (data: {
    codeActe: string;
    designationFr: string;
    lettreCle: string;
    coefficient: number;
    category: string;
  }): Promise<CnamNomenclatureEntryDto> => {
    return apiPost<CnamNomenclatureEntryDto>('/cnam-nomenclature', data);
  },

  update: async (id: string, data: {
    codeActe: string;
    designationFr: string;
    lettreCle: string;
    coefficient: number;
    category: string;
    /** The version read from the server. Omitted (or 0) the server skips the check — see `PatientDto.version`. */
    version?: number;
  }): Promise<CnamNomenclatureEntryDto> => {
    return apiPut<CnamNomenclatureEntryDto>(`/cnam-nomenclature/${id}`, data);
  },

  /**
   * Reactivate an entry switched off by mistake.
   *
   * ⚠️ It had no client and no route: the entity's `Activate()` existed and nothing could reach it, so a acte
   * deactivated by accident stayed deactivated for ever — a soft delete whose inverse is unreachable is a hard
   * delete with extra steps.
   */
  reactivate: async (id: string): Promise<void> => {
    return apiPost<void>(`/cnam-nomenclature/${id}/activate`, {});
  },

  deactivate: async (id: string): Promise<void> => {
    return apiDelete<void>(`/cnam-nomenclature/${id}`);
  },

  // Clears the provisional "à vérifier" flag on every catalog entry + VLC value.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/cnam-nomenclature/confirm', {});
  },

  updateLetterValue: async (id: string, value: number, version?: number): Promise<CnamLetterValueDto> => {
    return apiPut<CnamLetterValueDto>(`/cnam-nomenclature/letter-values/${id}`, { value, version });
  },
};

// ── Indicative reimbursement estimate (editor-only) ────────────────────────────────────────────────
// The estimate is purely a UI aid: it is NEVER persisted and NEVER printed on the BS1 PDF (spec R-9,
// AC-P6.16).
//
// The arithmetic is the BACKEND's (AC-P6.15). This module used to carry its own `CHILD_RATE`/`ADULT_RATE`,
// its own age-at-care-date computation and its own `coefficient × VLC × rate` — a second authority over a
// reimbursement figure, which would have drifted the first time CNAM moved a rate or the band edges. One call
// per bulletin now goes to `POST /cnam-nomenclature/reimbursement-estimates`, whose handler shares
// `CnamReimbursementCalculator` with the BS1 claim side.
//
// What stays here is *parsing the cotation cell*, which is input handling rather than calculation: the
// endpoint takes a lettre clé and a coefficient, and the editor's single free-text field holds both.

/** Parsed act ready to be estimated. `careDate` is per-act — the rate depends on the age at the care date. */
export interface ReimbursementEstimateItem {
  lettreCle: string;
  coefficient: number;
  careDate?: string | null;
}

/** One estimate, aligned by index to the request's items. `estimate: null` renders as "—", never as zero. */
export interface ReimbursementEstimateDto {
  estimate: number | null;
  rateApplied: number;
}

/**
 * Parse a cotation string ("<lettreCle> <coefficient>", e.g. "D 15") into its parts.
 * Returns null when the format does not match (free-text act → no estimate).
 */
export function parseCotation(cotation: string): { lettreCle: string; coefficient: number } | null {
  const match = cotation.trim().match(/^([A-Za-z]+)\s+([0-9]+(?:[.,][0-9]+)?)$/);
  if (!match) {
    return null;
  }
  const coefficient = parseFloat(match[2].replace(',', '.'));
  if (!Number.isFinite(coefficient) || coefficient <= 0) {
    return null;
  }
  return { lettreCle: match[1].toUpperCase(), coefficient };
}

/**
 * Indicative estimates for every act of one bulletin, in one round trip. Results are aligned to `items`
 * by index. Throws `ApiError` on failure — the caller must surface that rather than showing nothing
 * (AC-P6.17): a silent empty column is indistinguishable from « aucun acte remboursable ».
 */
export async function estimateReimbursements(
  items: ReimbursementEstimateItem[],
  patientDateOfBirth: string | null | undefined,
  careDate?: string | null,
): Promise<ReimbursementEstimateDto[]> {
  if (items.length === 0) {
    return [];
  }
  return apiPost<ReimbursementEstimateDto[]>('/cnam-nomenclature/reimbursement-estimates', {
    items,
    patientDateOfBirth: patientDateOfBirth ?? null,
    careDate: careDate ?? null,
  });
}
