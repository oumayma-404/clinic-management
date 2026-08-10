import { cookies } from "next/headers";

/**
 * The console's session cookie — the **single writer**, as `web/lib/auth/session-cookie.ts` is for the clinic
 * app and for the same reason: two routes each setting their own `Secure` or `sameSite` is how a stored cookie
 * gets replaced by one the browser drops, which ends the session with nothing anywhere saying so.
 *
 * ⚠️ **HttpOnly.** The console token is the credential for a cross-cabinet read surface, so it is never handed
 * to browser JavaScript: the pages call their own BFF routes, which attach it server-side. That is the same
 * shape the clinic app uses, and here it matters more — there is no short-lived access token in front of it.
 */
export const SESSION_COOKIE = "console_session";

/**
 * `Secure` is unconditional. The console is only ever reached over the private HTTPS site
 * (`deploy/Caddyfile`, `127.0.0.1:9443` through a tunnel), so there is no plain-HTTP deployment for a
 * conditional flag to accommodate — and making it conditional is how a production cookie ends up sent in clear.
 */
const COOKIE_OPTIONS = {
  httpOnly: true,
  secure: true,
  sameSite: "strict" as const,
  path: "/",
};

export async function readSessionToken(): Promise<string | null> {
  const store = await cookies();
  return store.get(SESSION_COOKIE)?.value ?? null;
}

export async function writeSessionToken(token: string, expiresAt: string): Promise<void> {
  const store = await cookies();
  const expires = new Date(expiresAt);

  store.set(SESSION_COOKIE, token, {
    ...COOKIE_OPTIONS,
    // The cookie dies with the token it carries, so the browser stops sending a credential the API would
    // refuse anyway — and « signed out » and « token expired » become one state rather than two.
    expires: Number.isNaN(expires.getTime()) ? undefined : expires,
  });
}

export async function clearSessionToken(): Promise<void> {
  const store = await cookies();
  store.set(SESSION_COOKIE, "", { ...COOKIE_OPTIONS, maxAge: 0 });
}
