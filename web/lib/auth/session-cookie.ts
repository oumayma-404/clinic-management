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
 * The cookie names to **write** on a connection that is (or is not) secure
 * (`hosted-security-hardening` FR-1.7).
 *
 * ⚠️ **This is not a constant rename, and treating it as one reproduces the exact symptom FR-1.7 quotes.**
 * `__Host-` *requires* `Secure`, so a browser silently refuses to store a `__Host-` cookie sent over plain
 * HTTP — and a genuine plain-HTTP LAN deployment is a supported topology here. The name is therefore a
 * **function of `isSecure`**, not a literal: rename the constants alone and such an install writes one name and
 * reads the other, i.e. « a login that appears to succeed and immediately bounces, forever, with no message ».
 *
 * The prefix buys two things a plain name cannot: the browser enforces `Secure` **and** `Path=/` with **no
 * `Domain`**, so a sibling host on the same registrable domain cannot set a cookie this app would then read.
 */
export function sessionCookieNames(secure: boolean): { session: string; mustChange: string } {
  return secure
    ? { session: `__Host-${SESSION_COOKIE}`, mustChange: `__Host-${MUST_CHANGE_COOKIE}` }
    : { session: SESSION_COOKIE, mustChange: MUST_CHANGE_COOKIE };
}

/** Both spellings of each cookie, hardened first. */
const CANDIDATES = {
  session: [`__Host-${SESSION_COOKIE}`, SESSION_COOKIE],
  mustChange: [`__Host-${MUST_CHANGE_COOKIE}`, MUST_CHANGE_COOKIE],
} as const;

/**
 * Reads whichever spelling is present, hardened first.
 *
 * ⚠️ **Every reader goes through this rather than deriving the name itself**, for two reasons. A reader may
 * have no request to test (`app/change-password/page.tsx` is a server component reading `cookies()`), and on
 * the deploy that introduces the prefix a browser is still holding the **old** name — so a reader that resolved
 * only the new one would sign everybody out a second time, on top of the once FR-1.7 already accepts.
 */
export function readSessionCookie(get: (name: string) => string | undefined): string | undefined {
  return firstPresent(get, CANDIDATES.session);
}

export function readMustChangeCookie(get: (name: string) => string | undefined): string | undefined {
  return firstPresent(get, CANDIDATES.mustChange);
}

function firstPresent(
  get: (name: string) => string | undefined,
  names: readonly string[]
): string | undefined {
  for (const name of names) {
    const value = get(name);
    if (value) {
      return value;
    }
  }
  return undefined;
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
  const names = sessionCookieNames(secure);
  const expires = state.expiresAt ? new Date(state.expiresAt) : undefined;
  const attributes = {
    httpOnly: true,
    secure,
    // ⚠️ FR-1.7 — **`lax` is kept deliberately; `strict` was considered and rejected.** The spec allows either
    // provided the reason is written down, and this is it.
    //
    // `SameSite=Strict` withholds the cookie on *any* cross-site-initiated top-level navigation, redirect
    // chains included. The **Google Calendar OAuth return** is exactly that: the browser is sent from
    // accounts.google.com to `/api/googlecalendar/callback`, which then redirects on to `FrontendUrl`. Under
    // `strict` the whole chain arrives with no session cookie, `middleware.ts` — which gates on cookie
    // presence alone — sees an anonymous request and bounces the user to `/login`. Connecting a calendar would
    // sign the user out every time, and the symptom (« I connected Google and it logged me out ») points
    // nowhere near the cookie attribute that caused it.
    //
    // `lax` already blocks the case that matters: a cross-site **POST** and every subresource request. What
    // `strict` would add on top is protection against a cross-site *link* carrying the session, and that is
    // not worth breaking a shipped integration for — especially now the name is `__Host-`-prefixed, which is
    // the larger win and costs nothing.
    //
    // (The other flow named for this walk, the e-mailed signup verification link, is unaffected either way:
    // `/signup/verifier` is public and issues no session, so it needs no cookie to arrive.)
    sameSite: 'lax' as const,
    path: '/',
    expires,
  };

  response.cookies.set(names.session, state.credential, attributes);

  if (state.mustChangePassword) {
    response.cookies.set(names.mustChange, '1', attributes);
  } else {
    clearCookie(response, names.mustChange);
  }
}

/** Drops both cookies — sign-out, and a refusal the API has told us is the credential's own fault. */
export function clearSessionCookies(response: NextResponse): void {
  // ⚠️ BOTH spellings. Clearing only the hardened one would leave a pre-upgrade `local_session` in place, and
  // the reader above — which tries both — would go on finding it: a sign-out that does not sign out.
  for (const name of [...CANDIDATES.session, ...CANDIDATES.mustChange]) {
    clearCookie(response, name);
  }
}

/** The forced change is done: stop the middleware redirecting, without touching the session itself. */
export function clearMustChangeCookie(response: NextResponse): void {
  for (const name of CANDIDATES.mustChange) {
    clearCookie(response, name);
  }
}

// No `secure` on a deletion: the browser matches the cookie by name, domain and path only, and requiring
// Secure here would leave a non-Secure cookie in place on exactly the plain-HTTP deployment above.
function clearCookie(response: NextResponse, name: string): void {
  response.cookies.set(name, '', { httpOnly: true, path: '/', maxAge: 0 });
}
