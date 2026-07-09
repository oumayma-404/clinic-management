import { apiPost } from './client';

// Mirrors the backend BackupResultDto (US-8 / AC-8.2) — where the backup landed, its size, and when.
export interface BackupResultDto {
  destinationPath: string;
  sizeBytes: number;
  timestampUtc: string;
}

export const backupApi = {
  // AC-8.1: admin-only one-click backup. An empty destination lets the server use its configured default.
  backupNow: async (destinationFolder?: string): Promise<BackupResultDto> => {
    return apiPost<BackupResultDto>('/backup', { destinationFolder: destinationFolder?.trim() || null });
  },
};
