// Shared, server-safe constants + auth-mode resolution for the dual-mode (Cloud/Local) auth.
// Imported by server code only (layout, middleware, route handlers) — reads AUTH_MODE from the env.

export const SESSION_COOKIE = 'local_session';

export type AuthMode = 'cloud' | 'local';

/** Resolves the configured auth mode from the Next server env (default Cloud). */
export function resolveAuthMode(): AuthMode {
  return (process.env.AUTH_MODE ?? '').trim().toLowerCase() === 'local' ? 'local' : 'cloud';
}
