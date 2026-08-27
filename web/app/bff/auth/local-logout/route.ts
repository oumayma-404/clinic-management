import { NextResponse } from 'next/server';
import { clearSessionCookies } from '@/lib/auth/session-cookie';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

// Clears the local session cookies. The configured server address is untouched (AC-3.6).
export async function POST() {
  const response = NextResponse.json({ ok: true });
  clearSessionCookies(response);
  return response;
}
