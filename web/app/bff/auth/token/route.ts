import { auth0 } from '@/lib/auth0';
import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, resolveAuthMode } from '@/lib/auth/local-auth';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing NEXT_PUBLIC_API_URL
// is relative (`/api`) behind the same-origin front door and has no origin server-side.
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

// Force dynamic rendering to avoid build-time evaluation
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export async function GET(request: NextRequest) {
  // Local mode: EXCHANGE the cookie's durable refresh token for a fresh short-lived access token
  // (security-hardening US-5). It used to return the cookie value verbatim, which is why the cookie's
  // HttpOnly flag bought no protection — the browser held the same 12-hour API credential the cookie did.
  //
  // The exchange runs server-side, so the refresh token itself never reaches browser JavaScript, and the API
  // re-checks live account state on every call: a password change, admin reset or deactivation stops renewal
  // immediately (AC-5.6).
  if (resolveAuthMode() === 'local') {
    const sessionCredential = request.cookies.get(SESSION_COOKIE)?.value;
    if (!sessionCredential) {
      return NextResponse.json({ error: 'Not authenticated' }, { status: 401 });
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

      if (!res.ok || !data?.isSuccess || !data?.value?.accessToken) {
        // The session is gone (expired, revoked, or the account was deactivated). 401 tells the client to
        // stop retrying and send the user to sign in again.
        return NextResponse.json(
          { error: data?.error || 'Session expirée. Veuillez vous reconnecter.' },
          { status: 401 }
        );
      }

      return NextResponse.json({
        accessToken: data.value.accessToken,
        expiresAt: data.value.expiresAt,
      });
    } catch {
      // The API is unreachable — distinct from "session invalid", so the client can retry rather than
      // logging the user out over a transient blip (spec EC-10).
      return NextResponse.json(
        { error: 'Serveur injoignable. Veuillez réessayer.' },
        { status: 503 }
      );
    }
  }

  // Cloud mode: return the Auth0 access token (unchanged).
  try {
    const session = await auth0.getSession(request);
    if (!session) {
      return NextResponse.json({ error: 'Not authenticated' }, { status: 401 });
    }
    
    // Get access token - In Auth0 v4 App Router, getAccessToken() can be called without parameters
    // It automatically uses the request context (cookies/headers) from the route handler
    // GetAccessTokenOptions only supports: refresh, scope, audience
    const tokenResult = await auth0.getAccessToken();
    
    if (!tokenResult || !tokenResult.token) {
      return NextResponse.json({ error: 'No access token available' }, { status: 401 });
    }
    
    return NextResponse.json({ accessToken: tokenResult.token });
  } catch (error) {
    console.error('Error getting access token:', error);
    return NextResponse.json({ error: 'Failed to get access token' }, { status: 500 });
  }
}

