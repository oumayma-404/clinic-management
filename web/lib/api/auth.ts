import { apiGet } from './client';

/**
 * What `GET /api/auth/mode` answers about this deployment. Anonymous — it is read before anyone has a session.
 */
export interface AuthModeDto {
  /** `local` when the product issues its own email+password tokens, `cloud` when Auth0 does. */
  mode: 'local' | 'cloud';
  /**
   * Whether staff may mint their own account with the clinic's join code (`POST /api/auth/register`).
   *
   * ⚠️ **Not derivable from `mode`, which is exactly why the server answers it.** The browser learns the mode from
   * the Next server's `AUTH_MODE`, and that reads `local` on a clinic's own PC *and* on the hosted multi-tenant
   * backend — but only the first is a LAN, where reaching the endpoint at all means being inside the surgery. On
   * the internet the six-character code is a password everyone who ever worked there knows, so the hosted profile
   * closes self-registration and an admin creates the accounts instead.
   */
  selfRegistrationEnabled: boolean;
}

export const authApi = {
  /**
   * Reads the deployment's auth capabilities. `null` skips the bearer token: this is the one call made before a
   * session exists, and attaching a stale one would fail the request rather than the auth.
   */
  getMode: async (): Promise<AuthModeDto> => apiGet<AuthModeDto>('/auth/mode', undefined, null),
};
