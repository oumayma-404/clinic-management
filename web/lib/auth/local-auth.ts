// Shared, server-safe constants + the auth-mode resolution. Imported by server code only (layout, route
// handlers) — reads AUTH_MODE from the env.
//
// ⚠️ One mode. Auth0 ('cloud') was retired with the CloudBrowser deployment kind, so this product issues
// every token it validates. The function is kept rather than deleted because AUTH_MODE is still set by both
// compose files and by the installer, and reading it is what keeps an unset or stale value from meaning
// something new; it now normalises anything to 'local' instead of quietly selecting a provider that is gone.

export const SESSION_COOKIE = 'local_session';

// Set at login when the account must change its password (admin reset — AC-5.2). While present,
// the middleware forces the user onto /change-password; the change-password route clears it.
export const MUST_CHANGE_COOKIE = 'local_must_change_password';

export type AuthMode = 'local';

/** The auth mode. One value — see the note above. */
export function resolveAuthMode(): AuthMode {
  return 'local';
}
