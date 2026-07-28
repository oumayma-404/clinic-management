import { apiGet, apiPost, apiPut } from './client';

/**
 * The closed set of clinic roles, mirroring the backend `User.AssignableRoles`. Storage keys stay English —
 * they are what the authorization policies match on; the French labels are display-only.
 */
export const USER_ROLES = ['admin', 'doctor', 'secretary'] as const;
export type UserRole = (typeof USER_ROLES)[number];

/** French labels for the three roles. Display-only — never sent to the API. */
export const USER_ROLE_LABELS_FR: Record<UserRole, string> = {
  admin: 'Administrateur',
  doctor: 'Médecin',
  secretary: 'Secrétaire',
};

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

  /**
   * AC-P2.23: move a user between `admin` / `doctor` / `secretary`. The server validates the value against the
   * closed set, keeps email + full name, refuses a self-demotion that would leave the clinic with no active
   * admin, and bumps the target's token version so the new role applies on their next request.
   */
  setRole: async (id: string, role: UserRole): Promise<ClinicUserDto> => {
    return apiPut<ClinicUserDto>(`/users/${id}/role`, { role });
  },
};
