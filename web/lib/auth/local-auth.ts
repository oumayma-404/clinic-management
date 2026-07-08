// Shared, server-safe constants + auth-mode resolution for the dual-mode (Cloud/Local) auth.
// Imported by server code only (layout, middleware, route handlers) — reads AUTH_MODE from the env.

export const SESSION_COOKIE = 'local_session';

// Set at login when the account must change its password (admin reset — AC-5.2). While present,
// the middleware forces the user onto /change-password; the change-password route clears it.
export const MUST_CHANGE_COOKIE = 'local_must_change_password';

export type AuthMode = 'cloud' | 'local';

/** Resolves the configured auth mode from the Next server env (default Cloud). */
export function resolveAuthMode(): AuthMode {
  return (process.env.AUTH_MODE ?? '').trim().toLowerCase() === 'local' ? 'local' : 'cloud';
}
