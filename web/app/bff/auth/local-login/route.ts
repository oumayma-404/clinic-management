import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, MUST_CHANGE_COOKIE } from '@/lib/auth/local-auth';

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
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email: body.email ?? '', password: body.password ?? '' }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess || !data?.value?.accessToken) {
      return NextResponse.json(
        { error: data?.error || 'Invalid email or password.' },
        { status: 401 }
      );
    }

    const { accessToken, expiresAt, mustChangePassword } = data.value;
    const mustChange = Boolean(mustChangePassword);
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
    const expires = expiresAt ? new Date(expiresAt) : undefined;
    const response = NextResponse.json({ mustChangePassword: mustChange });
    response.cookies.set(SESSION_COOKIE, accessToken, {
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
      { error: 'Cannot reach the clinic server. Please try again.' },
      { status: 502 }
    );
  }
}
