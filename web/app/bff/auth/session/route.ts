import { NextRequest, NextResponse } from 'next/server';
import { resolveAuthMode } from '@/lib/auth/local-auth';
import { readSessionCookie } from '@/lib/auth/session-cookie';
import { trustedFromClaims } from '@/lib/auth/idle-limit';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Returns the current local-session user (decoded from the JWT cookie) for the
// LocalSessionProvider to display. Local mode only.
export async function GET(request: NextRequest) {
  if (resolveAuthMode() !== 'local') {
    return NextResponse.json({ error: 'Not in local mode' }, { status: 404 });
  }

  const token = readSessionCookie((name) => request.cookies.get(name)?.value);
  if (!token) {
    return NextResponse.json({ error: 'Not authenticated' }, { status: 401 });
  }

  const claims = decodeJwtPayload(token);
  if (!claims) {
    return NextResponse.json({ error: 'Invalid session' }, { status: 401 });
  }

  // `trusted` travels with the identity so a cold page load can size its idle timer from the cookie alone, with
  // no extra round trip — the provider needs it before the first API call it would otherwise piggyback on.
  return NextResponse.json({
    user: { name: claims.name, email: claims.email, role: claims.role },
    trusted: trustedFromClaims(claims),
  });
}

function decodeJwtPayload(
  token: string
): { name?: string; email?: string; role?: string; session_trusted?: unknown } | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;
    const json = Buffer.from(payload.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    return JSON.parse(json);
  } catch {
    return null;
  }
}
