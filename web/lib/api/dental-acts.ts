import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { DentalActDto, CnamLetterValueDto } from './types';
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

  // Clears the provisional "à vérifier" flag on every catalog act AND every valeur de la lettre clé.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/dental-acts/confirm', {});
  },

  // ── Valeurs de la lettre clé (VLC) ────────────────────────────────────────────────────────────
  // They moved onto this controller with the catalogue merge: a cotation is meaningless without the
  // dinar value of its lettre clé, so one screen owns both. Readable by any authenticated user.
  listLetterValues: async (): Promise<CnamLetterValueDto[]> => {
    return apiGet<CnamLetterValueDto[]>('/dental-acts/letter-values');
  },

  updateLetterValue: async (id: string, value: number, version?: number): Promise<CnamLetterValueDto> => {
    return apiPut<CnamLetterValueDto>(`/dental-acts/letter-values/${id}`, { value, version });
  },
};

// ── Indicative reimbursement estimate (editor-only) ────────────────────────────────────────────────
// The estimate is purely a UI aid: it is NEVER persisted and NEVER printed on the BS1 PDF (spec R-9,
// AC-P6.16).
//
// The arithmetic is the BACKEND's (AC-P6.15). This module used to carry its own `CHILD_RATE`/`ADULT_RATE`,
// its own age-at-care-date computation and its own `coefficient × VLC × rate` — a second authority over a
// reimbursement figure, which would have drifted the first time CNAM moved a rate or the band edges. One call
// per bulletin goes to `POST /dental-acts/reimbursement-estimates`, whose handler shares
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
  /**
   * Why `estimate` is null, when it is: `"MissingCoefficient"` (the act carries no cotation — a gap an admin
   * can close in « Actes dentaires ») or `"NoLetterValue"` (the convention fixes no valeur for that lettre clé,
   * which nobody can close). Branch on this, never on a sentence.
   */
  unavailableReason: 'MissingCoefficient' | 'NoLetterValue' | null;
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
  return apiPost<ReimbursementEstimateDto[]>('/dental-acts/reimbursement-estimates', {
    items,
    patientDateOfBirth: patientDateOfBirth ?? null,
    careDate: careDate ?? null,
  });
}
