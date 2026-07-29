import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, MUST_CHANGE_COOKIE } from '@/lib/auth/local-auth';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing
// NEXT_PUBLIC_API_URL is relative (`/api`) behind the same-origin front door and has no origin
// server-side, so use the server-only API_INTERNAL_URL (default the co-located API over loopback).
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

// Local-mode login: posts credentials to the .NET API, then stores the returned
// JWT in an HttpOnly session cookie that the token route reads back.
export async function POST(request: NextRequest) {
  let body: { email?: string; password?: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }

  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/login`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...forwardedForHeader(request),
      },
      body: JSON.stringify({ email: body.email ?? '', password: body.password ?? '' }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess || !data?.value?.accessToken) {
      // A rate-limit refusal is NOT a credential failure — pass it through as 429 with its Retry-After
      // instead of flattening it to 401, so the UI can tell "wrong password" from "too many attempts"
      // (security-hardening AC-4.5). Everything else stays 401 so the endpoint never discloses more.
      if (res.status === 429) {
        const retryAfter = res.headers.get('retry-after');
        return NextResponse.json(
          { error: data?.error || 'Trop de tentatives. Veuillez réessayer plus tard.' },
          { status: 429, ...(retryAfter ? { headers: { 'Retry-After': retryAfter } } : {}) }
        );
      }

      return NextResponse.json(
        { error: data?.error || 'Invalid email or password.' },
        { status: 401 }
      );
    }

    // Store the REFRESH token in the cookie, never the access token (security-hardening AC-5.5). The API
    // rejects the refresh audience as a bearer token, so the cookie no longer carries a working API
    // credential — it can only be exchanged, and the exchange re-checks live account state. Older builds
    // stored the access token here, which is why the cookie value is still a decodable JWT: /bff/auth/session
    // reads its claims for the header identity (AC-5.12).
    const { refreshToken, accessToken, expiresAt, refreshExpiresAt, mustChangePassword } = data.value;
    const sessionCredential = refreshToken || accessToken;
    const mustChange = Boolean(mustChangePassword);
    // The cookie's lifetime has to track the credential it actually holds. That is normally the REFRESH
    // token (12h), so use its own expiry: keying off `expiresAt` — the 30-minute ACCESS token's — made the
    // browser discard a still-valid session after half an hour, whatever the user was doing. Only in the
    // access-token fallback above does `expiresAt` describe the cookie. If the expiry is unknown (an older
    // API build that doesn't send `refreshExpiresAt`), leave it a browser-session cookie rather than guess —
    // the API enforces the true lifetime on every exchange regardless.
    const credentialExpiresAt = refreshToken ? refreshExpiresAt : expiresAt;
    // The browser now reaches the app over the HTTPS front door (Phase 5 S3), but this handler runs on
    // the Node server that sits behind it on a plain-HTTP loopback hop — so `request.nextUrl.protocol`
    // is `http:` here and would wrongly drop the Secure flag. Keying `secure` off NODE_ENV instead would
    // set it on any production build, including genuine plain-HTTP dev, and the browser would silently
    // drop the cookie over HTTP, breaking login. So derive it from the request scheme by default, and set
    // AUTH_COOKIE_SECURE=true for deployments behind a TLS-terminating proxy — which the server installer
    // does for the front-door topology, so the session cookie is Secure on the HTTPS LAN deployment.
    const secure = process.env.AUTH_COOKIE_SECURE
      ? process.env.AUTH_COOKIE_SECURE === 'true'
      : request.nextUrl.protocol === 'https:';
    const expires = credentialExpiresAt ? new Date(credentialExpiresAt) : undefined;
    const response = NextResponse.json({ mustChangePassword: mustChange });
    response.cookies.set(SESSION_COOKIE, sessionCredential, {
      httpOnly: true,
      secure,
      sameSite: 'lax',
      path: '/',
      expires,
    });
    // AC-5.2: while this flag is set the middleware forces the user onto /change-password.
    if (mustChange) {
      response.cookies.set(MUST_CHANGE_COOKIE, '1', {
        httpOnly: true,
        secure,
        sameSite: 'lax',
        path: '/',
        expires,
      });
    }
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre le serveur de la clinique. Veuillez réessayer.' },
      { status: 502 }
    );
  }
}
