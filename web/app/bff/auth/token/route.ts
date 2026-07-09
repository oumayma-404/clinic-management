import { auth0 } from '@/lib/auth0';
import { NextRequest, NextResponse } from 'next/server';
import { SESSION_COOKIE, resolveAuthMode } from '@/lib/auth/local-auth';

// Force dynamic rendering to avoid build-time evaluation
export const dynamic = 'force-dynamic';
export const runtime = 'nodejs';

export async function GET(request: NextRequest) {
  // Local mode: return the app-issued JWT stored in the session cookie.
  if (resolveAuthMode() === 'local') {
    const token = request.cookies.get(SESSION_COOKIE)?.value;
    if (!token) {
      return NextResponse.json({ error: 'Not authenticated' }, { status: 401 });
    }
    return NextResponse.json({ accessToken: token });
  }

  // Cloud mode: return the Auth0 access token (unchanged).
  try {
    const session = await auth0.getSession(request);
    if (!session) {
      return NextResponse.json({ error: 'Not authenticated' }, { status: 401 });
    }
    
    // Get access token - In Auth0 v4 App Router, getAccessToken() can be called without parameters
    // It automatically uses the request context (cookies/headers) from the route handler
    // GetAccessTokenOptions only supports: refresh, scope, audience
    const tokenResult = await auth0.getAccessToken();
    
    if (!tokenResult || !tokenResult.token) {
      return NextResponse.json({ error: 'No access token available' }, { status: 401 });
    }
    
    return NextResponse.json({ accessToken: tokenResult.token });
  } catch (error) {
    console.error('Error getting access token:', error);
    return NextResponse.json({ error: 'Failed to get access token' }, { status: 500 });
  }
}

