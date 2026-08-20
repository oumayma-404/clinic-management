import { NextRequest, NextResponse } from 'next/server';
import { writeSessionCookies } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

/**
 * Signing in with a single-use recovery code (`hosted-security-hardening` FR-1.4).
 *
 * ⚠️ **This one DOES write the session cookies**, unlike its enrolment sibling: it is a sign-in, and the server
 * answers with the same `LoginResultDto` the ordinary ladder does. It therefore goes through
 * `writeSessionCookies` — the single writer — rather than setting either cookie by name, so the `Secure` rule
 * and the forced-password-change flag cannot diverge from the login path's.
 */
export async function POST(request: NextRequest) {
  let body: { email?: string; password?: string; recoveryCode?: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: 'Requête invalide.' }, { status: 400 });
  }

  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/recovery`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...forwardedForHeader(request),
      },
      body: JSON.stringify({
        email: body.email ?? '',
        password: body.password ?? '',
        recoveryCode: body.recoveryCode ?? '',
      }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess || !data?.value?.accessToken) {
      if (res.status === 429) {
        const retryAfter = res.headers.get('retry-after');
        return NextResponse.json(
          { error: data?.error || 'Trop de tentatives. Veuillez réessayer plus tard.' },
          { status: 429, ...(retryAfter ? { headers: { 'Retry-After': retryAfter } } : {}) }
        );
      }

      return NextResponse.json(
        {
          error: data?.error || 'Code de récupération invalide.',
          ...(typeof data?.code === 'string' ? { code: data.code } : {}),
        },
        { status: res.status }
      );
    }

    const {
      refreshToken,
      accessToken,
      expiresAt,
      refreshExpiresAt,
      mustChangePassword,
      mayReplaceSecondFactor,
    } = data.value;
    const mustChange = Boolean(mustChangePassword);

    // ⚠️ Forwarded to the browser, and it is the only signal the screen gets. Redeeming a code proves the
    // owner is present, so the server has opened a short window in which the factor may be moved to a new
    // phone — but the account is still bound to the old one, and nothing else in the response says so.
    // Dropping this field here would sign the user in and leave them exactly where they started.
    const response = NextResponse.json({
      mustChangePassword: mustChange,
      mayReplaceSecondFactor: Boolean(mayReplaceSecondFactor),
    });
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
