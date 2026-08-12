import { NextRequest, NextResponse } from 'next/server';
import { resolveAuthMode } from '@/lib/auth/local-auth';
import { readSessionCookie } from '@/lib/auth/session-cookie';

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

  return NextResponse.json({ user: { name: claims.name, email: claims.email, role: claims.role } });
}

function decodeJwtPayload(token: string): { name?: string; email?: string; role?: string } | null {
  try {
    const payload = token.split('.')[1];
    if (!payload) return null;
    const json = Buffer.from(payload.replace(/-/g, '+').replace(/_/g, '/'), 'base64').toString('utf8');
    return JSON.parse(json);
  } catch {
    return null;
  }
}
