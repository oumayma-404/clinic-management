import type { NextRequest } from 'next/server';
import { NextResponse } from 'next/server';
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

// The route gate: an offline-capable cookie session, the only kind this product has since the Auth0-backed
// deployment was retired. AUTH_MODE is no longer branched on here — there is one mode.
export async function middleware(request: NextRequest) {
  const { pathname } = request.nextUrl;

  // Gate on the session cookie, redirect to /login.
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

  // Clinic membership is checked client-side by <ClinicGuard>, which is what lets /setup and /join work.
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
     * ⚠️ In practice `app/manifest.ts` is generated at build time, so the hosted deployment answers it **200
     * `application/manifest+json`** repeatedly. It is excluded here to make that a property of the matcher rather
     * than of how Next happens to emit the route. **Do not extend this into a general `\.(png|json|txt)$`
     * escape** — that widens the unauthenticated surface to any future route whose path ends in an extension,
     * which is a much bigger claim than these files need.
     *
     * ⚠️ **The icon files are NOT in that lucky category, and the sentence that used to say they were was wrong.**
     * Measured on the hosted deployment: `GET /icon-192.png` answered **307 → /login?returnTo=%2Ficon-192.png`**.
     * They live under `public/`, which Next serves through the normal request pipeline, so this matcher runs on
     * them and the guard redirects. Every consumer of one is a browser fetching it with **no session** — the
     * login screen's own mark, and the icons the manifest names — so each got an HTML login page where it asked
     * for an image. The login screen showed a broken-image glyph above « Connexion », on the one screen that has
     * nothing else to identify the product by; the manifest's icons failed the same way, invisibly, which
     * downgrades the installed app's tile. Nothing in a build or a type-check can see it: the file is present in
     * the image, the route answers 307 rather than 404, and the app is *correct* to redirect an unauthenticated
     * request for anything it does not know to be public.
     *
     * The seven icons are listed **by name**, in the same plain form as `favicon.ico` above — no character class,
     * no `(?:…)` group, no escaped dot. Next hands this matcher to path-to-regexp rather than straight to
     * `RegExp`, so the narrowest thing that is certainly parsed the same way as the entries already here is worth
     * more than a shorter pattern; and an explicit list makes the unauthenticated surface readable at a glance,
     * which a wildcard never does. `check:responsive`'s `public-asset-not-guarded` derives the required list from
     * what the code actually references, so an eighth icon fails the build instead of failing in a browser.
     */
    '/((?!_next/static|_next/image|favicon.ico|sitemap.xml|robots.txt|manifest.webmanifest|apple-icon.png|icon.svg|icon-192.png|icon-512.png|icon-maskable-512.png|icon-light-32x32.png|icon-dark-32x32.png).*)',
  ],
};

