import { NextRequest, NextResponse } from 'next/server';
import { readSessionCookie } from '@/lib/auth/session-cookie';
import { clearSessionCookies, writeSessionCookies } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing NEXT_PUBLIC_API_URL
// is relative (`/api`) behind the same-origin front door and has no origin server-side.
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

// Force dynamic rendering to avoid build-time evaluation
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export async function GET(request: NextRequest) {
  // EXCHANGE the cookie's durable refresh token for a fresh short-lived access token
  // (security-hardening US-5). It used to return the cookie value verbatim, which is why the cookie's
  // HttpOnly flag bought no protection — the browser held the same 12-hour API credential the cookie did.
  //
  // The exchange runs server-side, so the refresh token itself never reaches browser JavaScript, and the API
  // re-checks live account state on every call: a password change, admin reset or deactivation stops renewal
  // immediately (AC-5.6).
    const sessionCredential = readSessionCookie((name) => request.cookies.get(name)?.value);
    if (!sessionCredential) {
      // French like every other `{ error }` in this file. The body is not currently read by `client.ts`'s
      // `fetchAccessToken` (it branches on the status alone), but the contract here is the app-wide canonical
      // `{ error }` shape, and every other refusal in this handler already answers in French — an English
      // island is one `getErrorMessage` away from a dentist reading « Not authenticated ».
      return NextResponse.json({ error: 'Session absente. Reconnectez-vous.' }, { status: 401 });
    }

    try {
      const res = await fetch(`${API_INTERNAL_URL}/auth/refresh`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...forwardedForHeader(request),
        },
        body: JSON.stringify({ refreshToken: sessionCredential }),
      });

      const data = await res.json().catch(() => null);

      if (res.ok && data?.isSuccess && data?.value?.accessToken) {
        // The response body carries only the access token, exactly as before — the durable credential must
        // never reach browser JavaScript, which is the whole point of the HttpOnly cookie (AC-5.5).
        const response = NextResponse.json({
          accessToken: data.value.accessToken,
          expiresAt: data.value.expiresAt,
        });

        // The half that used to be missing (AC-35): storing the freshly-minted credential is what makes the
        // session slide. An older API build sends no `refreshToken`, and then the cookie is left alone.
        const { refreshToken, refreshExpiresAt, mustChangePassword } = data.value;
        if (refreshToken) {
          writeSessionCookies(response, request, {
            credential: refreshToken,
            expiresAt: refreshExpiresAt,
            mustChangePassword: Boolean(mustChangePassword),
          });
        }

        return response;
      }

      // Below here the exchange did not produce a token. Only the API's own 401 means the credential
      // itself is dead; everything else is a refusal to answer *right now* and must leave the session
      // alone. Flattening them all to 401 is what turned a rate-limit blip into a destroyed session and
      // an endless bounce to a login page that was itself rate-limited.
      if (res.status === 429) {
        // Renewal is automatic and unattended, so the client's job is to back off, not to sign the user
        // out. Pass the status and Retry-After straight through.
        const retryAfter = res.headers.get('retry-after');
        return NextResponse.json(
          { error: data?.error || 'Trop de requêtes. Veuillez réessayer dans un instant.' },
          { status: 429, ...(retryAfter ? { headers: { 'Retry-After': retryAfter } } : {}) }
        );
      }

      if (res.status !== 401) {
        // 5xx, or a 2xx whose body we cannot use: the server is unwell, not the session. Same contract as
        // the unreachable case below, so the client retries instead of logging the user out.
        return NextResponse.json(
          { error: data?.error || 'Serveur indisponible. Veuillez réessayer.' },
          { status: 503 }
        );
      }

      // A genuine 401: expired, revoked, the account was deactivated, or the cookie was minted by a build
      // that stored the ACCESS token here (the API refuses that audience on exchange).
      //
      // Clear the cookie as we refuse it, or the app disagrees with itself and spins: /bff/auth/session
      // only base64-decodes this same value (no signature, expiry or audience check), so it keeps
      // reporting a signed-in user. ClinicGuard sees no access token and sends the browser to /login,
      // /login sees that "user" and pushes back to /, forever. Dropping the credential here is what
      // turns the loop into the re-login the API is actually asking for.
      const response = NextResponse.json(
        { error: data?.error || 'Session expirée. Veuillez vous reconnecter.' },
        { status: 401 }
      );
      clearSessionCookies(response);
      return response;
    } catch {
      // The API is unreachable — distinct from "session invalid", so the client can retry rather than
      // logging the user out over a transient blip (spec EC-10).
      return NextResponse.json(
        { error: 'Serveur injoignable. Veuillez réessayer.' },
        { status: 503 }
      );
    }
}
