import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE } from '@/lib/auth/local-auth';

export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5000/api';

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
    const res = await fetch(`${API_BASE_URL}/auth/login`, {
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
    const response = NextResponse.json({ mustChangePassword: Boolean(mustChangePassword) });
    response.cookies.set(SESSION_COOKIE, accessToken, {
      httpOnly: true,
      secure: process.env.NODE_ENV === 'production',
      sameSite: 'lax',
      path: '/',
      expires: expiresAt ? new Date(expiresAt) : undefined,
    });
    return response;
  } catch {
    return NextResponse.json(
      { error: 'Cannot reach the clinic server. Please try again.' },
      { status: 502 }
    );
  }
}
