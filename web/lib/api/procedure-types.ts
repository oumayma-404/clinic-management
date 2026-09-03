import { apiGet, apiPost, apiPut, apiDelete } from './client';
import type { ProcedureTypeDto, ProcedureStepTemplateDto } from './types';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

/** One selectable agenda colour: the value that is stored, and what the server calls it. */
export interface ProcedureColor {
  hex: string;
  /** « Bleu moyen » — composed server-side, so no client holds a hex→French map of its own. */
  label: string;
  /** « Clair » / « Moyen » / « Foncé » — the nuance strip's caption once a family is picked. */
  tone: string;
}

/**
 * One hue family of the palette. These two types live here rather than in `types.ts` because only the colour
 * picker reads them and they only make sense beside `getColorPalette` — the reasoning `paging.ts` follows.
 */
export interface ProcedureColorFamily {
  key: string;
  label: string;
  colors: ProcedureColor[];
}

export const procedureTypesApi = {
  list: async (includeInactive: boolean = false): Promise<ProcedureTypeDto[]> => {
    return unwrapPaged(
      await apiGet<PagedResponse<ProcedureTypeDto>>('/procedure-types', { includeInactive }),
    );
  },

  /**
   * One page of acts, ordered by catégorie then nom. `search` matches nom / catégorie / description server-side
   * over the whole catalog; `category` narrows to one discipline — also server-side, because narrowing an
   * already-cut page shrinks pages unpredictably. An unrecognised `category` matches nothing rather than failing.
   */
  listPaged: async (
    params: PageParams & { includeInactive?: boolean; category?: string },
  ): Promise<PagedResponse<ProcedureTypeDto>> =>
    apiGet<PagedResponse<ProcedureTypeDto>>('/procedure-types', params),

  get: async (id: string): Promise<ProcedureTypeDto> => {
    return apiGet<ProcedureTypeDto>(`/procedure-types/${id}`);
  },

  create: async (data: {
    name: string;
    defaultDurationMinutes: number;
    defaultCost?: number | null;
    colorHex: string;
    description?: string;
    category?: string;
    resultingCondition?: string | null;
    /**
     * The act's suggested clinical steps. Accepted on create as well as update deliberately: the form posts one
     * body, and an act creatable only without its protocol sends every practice through create-then-edit.
     */
    defaultSteps?: ProcedureStepTemplateDto[];
  }): Promise<ProcedureTypeDto> => {
    return apiPost<ProcedureTypeDto>('/procedure-types', {
      name: data.name,
      defaultDurationMinutes: data.defaultDurationMinutes,
      defaultCost: data.defaultCost,
      colorHex: data.colorHex,
      description: data.description,
      category: data.category,
      resultingCondition: data.resultingCondition,
      defaultSteps: data.defaultSteps,
    });
  },

  update: async (id: string, data: {
    name?: string;
    defaultDurationMinutes?: number;
    defaultCost?: number | null;
    colorHex?: string;
    description?: string;
    /** Tri-state, like every field here: omit = unchanged, `""` = unfile the act, a label = file it. */
    category?: string;
    resultingCondition?: string | null;
    /**
     * The act's suggested clinical steps. **Tri-state, and a list gets the distinction for free**: omit the key
     * to leave the template alone, send `[]` to clear it (« cet acte se fait en une séance », a real answer),
     * send a list to replace it. No `Specified` companion is needed, unlike `defaultCost` — `null` and `[]` are
     * already different JSON values.
     *
     * Editing it touches **no** devis: a template is copied onto a plan line when the act is added, and the line
     * owns its steps from then on, so re-wording one can never rewrite the protocol of a bridge under way.
     */
    defaultSteps?: ProcedureStepTemplateDto[];
    /** The version read from the server. Omitted (or 0) the server skips the check — see `PatientDto.version`. */
    version?: number;
  }): Promise<ProcedureTypeDto> => {
    return apiPut<ProcedureTypeDto>(`/procedure-types/${id}`, data);
  },

  /**
   * AC-P4.14 — replaces the act's material list wholesale. An empty array is a real value meaning « this act
   * consumes nothing » (the opt-out, AC-P4.11), which is why this is a separate call from `update`, whose
   * every field means "unchanged" when omitted. Admin-only server-side.
   */
  setMaterials: async (
    id: string,
    materials: { stockItemId: string; quantityPerAct: number }[],
  ): Promise<ProcedureTypeDto> => {
    return apiPut<ProcedureTypeDto>(`/procedure-types/${id}/materials`, { materials });
  },

  /**
   * AC-P2.36: the palette the backend `ColorHex` value object accepts, **grouped by hue family and named**.
   *
   * Grouped because a clinic's act catalogue outgrows ten colours long before it outgrows twelve hues, and the
   * picker offers one swatch per family with its nuances only once a family is chosen — 36 loose swatches is a
   * wall rather than a choice. Named because this module's consumer used to carry its own hex→French map, so a
   * colour added server-side rendered under its raw hex until somebody remembered to name it too; the endpoint is
   * now the authority on *which* colours are valid **and** on what they are called.
   */
  getColorPalette: async (): Promise<ProcedureColorFamily[]> => {
    return apiGet<ProcedureColorFamily[]>('/procedure-types/colors');
  },

  /**
   * The categories to offer when filing or filtering an act: the suggested clinical disciplines in clinical order,
   * followed by any category this clinic invented for itself.
   *
   * Fetched rather than hardcoded for the same reason `getColorPalette` is — but with a stronger one: half the list is
   * *data*. Only the server knows which categories the clinic has used, and a suggestion list missing them is what
   * makes the next admin retype « endodontie » and split a discipline in two.
   */
  getCategories: async (): Promise<string[]> => {
    return apiGet<string[]>('/procedure-types/categories');
  },

  /**
   * Deletes an act — or **archives** it when a future rendez-vous still refers to it. The server decides from
   * usage, so `archived` is the only way the caller can know which happened.
   *
   * ⚠️ It used to return `void`, so the screen could say nothing and the row simply vanished either way — a
   * permanent delete was indistinguishable from a deactivation on the one action that cannot be undone.
   */
  delete: async (id: string): Promise<{ archived: boolean }> => {
    return apiDelete<{ archived: boolean }>(`/procedure-types/${id}`);
  },

  // Idempotently tops the clinic's ProcedureType menu up from the seeded starter catalogue, skipping names
  // already present — so it never overwrites a price the clinic has already set. Returns how many rows landed.
  // ⚠️ It claimed « ~42 » until the catalogue was cut from 43 rows to 19 (feef4d8a). The count belongs to the
  // seed, so naming one here is a second copy that goes stale the next time a row is added.
  initializeDefaults: async (): Promise<{ added: number }> => {
    return apiPost<{ added: number }>('/procedure-types/initialize-defaults', {});
  },
};


