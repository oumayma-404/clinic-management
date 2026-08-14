import { ARCHIVE_TIMEOUT_MS, apiGet, apiGetFile, apiPost, apiPostFormData, apiPut, type DownloadedFile } from './client';
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
  /**
   * **This deployment does not back itself up — its host does.** True on the two hosted kinds
   * (`DeploymentProfile.BacksUpItsOwnData`), where a `backup` sidecar dumps the database and the object store
   * off-server on a schedule and one database holds every cabinet, so an in-app `pg_dump` would be both weaker
   * than what already runs and a cross-tenant read. `backupNow` and `setSchedule` **404** there.
   *
   * ⚠️ It is a field rather than something the card infers from an empty page: « aucune sauvegarde » and « les
   * sauvegardes ne sont pas gérées ici » are the same picture and opposite facts, and the first is the one that
   * sends an owner hunting for a button to press. When true every other field is a neutral empty value and none
   * of them is a claim — in particular there is **no date**, because the sidecar runs in another container and
   * this application cannot observe it.
   */
  managedByHost: boolean;
}

/** The schedule as stored, echoed back so the screen renders what the server accepted. */
export interface BackupScheduleDto {
  enabled: boolean;
  hourLocal: number;
  retentionCount: number;
  staleAfterHours: number;
}

/**
 * What a restore did, per entity (`clinic-data-archive-and-restore`). Mirrors the backend
 * `ClinicArchiveRestoreReport`.
 *
 * ⚠️ **Three counts and not one**, and the middle one is what makes a restore safe to run twice. `restored` is a
 * row that was gone and is back; `alreadyPresent` is a row that was already there, identical, and was **not
 * touched** — so a second restore reports everything in that column and changes nothing. `conflicts` is a row that
 * exists and *differs*: it was **skipped, never overwritten**, because work done since the archive was taken must
 * survive putting the archive back.
 *
 * Keyed by entity rather than totalled, because « 3 conflits » says nothing an owner can act on while
 * « 3 conflits sur Patient » sends them to three patient records.
 */
export interface ClinicArchiveRestoreReport {
  archivedAtUtc: string;
  clinicId: string;
  restored: Record<string, number>;
  alreadyPresent: Record<string, number>;
  conflicts: Record<string, number>;
  /** Blobs written back at their original storage keys. */
  blobsRestored: number;
  /** What could not be restored, in French. Empty is the ordinary case. */
  warnings: string[];
  /**
   * The French name of each entity the three dictionaries are keyed on, mapped server-side beside `AuditLabels`.
   *
   * The keys stay the English CLR names — the standing convention — because they are the wire format, and the
   * panel prints the label. Before it, a French cabinet owner read « PatientMedicalHistory · 12 remis » at the
   * moment they were most anxious, and the list sorted by an identifier they could not predict.
   */
  entityLabels: Record<string, string>;
  totalRestored: number;
  totalAlreadyPresent: number;
  totalConflicts: number;
}

/**
 * The refusal codes the archive endpoints return beside their French sentence.
 *
 * ⚠️ **Branch on these, never on the message.** The server owns the wording and rewording it must not change
 * behaviour — the `Contains("déjà facturée")` defect this repo deleted in `adoption-gaps-remediation`.
 */
export const ARCHIVE_ERROR_CODES = {
  invalid: 'archive_invalid',
  clinicMismatch: 'archive_clinic_mismatch',
  schemaUnsupported: 'archive_schema_unsupported',
} as const;

/**
 * One retained recovery point (`clinic-recovery-points`). Mirrors the backend `RecoveryPointDto`.
 *
 * ⚠️ **`carriesFiles` is a field and not something to infer from a zero.** A scheduled point is rows-only by design,
 * while a full archive whose every blob failed to read also restores zero files — opposite facts with the same
 * picture, and only the second is a reason to go looking at the object store.
 */
export interface RecoveryPointDto {
  id: string;
  startedAt: string;
  completedAt: string | null;
  /** `Running` | `Succeeded` | `Failed` — the backend `BackupOutcome`. */
  outcome: string;
  /**
   * Whether this point can actually be restored from. The server's own answer (a success that still names an
   * object), never re-derived here from `outcome`: a `Running` row left by a crash and a success whose object was
   * pruned are both unusable, and only the server knows the second.
   */
  isRestorable: boolean;
  carriesFiles: boolean;
  sizeBytes: number | null;
  tableCount: number | null;
  rowCount: number | null;
  error: string | null;
}

/**
 * What the cabinet can restore from, and the state of the copy it holds itself (`clinic-recovery-points`).
 *
 * ⚠️ `archiveStaleAfterDays` is **served, not restated in the browser**, so the card's wording and the bell's alert
 * cannot disagree about when a copy has gone stale.
 */
export interface RecoveryPointsDto {
  points: RecoveryPointDto[];
  /** When an archive last *reached* somebody. `null` on a cabinet that has never taken one — say « jamais ». */
  lastArchiveDownloadedAtUtc: string | null;
  archiveStaleAfterDays: number;
  retentionCount: number;
}

export const backupApi = {
  /**
   * The cabinet's retained recovery points, newest first, plus its own off-server-copy state.
   *
   * Available on **every** deployment, like `downloadArchive` and unlike `backupNow`/`setSchedule`: a recovery point
   * is a tenant-filtered per-clinic archive, not a `pg_dump`.
   */
  recoveryPoints: async (): Promise<RecoveryPointsDto> => {
    return apiGet<RecoveryPointsDto>('/backup/recovery-points');
  },

  /**
   * Restores from one retained point.
   *
   * ⚠️ Carries the **same** step-up action as `restoreArchive` (`restore-clinic-archive`) — it is the same operation
   * on the same records — and the token is single-use, so each restore needs its own confirmation.
   */
  restoreFromRecoveryPoint: async (
    recoveryPointId: string, stepUpToken: string
  ): Promise<ClinicArchiveRestoreReport> => {
    return apiPost<ClinicArchiveRestoreReport>(
      `/backup/recovery-points/${recoveryPointId}/restore`, {}, undefined, stepUpToken);
  },

  /**
   * Downloads the cabinet's own archive (`GET /api/backup/archive`) — every record it holds plus the blobs
   * behind them, tenant-filtered, as one `.zip`.
   *
   * ⚠️ **Available on every deployment**, unlike `backupNow`/`setSchedule`, which 404 where the host owns the
   * backups: those run `pg_dump`, which has no tenant predicate and on a shared database would hand one practice
   * all the others. This one goes through the same tenant filter as every CSV export.
   *
   * ⚠️ Returns the `Blob` **and the server's own file name**, which the server composes from the cabinet's name
   * and the clinic-local day (`ClinicArchiveFormat.FileName`). Rebuilding it here threw that away and gave
   * *every* cabinet's archive the same name — defeating the exact scenario both restore handlers cite as the
   * reason the clinic-id check exists: two files in one Downloads folder that differ only by a date, now
   * differing only by the browser's `(1)` suffix. `filenameFromDisposition` is the single parser, and
   * `lib/api/export.ts` has used it for nine same-origin exports all along.
   *
   * ⚠️ Its own deadline, not the transfer one: the whole archive is built before a byte is sent, so three
   * minutes was a limit on the *server's work* rather than on the download.
   */
  downloadArchive: async (stepUpToken: string): Promise<DownloadedFile> =>
    apiGetFile('/backup/archive', undefined, undefined, ARCHIVE_TIMEOUT_MS, stepUpToken),

  /**
   * Restores an archive into this cabinet (`POST /api/backup/archive/restore`).
   *
   * ⚠️ **Additive**: missing rows are re-inserted with their original ids, rows still present are left untouched,
   * and a row that exists but differs is skipped and counted apart. Nothing is ever overwritten or deleted.
   */
  restoreArchive: async (archive: File, stepUpToken: string): Promise<ClinicArchiveRestoreReport> => {
    const form = new FormData();
    form.append('archive', archive);

    // ⚠️ The archive deadline, not the transfer one. The confirmation this call runs behind says the operation
    // can take several minutes on a full cabinet, so the three-minute default aborted the client while the
    // server kept committing — the user got the network wording and lost the per-entity report.
    return apiPostFormData<ClinicArchiveRestoreReport>(
      '/backup/archive/restore', form, undefined, ARCHIVE_TIMEOUT_MS, stepUpToken);
  },

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
