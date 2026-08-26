import { apiGet, apiPost, apiPut } from './client';
import { unwrapPaged, type PagedResponse, type PageParams } from './paging';

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
  /**
   * Never been able to log in — a self-registration waiting for an admin's approval, as opposed to an account
   * switched off after use. Both are `!isActive`; the badge wording differs because only one of them is somebody's
   * first day.
   */
  isPendingActivation: boolean;
  mustChangePassword: boolean;
  lastLoginAt?: string;
  /** Optimistic-concurrency token — see `PatientDto.version`. Round-trip it on the role / activation actions. */
  version: number;
  createdAt: string;
}

/**
 * Mirrors the backend `ClinicUsersPageDto` — a page of staff plus the clinic-wide pending count.
 *
 * Structurally a `PagedResponse<ClinicUserDto>` with one extra field, so the shared pager consumes it unchanged.
 * The count is **not** narrowed by the search term: it is the figure above the table, and an admin filtering for
 * one name must still see that somebody else is waiting to be let in.
 */
export interface ClinicUsersPageDto extends PagedResponse<ClinicUserDto> {
  pendingActivationCount: number;
}

// Mirrors the backend ResetPasswordResultDto (AC-5.2) — temp password returned once.
export interface ResetPasswordResultDto {
  userId: string;
  temporaryPassword: string;
}

/**
 * Mirrors the backend `CreatedClinicUserDto` (US-3) — an admin-created account plus the one-time password.
 * Deliberately not `ClinicUserDto & { temporaryPassword }`: the password is returned exactly once and must not
 * travel on the type the users list is built from.
 */
export interface CreatedClinicUserDto {
  userId: string;
  email?: string;
  fullName?: string;
  role: string;
  temporaryPassword: string;
}

export const usersApi = {
  // AC-5.1: list the clinic's users with account status (admin-only endpoint).
  list: async (): Promise<ClinicUserDto[]> => {
    return unwrapPaged(await apiGet<PagedResponse<ClinicUserDto>>('/users'));
  },

  /**
   * One page of staff. `search` matches full name / email server-side over the whole clinic; the response also
   * carries `pendingActivationCount` over the whole clinic (I5).
   */
  listPaged: async (params: PageParams): Promise<ClinicUsersPageDto> =>
    apiGet<ClinicUsersPageDto>('/users', params),

  /**
   * US-3: create a colleague's account. The server mints the password — there is no field for one, so an admin
   * cannot choose a weak shared one, and the account is forced to replace it at first login.
   *
   * The clinic comes from the caller's own record, never the request: an admin creates staff for their own
   * practice and nowhere else. 404s in Cloud, where Auth0 owns identities.
   */
  /**
   * Creates a colleague's account and returns the one-time password once.
   *
   * ⚠️ `doctorInfo` is **required by the server for the `doctor` role** and ignored for the other two — the same
   * shape `clinicsApi.join` takes. Without it the account gets a `User` row and no `Doctor`, so the person is absent
   * from every praticien list, their money is unattributed, and their ordonnances print with no cachet or n° d'ordre.
   */
  create: async (data: {
    email: string;
    fullName: string;
    role: UserRole;
    doctorInfo?: { firstName: string; lastName: string; specialty: string; phone?: string };
  }): Promise<CreatedClinicUserDto> => {
    return apiPost<CreatedClinicUserDto>('/users', data);
  },

  // AC-5.2: reset a user's password → temporary password returned once for the admin to relay.
  resetPassword: async (id: string): Promise<ResetPasswordResultDto> => {
    return apiPost<ResetPasswordResultDto>(`/users/${id}/reset-password`, {});
  },

  // AC-5.3: deactivate / reactivate a user (historical records retained).
  setStatus: async (id: string, isActive: boolean, version?: number): Promise<ClinicUserDto> => {
    return apiPut<ClinicUserDto>(`/users/${id}/status`, { isActive, version });
  },

  /**
   * AC-P2.23: move a user between `admin` / `doctor` / `secretary`. The server validates the value against the
   * closed set, keeps email + full name, refuses a self-demotion that would leave the clinic with no active
   * admin, and bumps the target's token version so the new role applies on their next request.
   */
  setRole: async (id: string, role: UserRole, version?: number): Promise<ClinicUserDto> => {
    return apiPut<ClinicUserDto>(`/users/${id}/role`, { role, version });
  },
};
