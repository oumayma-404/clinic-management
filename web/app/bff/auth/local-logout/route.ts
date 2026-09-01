import { NextRequest, NextResponse } from 'next/server';
import { clearSessionCookies, readSessionCookie } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing NEXT_PUBLIC_API_URL
// is relative (`/api`) behind the same-origin front door and has no origin server-side.
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

/**
 * « Se déconnecter » — revokes the session on the server, then clears the cookies.
 *
 * ⚠️ **This used to clear the cookies and stop.** There was no revoke endpoint on the API at all, so the refresh
 * credential the cookie held stayed valid for its full **12 hours** and kept rotating itself. Signing out removed
 * it from *this browser* and revoked nothing — which on a shared reception PC is precisely the machine where
 * somebody signs out because they are walking away from it.
 *
 * ⚠️ **The credential is read here and sent server-side**, never returned to the page: it is HttpOnly for exactly
 * that reason, and `bff/auth/token` already establishes the pattern.
 *
 * ⚠️ **The cookies are cleared whatever the API answers.** A user pressing « Se déconnecter » must end up signed
 * out of this browser even if the server is unreachable; leaving them apparently signed in because a revoke call
 * failed would be the worse of the two outcomes, and the API's own endpoint is idempotent so a retry costs
 * nothing. The configured server address is untouched (AC-3.6).
 */
export async function POST(request: NextRequest) {
  const sessionCredential = readSessionCookie((name) => request.cookies.get(name)?.value);

  /*
   * Why the reason travels: only the client knows whether a person pressed « Se déconnecter » or whether the
   * inactivity limit ran out, and both end the same session through this same route. Both used to be stamped
   * « Déconnexion demandée par l'utilisateur », so the session table could not tell them apart and « how often
   * is the timeout actually signing people out? » had to be answered by reading this file instead of the
   * practice's own records.
   *
   * ⚠️ A missing or unparseable body is not an error. Sign-out fires while the session is already being torn
   * down — `session.tsx` sends no body on some paths and `navigator.sendBeacon` may send none at all — and a 400
   * here would put a French failure toast on a successful sign-out.
   */
  const reason = await request
    .json()
    .then((body: { reason?: unknown }) => (typeof body?.reason === 'string' ? body.reason : undefined))
    .catch(() => undefined);

  if (sessionCredential) {
    try {
      await fetch(`${API_INTERNAL_URL}/auth/logout`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...forwardedForHeader(request),
        },
        // The API maps this onto a closed set and refuses nothing — an unrecognised value is recorded as a
        // deliberate sign-out rather than rejected.
        body: JSON.stringify({ refreshToken: sessionCredential, ...(reason ? { reason } : {}) }),
      });
    } catch {
      // Deliberately swallowed — see the note above on why the cookies go regardless.
    }
  }

  const response = NextResponse.json({ ok: true });
  clearSessionCookies(response);
  return response;
}
