import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE } from '@/lib/auth/local-auth';
import { clearMustChangeCookie } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing
// NEXT_PUBLIC_API_URL is relative (`/api`) behind the same-origin front door and has no origin
// server-side, so use the server-only API_INTERNAL_URL (default the co-located API over loopback).
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

// Local-mode change password: proxies to the .NET API with the session-cookie JWT, then clears
// the forced-change flag on success (AC-5.2). Used by the /change-password screen for both the
// forced (post-reset) change and voluntary changes.
export async function POST(request: NextRequest) {
  const token = request.cookies.get(SESSION_COOKIE)?.value;
  if (!token) {
    return NextResponse.json({ error: 'Not authenticated.' }, { status: 401 });
  }

  let body: { currentPassword?: string; newPassword?: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }

  try {
    const res = await fetch(`${API_INTERNAL_URL}/auth/change-password`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${token}`,
        // Same loopback-hop problem as local-login: forward the browser's address rather than this
        // handler's, which is always the front door. Done here too so the two BFF routes cannot diverge.
        ...forwardedForHeader(request),
      },
      body: JSON.stringify({
        currentPassword: body.currentPassword ?? '',
        newPassword: body.newPassword ?? '',
      }),
    });

    const data = await res.json().catch(() => null);

    if (!res.ok || !data?.isSuccess) {
      return NextResponse.json(
        { error: data?.error || 'Échec du changement de mot de passe.' },
        { status: res.status === 401 ? 401 : 400 }
      );
    }

    const response = NextResponse.json({ ok: true });
    // Forced-change satisfied — clear the flag so the middleware stops redirecting. The next token exchange
    // would clear it too (the server reports the flag), but that is up to half an hour away.
    clearMustChangeCookie(response);
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre le serveur de la clinique. Veuillez réessayer.' },
      { status: 502 }
    );
  }
}
