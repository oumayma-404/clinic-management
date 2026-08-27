import { apiPostFormData } from './client';

/**
 * Patient CSV import (L5) — the two calls behind « Importer des patients ».
 *
 * <p><b>Why the types live here and not in `types.ts`.</b> That file mirrors the DTOs several screens share; these
 * four are read by exactly one dialog and by nothing else, and the shapes only make sense beside the two functions
 * that produce them — the same reasoning `paging.ts` follows for `PagedResponse`.</p>
 *
 * <p>⚠️ <b>Both calls send the file.</b> Nothing is staged server-side between the preview and the commit: a staging
 * table would need an owner, a lifetime and a pruner, and its rows would outlive the tab that created them. The
 * consequence the caller must honour is that the commit has to re-send the <i>same</i> `File` and the <i>same</i>
 * mapping — the server re-reads and re-matches from scratch, which is exactly what makes the preview a promise it
 * keeps.</p>
 */

/** Every outcome a row can carry. Mirrors the backend enum; `Created`/`Skipped`/`Failed` only appear in a result. */
export type PatientImportRowOutcome =
  | 'Ready'
  | 'Duplicate'
  | 'Invalid'
  | 'Created'
  | 'Skipped'
  | 'Failed';

export interface PatientImportRow {
  /** The line in the uploaded file, header included — the number in Excel's own gutter. */
  lineNumber: number;
  displayName: string;
  outcome: PatientImportRowOutcome;
  /** French reasons the row cannot be created. */
  errors: string[];
  /** French notes about what will be dropped or defaulted if the row is imported as-is. */
  warnings: string[];
  duplicateOfPatientId?: string | null;
  /** Whose record it matches and on what — « Amine Ben Salah (même nom et date de naissance) ». */
  duplicateOf?: string | null;
}

/** One mappable patient field, as the mapping step needs it. The server owns the list and its French labels. */
export interface PatientImportField {
  /** The stable English token sent back in the mapping. */
  field: string;
  label: string;
  required: boolean;
}

export interface PatientImportPreview {
  headers: string[];
  /** `field token → column index`, as actually applied. */
  mapping: Record<string, number>;
  fields: PatientImportField[];
  /** « point-virgule (;) » — named, because a tab is invisible in a UI. */
  delimiter: string;
  encoding: string;
  /** True when the file held more rows than one import may carry. Must be shown, never absorbed. */
  truncated: boolean;
  rows: PatientImportRow[];
  readyCount: number;
  duplicateCount: number;
  invalidCount: number;
}

export interface PatientImportResult {
  createdCount: number;
  skippedCount: number;
  failedCount: number;
  rows: PatientImportRow[];
}

/** How a field the operator chose not to import is expressed on the wire — see the backend's `ResolveMapping`. */
export const IMPORT_FIELD_UNMAPPED = -1;

function buildForm(
  file: File,
  mapping?: Record<string, number>,
  createAnywayLines?: number[],
): FormData {
  const form = new FormData();
  form.append('file', file);
  // One JSON value rather than one form field per key: a multipart form cannot carry a nested object, and
  // `mapping[LastName]=0` binds partially and silently — a mistyped key becomes a column simply not imported.
  if (mapping) form.append('mapping', JSON.stringify(mapping));
  if (createAnywayLines?.length) form.append('createAnywayLines', createAnywayLines.join(','));
  return form;
}

export const patientImportApi = {
  /**
   * The dry run. Omit `mapping` on the first upload to get the server's own detection back — for this product's
   * own export that resolves every column, so the mapping step opens already filled in.
   */
  preview: (file: File, mapping?: Record<string, number>) =>
    apiPostFormData<PatientImportPreview>('/patients/import/preview', buildForm(file, mapping)),

  /**
   * The commit. `createAnywayLines` carries the file lines the operator ticked despite a duplicate match; omitting
   * it skips every duplicate, which is the deliberate default (this product has no patient merge).
   */
  commit: (file: File, mapping: Record<string, number>, createAnywayLines: number[]) =>
    apiPostFormData<PatientImportResult>('/patients/import', buildForm(file, mapping, createAnywayLines)),
};
