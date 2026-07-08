import { NextResponse } from 'next/server';
import { SESSION_COOKIE } from '@/lib/auth/local-auth';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Clears the local session cookie. The configured server address is untouched (AC-3.6).
export async function POST() {
  const response = NextResponse.json({ ok: true });
  response.cookies.set(SESSION_COOKIE, '', { httpOnly: true, path: '/', maxAge: 0 });
  return response;
}
