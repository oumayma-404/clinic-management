import { NextRequest, NextResponse } from 'next/server';
import { writeSessionCookies } from '@/lib/auth/session-cookie';
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

    // Store the REFRESH token, never the access token (AC-5.5): the API rejects the refresh audience as a
    // bearer, so the cookie carries nothing that can call the API — it can only be exchanged.
    const { refreshToken, accessToken, expiresAt, refreshExpiresAt, mustChangePassword } = data.value;
    const mustChange = Boolean(mustChangePassword);
    const response = NextResponse.json({ mustChangePassword: mustChange });
    // The cookie's lifetime tracks the credential it holds, so only the access-token fallback uses `expiresAt`:
    // keying the 12 h session off the 30-minute one made the browser discard it after half an hour.
    writeSessionCookies(response, request, {
      credential: refreshToken || accessToken,
      expiresAt: refreshToken ? refreshExpiresAt : expiresAt,
      mustChangePassword: mustChange,
    });
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre le serveur de la clinique. Veuillez réessayer.' },
      { status: 502 }
    );
  }
}
