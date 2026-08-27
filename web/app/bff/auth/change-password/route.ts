import { NextRequest, NextResponse } from 'next/server';
import { readSessionCookie } from '@/lib/auth/session-cookie';
import { clearSessionCookies } from '@/lib/auth/session-cookie';
import { forwardedForHeader } from '@/lib/auth/forwarded-for';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Server-side handler: must reach the .NET API with an ABSOLUTE URL. The browser-facing
// NEXT_PUBLIC_API_URL is relative (`/api`) behind the same-origin front door and has no origin
// server-side, so use the server-only API_INTERNAL_URL (default the co-located API over loopback).
const API_INTERNAL_URL = process.env.API_INTERNAL_URL || 'http://localhost:5000/api';

// Local-mode change password: proxies to the .NET API, then clears the session on success (AC-5.2).
// Used by the /change-password screen for both the forced (post-reset) change and voluntary changes.
export async function POST(request: NextRequest) {
  const sessionCredential = readSessionCookie((name) => request.cookies.get(name)?.value);
  if (!sessionCredential) {
    return NextResponse.json({ error: 'Session absente. Reconnectez-vous.' }, { status: 401 });
  }

  let body: { currentPassword?: string; newPassword?: string };
  try {
    body = await request.json();
  } catch {
    return NextResponse.json({ error: 'Invalid request.' }, { status: 400 });
  }

  try {
    // ⚠️ The cookie holds the REFRESH token, which the API rejects outright as a bearer — a different
    // audience is precisely what makes it useless for anything but being exchanged (AC-5.5). Sending it
    // here 401'd at authentication, before `ChangePasswordCommand` ever ran, so EVERY password change
    // failed with « Échec du changement de mot de passe. » and a forced-reset user was stuck on this
    // screen with no way forward. `RefreshTokenCommand` states the intended shape in its own comment —
    // « a pending forced password change is NOT a refusal: the change-password screen itself needs a
    // working access token to submit » — and this is the exchange that was missing. Same step
    // `/bff/auth/token` takes; the two routes read the same cookie and must read it the same way.
    const accessToken = await exchangeForAccessToken(request, sessionCredential);
    if (!accessToken) {
      return NextResponse.json(
        { error: 'Votre session n’est plus valide. Veuillez vous reconnecter.' },
        { status: 401 }
      );
    }

    const res = await fetch(`${API_INTERNAL_URL}/auth/change-password`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${accessToken}`,
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
    // ⚠️ BOTH cookies go, not just the forced-change flag. `User.SetPassword` bumps `TokenVersion`, so the
    // credential this cookie holds was revoked by the very call that just succeeded — leaving it in place
    // means the next page load exchanges a dead token, gets a 401 and bounces to /login anyway, which reads
    // as the change having failed. Clearing it here makes the re-login the deliberate ending it is.
    clearSessionCookies(response);
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Impossible de joindre le serveur du cabinet. Veuillez réessayer.' },
      { status: 502 }
    );
  }
}

/**
 * Exchanges the cookie's durable refresh credential for a short-lived access token, or null when the API
 * refuses it. Deliberately does NOT re-set the session cookie the way `/bff/auth/token` does: the password
 * change about to run revokes whatever this mints, so storing it would only extend a credential by one
 * request before killing it.
 */
async function exchangeForAccessToken(
  request: NextRequest,
  sessionCredential: string
): Promise<string | null> {
  const res = await fetch(`${API_INTERNAL_URL}/auth/refresh`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      ...forwardedForHeader(request),
    },
    body: JSON.stringify({ refreshToken: sessionCredential }),
  });

  const data = await res.json().catch(() => null);
  return res.ok && data?.isSuccess ? (data.value?.accessToken ?? null) : null;
}
