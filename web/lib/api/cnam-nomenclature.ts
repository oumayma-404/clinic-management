import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { CnamNomenclatureEntryDto, CnamLetterValueDto } from './types';

export const cnamNomenclatureApi = {
  // DB-backed CNAM dental nomenclature. `q`/`category` optional (empty → full list). `includeInactive`
  // is used by the admin screen to also show deactivated rows.
  list: async (q?: string, category?: string, includeInactive?: boolean): Promise<CnamNomenclatureEntryDto[]> => {
    return apiGet<CnamNomenclatureEntryDto[]>('/cnam-nomenclature', { q, category, includeInactive });
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
  }): Promise<CnamNomenclatureEntryDto> => {
    return apiPut<CnamNomenclatureEntryDto>(`/cnam-nomenclature/${id}`, data);
  },

  deactivate: async (id: string): Promise<void> => {
    return apiDelete<void>(`/cnam-nomenclature/${id}`);
  },

  // Clears the provisional "à vérifier" flag on every catalog entry + VLC value.
  confirmData: async (): Promise<void> => {
    return apiPost<void>('/cnam-nomenclature/confirm', {});
  },

  updateLetterValue: async (id: string, value: number): Promise<CnamLetterValueDto> => {
    return apiPut<CnamLetterValueDto>(`/cnam-nomenclature/letter-values/${id}`, { value });
  },
};

// ── Indicative reimbursement estimate (editor-only) ────────────────────────────────────────────────
// The estimate is purely a UI aid: it is NEVER persisted and NEVER printed on the BS1 PDF (spec R-9).
// It mirrors the authoritative, tested backend calculator (GET /cnam-nomenclature/reimbursement-estimate,
// CnamReimbursementCalculator): estimate = coefficient × VLC × rate, where the VLC values are the
// admin-managed DB set and the rate is age-based (70% ages 4–18 inclusive, 60% otherwise; unknown DOB →
// 60%). Computed client-side from the fetched VLC map so the acts table stays live per keystroke.

const CHILD_RATE = 0.7;
const ADULT_RATE = 0.6;

// Age in full years at the care date (not today), matching the backend calculator.
function ageAt(dateOfBirth: string, careDate: Date): number {
  const dob = new Date(dateOfBirth);
  let age = careDate.getFullYear() - dob.getFullYear();
  const m = careDate.getMonth() - dob.getMonth();
  if (m < 0 || (m === 0 && careDate.getDate() < dob.getDate())) {
    age--;
  }
  return age;
}

export function reimbursementRate(dateOfBirth: string | null | undefined, careDate: Date): number {
  if (!dateOfBirth) return ADULT_RATE;
  const age = ageAt(dateOfBirth, careDate);
  return age >= 4 && age <= 18 ? CHILD_RATE : ADULT_RATE;
}

// Parse a cotation string ("<lettreCle> <coefficient>", e.g. "D 15") into its parts.
// Returns null when the format does not match (free-text act → no estimate).
function parseCotation(cotation: string): { lettreCle: string; coefficient: number } | null {
  const match = cotation.trim().match(/^([A-Za-z]+)\s+([0-9]+(?:[.,][0-9]+)?)$/);
  if (!match) {
    return null;
  }
  const coefficient = parseFloat(match[2].replace(',', '.'));
  if (!Number.isFinite(coefficient)) {
    return null;
  }
  return { lettreCle: match[1].toUpperCase(), coefficient };
}

/**
 * Indicative reimbursement estimate for a single act, from its cotation cell.
 * Estimate = coefficient × VLC(lettreCle) × age-rate. Returns null (→ "—") for free-text acts, a lettre
 * clé with no VLC value, or a missing/zero coefficient.
 */
export function estimateReimbursement(
  cotation: string,
  vlcMap: Record<string, number>,
  dateOfBirth: string | null | undefined,
  careDate: Date,
): number | null {
  const parsed = parseCotation(cotation);
  if (!parsed) {
    return null;
  }
  const vlc = vlcMap[parsed.lettreCle];
  if (vlc === undefined || parsed.coefficient <= 0) {
    return null;
  }
  return parsed.coefficient * vlc * reimbursementRate(dateOfBirth, careDate);
}
