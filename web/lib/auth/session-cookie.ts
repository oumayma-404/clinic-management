// Server-only: the single place the Local session cookies are written and cleared. Two routes write them —
// `/bff/auth/local-login` at sign-in and `/bff/auth/token` on every refresh exchange (AC-35) — and a refresh
// that set `Secure` differently from login would replace a stored cookie with one the browser drops.

import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, MUST_CHANGE_COOKIE } from './local-auth';

/** What the API's login / refresh response says the browser should now be holding. */
export interface SessionCookieState {
  /**
   * The durable credential — the refresh token, never the access token (security-hardening AC-5.5). It is a
   * decodable JWT, which `/bff/auth/session` relies on: it reads these claims for the header identity (AC-5.12).
   */
  credential: string;
  /** That credential's own expiry, ISO-8601. Absent ⇒ a browser-session cookie rather than a guessed date. */
  expiresAt?: string | null;
  /** Whether the account still owes a forced password change, as the server reports it on this exchange. */
  mustChangePassword: boolean;
}

/**
 * Whether the session cookie carries `Secure`.
 *
 * The browser reaches the app over the HTTPS front door (Phase 5 S3), but these handlers run on the Node
 * server behind it on a plain-HTTP loopback hop — so `request.nextUrl.protocol` is `http:` here and would
 * wrongly drop the flag. Keying it off `NODE_ENV` instead would set it on any production build, including a
 * genuine plain-HTTP LAN deployment, and the browser would silently refuse to store the cookie, bouncing the
 * user back to `/login` with nothing to diagnose (LEARNINGS: « `secure` cookie keyed on `NODE_ENV` breaks
 * login over plain HTTP »). So: the explicit flag wins, and the request scheme is the fallback —
 * `AUTH_COOKIE_SECURE=true` is what the server installer writes for the front-door topology.
 */
function isSecure(request: NextRequest): boolean {
  return process.env.AUTH_COOKIE_SECURE
    ? process.env.AUTH_COOKIE_SECURE === 'true'
    : request.nextUrl.protocol === 'https:';
}

/**
 * Writes both session cookies from one server answer.
 *
 * They are written together deliberately. `local_must_change_password` used to be set only at login, with the
 * session's expiry — so once the session started sliding it would outlive the flag, and a user who owes a
 * password change would find the middleware had stopped redirecting them (the API's own
 * `LocalAuthEnforcementMiddleware` still refuses every other call, so the app would look usable and be dead).
 * Re-deriving it from the server's answer on every exchange also means it is *cleared* once the change is done,
 * rather than only by the route that happened to perform it.
 */
export function writeSessionCookies(
  response: NextResponse,
  request: NextRequest,
  state: SessionCookieState
): void {
  const secure = isSecure(request);
  const expires = state.expiresAt ? new Date(state.expiresAt) : undefined;
  const attributes = { httpOnly: true, secure, sameSite: 'lax' as const, path: '/', expires };

  response.cookies.set(SESSION_COOKIE, state.credential, attributes);

  if (state.mustChangePassword) {
    response.cookies.set(MUST_CHANGE_COOKIE, '1', attributes);
  } else {
    clearCookie(response, MUST_CHANGE_COOKIE);
  }
}

/** Drops both cookies — sign-out, and a refusal the API has told us is the credential's own fault. */
export function clearSessionCookies(response: NextResponse): void {
  clearCookie(response, SESSION_COOKIE);
  clearCookie(response, MUST_CHANGE_COOKIE);
}

/** The forced change is done: stop the middleware redirecting, without touching the session itself. */
export function clearMustChangeCookie(response: NextResponse): void {
  clearCookie(response, MUST_CHANGE_COOKIE);
}

// No `secure` on a deletion: the browser matches the cookie by name, domain and path only, and requiring
// Secure here would leave a non-Secure cookie in place on exactly the plain-HTTP deployment above.
function clearCookie(response: NextResponse, name: string): void {
  response.cookies.set(name, '', { httpOnly: true, path: '/', maxAge: 0 });
}
