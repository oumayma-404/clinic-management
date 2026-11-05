import { NextRequest, NextResponse } from 'next/server';
import { writeSessionCookies } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';
import { DEMO_VISIT_COOKIE } from '@/lib/demo/demo-cookie';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

/**
 * « Entrer dans la démo » (`demo-cabinet` US-1): asks the API for a session of the visitor's own and stores it in
 * the same HttpOnly cookie a sign-in writes. The visitor's address travels on, so the per-address limit counts
 * visitors and not this server.
 */
export async function POST(request: NextRequest) {
  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/demo-session`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...forwardedForHeader(request) },
      body: '{}',
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess || !data?.value?.refreshToken) {
      const retryAfter = res.headers.get('retry-after');
      // 429 (too many visits) and 503 (no cabinet yet) keep their status and sentence; anything else is « try again ».
      const status = res.status === 429 || res.status === 503 || res.status === 404 ? res.status : 502;
      return NextResponse.json(
        {
          error:
            data?.error
            || (res.status === 429
              ? 'Trop de visites depuis cette adresse. Réessayez dans quelques minutes.'
              : 'La démo est momentanément indisponible. Réessayez dans un instant.'),
          ...(typeof data?.code === 'string' ? { code: data.code } : {}),
        },
        { status, ...(retryAfter ? { headers: { 'Retry-After': retryAfter } } : {}) },
      );
    }

    const { refreshToken, refreshExpiresAt } = data.value;
    const response = NextResponse.json({ ok: true });
    writeSessionCookies(response, request, {
      credential: refreshToken,
      expiresAt: refreshExpiresAt,
      mustChangePassword: false,
    });
    // Outlives the session so the next visit can be told « la démo a expiré » rather than greeted as new.
    response.cookies.set(DEMO_VISIT_COOKIE, '1', { path: '/', maxAge: 60 * 60 * 24, sameSite: 'lax' });
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre la démo. Vérifiez votre connexion et réessayez.' },
      { status: 502 },
    );
  }
}
