import { apiPost } from './client';

// Mirrors the backend BackupResultDto (US-8 / AC-8.2) — where the backup landed, its size, and when.
export interface BackupResultDto {
  destinationPath: string;
  sizeBytes: number;
  timestampUtc: string;
  // Set when the backup succeeded but could NOT be access-restricted — a removable or network destination,
  // where NTFS permissions cannot be relied on (security-hardening US-14 / AC-14.3). The backup is valid;
  // the copy of the patient records in it is simply readable by anyone who can reach that medium, so this
  // must be shown to the admin, not only logged.
  warning?: string | null;
}

export const backupApi = {
  // AC-8.1: admin-only one-click backup. An empty destination lets the server use its configured default.
  backupNow: async (destinationFolder?: string): Promise<BackupResultDto> => {
    return apiPost<BackupResultDto>('/backup', { destinationFolder: destinationFolder?.trim() || null });
  },
};
