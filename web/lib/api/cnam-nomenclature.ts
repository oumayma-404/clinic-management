import { apiGet } from './client';
import type { CnamNomenclatureEntryDto } from './types';

export const cnamNomenclatureApi = {
  // Curated CNAM dental nomenclature. `q` and `category` are optional; empty → full list.
  list: async (q?: string, category?: string): Promise<CnamNomenclatureEntryDto[]> => {
    return apiGet<CnamNomenclatureEntryDto[]>('/cnam-nomenclature', { q, category });
  },
};

// ── Indicative reimbursement estimate (editor-only) ────────────────────────────────────────────────
// The estimate is purely a UI aid: it is NEVER persisted and NEVER printed on the BS1 PDF (spec AC-5).
// ⚠ PENDING VERIFICATION: the dinar value per lettre clé and the rates below are best-effort
// indicative defaults, NOT authoritative CNAM tariffs. Admin editing of these is a later feature.

// Conventional dinar value per lettre clé (TND per coefficient unit).
const VALEUR_LETTRE_CLE: Record<string, number> = {
  CD: 7,
  CDS: 10,
  VD: 10,
  D: 1.2,
  RD: 2,
};

const STANDARD_RATE = 0.7; // régime privé — taux de remboursement standard (indicatif)
const APCI_RATE = 1.0; // affection prise en charge intégralement

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
 * Estimate = coefficient × valeur(lettreCle) × rate (APCI rate when the care type is APCI, else standard).
 * Returns null (→ blank cell) for free-text acts, an unknown lettre clé, or a missing/zero coefficient.
 */
export function estimateReimbursement(cotation: string, careType: string): number | null {
  const parsed = parseCotation(cotation);
  if (!parsed) {
    return null;
  }
  const valeur = VALEUR_LETTRE_CLE[parsed.lettreCle];
  if (valeur === undefined || parsed.coefficient <= 0) {
    return null;
  }
  const rate = careType === 'APCI' ? APCI_RATE : STANDARD_RATE;
  return parsed.coefficient * valeur * rate;
}
