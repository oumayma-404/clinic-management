import { apiGet, apiPost, apiPut } from './client';
import type { PagedResponse } from './paging';

// Mirrors the backend BackupResultDto (US-8 / AC-8.2) — where the backup landed, its size, and when.
export interface BackupResultDto {
  destinationPath: string;
  sizeBytes: number;
  timestampUtc: string;
  /**
   * Objects found by `pg_restore --list` (L4c). `pg_dump` exiting 0 is not proof a dump is restorable, so a
   * backup is only reported successful once its table of contents reads back non-empty. The **count** is
   * surfaced rather than a tick because « 3 objets » where the schema has thirty-eight tables is the one shape
   * of disaster a tick cannot express.
   */
  verifiedObjectCount: number;
  // Set when the backup succeeded but could NOT be access-restricted — a removable or network destination,
  // where NTFS permissions cannot be relied on (security-hardening US-14 / AC-14.3) — or when it landed on the
  // same volume as the live data (L4b). The backup is valid; this is about where it is and who can read it.
  warning?: string | null;
}

export type BackupRunOutcome = 'running' | 'succeeded' | 'failed';

/** One recorded backup attempt (L4d). Mirrors the backend `BackupRunDto`. */
export interface BackupRunDto {
  id: string;
  startedAt: string;
  completedAt: string | null;
  outcome: BackupRunOutcome;
  /** `scheduled` | `manual` — « personne n'a cliqué » and « le programme n'a pas tourné » are different problems. */
  trigger: 'scheduled' | 'manual';
  destinationPath: string | null;
  sizeBytes: number | null;
  verifiedObjectCount: number | null;
  error: string | null;
}

/**
 * The « Sauvegarde » panel in one read (L4d).
 *
 * ⚠️ `lastSuccessAt` is a *different question* from « the newest row »: a week of nightly failures leaves the
 * newest row failed and the last success seven days back, and only having both distinguishes « nobody has backed
 * up » from « it has been trying and failing ». Before this read existed, the result of a backup lived in a React
 * `useState` and the question had no answer anywhere in the product.
 */
export interface BackupHistoryDto {
  page: PagedResponse<BackupRunDto>;
  lastSuccessAt: string | null;
  lastSuccessSizeBytes: number | null;
  /** Where a backup with no explicit destination goes — resolved by the server, never re-derived here. */
  defaultDestination: string;
  staleAfterHours: number;
  backupEnabled: boolean;
  backupHourLocal: number;
  retentionCount: number;
}

/** The schedule as stored, echoed back so the screen renders what the server accepted. */
export interface BackupScheduleDto {
  enabled: boolean;
  hourLocal: number;
  retentionCount: number;
  staleAfterHours: number;
}

export const backupApi = {
  // AC-8.1: admin-only one-click backup. An empty destination lets the server use its configured default.
  backupNow: async (destinationFolder?: string): Promise<BackupResultDto> => {
    return apiPost<BackupResultDto>('/backup', { destinationFolder: destinationFolder?.trim() || null });
  },

  /** L4d — the recorded attempts plus the headline figures. Admin-only, always paged. */
  history: async (params: { page?: number; pageSize?: number } = {}): Promise<BackupHistoryDto> => {
    return apiGet<BackupHistoryDto>('/backup/history', params);
  },

  /** L4a — the unattended schedule. The caller the four new columns ship with. */
  setSchedule: async (schedule: BackupScheduleDto): Promise<BackupScheduleDto> => {
    return apiPut<BackupScheduleDto>('/backup/schedule', schedule);
  },
};
