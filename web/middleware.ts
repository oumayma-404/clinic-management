import type { NextRequest } from 'next/server';
import { NextResponse } from 'next/server';
import { auth0 } from './lib/auth0';
import { resolveAuthMode } from './lib/auth/local-auth';
import { readSessionCookie, readMustChangeCookie } from './lib/auth/session-cookie';

// ⚠️ Matched by EXACT path (`includes`), so a route with a child needs both entries — `/signup/verifier` is
// where the emailed link lands and is reached with no session by definition, which is the whole point of it.
//
// ⚠️ **The two password-reset routes belong here for that same reason, and omitting them is a self-cancelling
// bug**: somebody who has forgotten their password has no session by definition, so gating « mot de passe
// oublié » on one sends them to the login screen they just failed at — and the reset link in their inbox lands
// on `/login?returnTo=…` instead of the form, quietly spending nothing and explaining nothing. Neither page
// reads clinic data and neither issues a session; the emailed single-use token is the only credential either
// one has.
const PUBLIC_ROUTES = [
  '/login',
  '/setup',
  '/join',
  '/signup',
  '/signup/verifier',
  '/mot-de-passe-oublie',
  '/reinitialiser-mot-de-passe',
];
const CHANGE_PASSWORD_ROUTE = '/change-password';

// Redirect to the same-origin FRONT DOOR. Behind the reverse proxy the Next server's own request host is
// the internal localhost:<webPort> (HTTP-only), so `new URL(path, request.url)` would bounce the browser
// there (ERR_SSL_PROTOCOL_ERROR). A bare relative Location isn't allowed either — Next's middleware runtime
// runs `new URL()` on it and throws ERR_INVALID_URL (→ 500). So build an ABSOLUTE URL from the forwarded
// headers YARP adds (the browser's real host + scheme), sending the browser to the HTTPS front door.
function frontDoorRedirect(request: NextRequest, location: string) {
  const host = request.headers.get('x-forwarded-host') ?? request.headers.get('host') ?? request.nextUrl.host;
  const proto = request.headers.get('x-forwarded-proto') ?? 'https';
  return NextResponse.redirect(new URL(location, `${proto}://${host}`));
}

// Dual-mode middleware: Local (offline cookie session) or Cloud (Auth0), by AUTH_MODE.
export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // ---- Local (offline) mode: gate on the session cookie, redirect to /login ----
  if (resolveAuthMode() === 'local') {
    if (
      pathname.startsWith('/_next/') ||
      pathname.startsWith('/bff/auth/') ||
      PUBLIC_ROUTES.includes(pathname)
    ) {
      return NextResponse.next();
    }

    const token = readSessionCookie((name) => request.cookies.get(name)?.value);
    if (!token) {
      return frontDoorRedirect(request, `/login?returnTo=${encodeURIComponent(pathname)}`);
    }

    // AC-5.2: a user whose password was reset must change it before using the app. While the
    // flag cookie is set, force them onto /change-password (the route clears it on success).
    const mustChange = readMustChangeCookie((name) => request.cookies.get(name)?.value) === '1';
    if (mustChange && pathname !== CHANGE_PASSWORD_ROUTE) {
      return frontDoorRedirect(request, CHANGE_PASSWORD_ROUTE);
    }

    return NextResponse.next();
  }

  // ---- Cloud (Auth0) mode: unchanged ----
  // Let Auth0 handle auth routes (/auth/*)
  if (pathname.startsWith('/auth/')) {
    return await auth0.middleware(request);
  }

  // Allow public routes (setup and join don't require clinic membership)
  if (PUBLIC_ROUTES.includes(pathname) || pathname.startsWith('/_next/') || pathname.startsWith('/bff/auth/')) {
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
     * - manifest.webmanifest (the same class of metadata file as the three above)
     *
     * `manifest.webmanifest` belongs beside `favicon.ico`/`sitemap.xml`/`robots.txt` for the reason they are
     * already here: the browser fetches it with no session, before anyone has signed in, and reads a redirect to
     * an HTML login page as « no manifest » — which downgrades « Ajouter à l'écran d'accueil » from a standalone
     * app to a bare shortcut, silently, on iOS and Android alike. That matters most on iOS, where the installable
     * web app is the only route we have at all (see `mobile/STORE-SUBMISSION.md`).
     *
     * ⚠️ In practice it is already served without this: `app/manifest.ts` is generated at build time, so the
     * hosted deployment answers it **200 `application/manifest+json`** repeatedly, and the icons under `public/`
     * likewise. It is excluded here to make that a property of the matcher rather than of how Next happens to
     * emit the route. **Do not extend this into a general `\.(png|json|txt)$` escape** — that widens the
     * unauthenticated surface to any future route whose path ends in an extension, which is a much bigger claim
     * than this one file needs.
     */
    '/((?!_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt|manifest.webmanifest).*)',
  ],
};

