import { apiGet, apiPost, apiPut } from './client';

// Mirrors the backend ClinicUserDto (admin user-management screen — AC-5.1).
export interface ClinicUserDto {
  id: string;
  clinicId: string;
  role: string;
  email?: string;
  fullName?: string;
  isActive: boolean;
  mustChangePassword: boolean;
  lastLoginAt?: string;
  createdAt: string;
}

// Mirrors the backend ResetPasswordResultDto (AC-5.2) — temp password returned once.
export interface ResetPasswordResultDto {
  userId: string;
  temporaryPassword: string;
}

export const usersApi = {
  // AC-5.1: list the clinic's users with account status (admin-only endpoint).
  list: async (): Promise<ClinicUserDto[]> => {
    return apiGet<ClinicUserDto[]>('/users');
  },

  // AC-5.2: reset a user's password → temporary password returned once for the admin to relay.
  resetPassword: async (id: string): Promise<ResetPasswordResultDto> => {
    return apiPost<ResetPasswordResultDto>(`/users/${id}/reset-password`, {});
  },

  // AC-5.3: deactivate / reactivate a user (historical records retained).
  setStatus: async (id: string, isActive: boolean): Promise<ClinicUserDto> => {
    return apiPut<ClinicUserDto>(`/users/${id}/status`, { isActive });
  },
};
