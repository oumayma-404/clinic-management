import type { NextRequest } from 'next/server';
import { NextResponse } from 'next/server';
import { auth0 } from './lib/auth0';
import { SESSION_COOKIE, MUST_CHANGE_COOKIE, resolveAuthMode } from './lib/auth/local-auth';

const PUBLIC_ROUTES = ['/login', '/setup', '/join'];
const CHANGE_PASSWORD_ROUTE = '/change-password';

// Dual-mode middleware: Local (offline cookie session) or Cloud (Auth0), by AUTH_MODE.
export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // ---- Local (offline) mode: gate on the session cookie, redirect to /login ----
  if (resolveAuthMode() === 'local') {
    if (
      pathname.startsWith('/_next/') ||
      pathname.startsWith('/api/auth/') ||
      PUBLIC_ROUTES.includes(pathname)
    ) {
      return NextResponse.next();
    }

    const token = request.cookies.get(SESSION_COOKIE)?.value;
    if (!token) {
      const loginUrl = new URL('/login', request.url);
      loginUrl.searchParams.set('returnTo', pathname);
      return NextResponse.redirect(loginUrl);
    }

    // AC-5.2: a user whose password was reset must change it before using the app. While the
    // flag cookie is set, force them onto /change-password (the route clears it on success).
    const mustChange = request.cookies.get(MUST_CHANGE_COOKIE)?.value === '1';
    if (mustChange && pathname !== CHANGE_PASSWORD_ROUTE) {
      return NextResponse.redirect(new URL(CHANGE_PASSWORD_ROUTE, request.url));
    }

    return NextResponse.next();
  }

  // ---- Cloud (Auth0) mode: unchanged ----
  // Let Auth0 handle auth routes (/auth/*)
  if (pathname.startsWith('/auth/')) {
    return await auth0.middleware(request);
  }

  // Allow public routes (setup and join don't require clinic membership)
  if (PUBLIC_ROUTES.includes(pathname) || pathname.startsWith('/_next/') || pathname.startsWith('/api/auth/')) {
    return NextResponse.next();
  }

  // Check if user is authenticated
  const session = await auth0.getSession(request);

  // If not authenticated, redirect to login
  if (!session) {
    const loginUrl = new URL('/auth/login', request.url);
    loginUrl.searchParams.set('returnTo', pathname);
    return NextResponse.redirect(loginUrl);
  }

  // User is authenticated, continue
  // Note: Clinic membership check is done client-side via ClinicGuard component
  // This ensures better UX and allows setup/join pages to work
  return NextResponse.next();
}

export const config = {
  matcher: [
    /*
     * Match all request paths except for the ones starting with:
     * - _next/static (static files)
     * - _next/image (image optimization files)
     * - favicon.ico, sitemap.xml, robots.txt (metadata files)
     */
    '/((?!_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt).*)',
  ],
};

