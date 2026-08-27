import { NextResponse, type NextRequest } from "next/server";
import { SESSION_COOKIE } from "@/lib/session";

/**
 * Sends a caller with no session cookie to the sign-in screen.
 *
 * ⚠️ **A convenience, not the gate.** The gate is the API: every console endpoint carries the `PlatformConsole`
 * policy and `PlatformAccountStateMiddleware` re-reads the account on each request, so a forged or expired
 * cookie buys nothing. What this avoids is the page rendering an empty portfolio and *looking* like a
 * deployment with no cabinets — EC-12's « je n'ai pas pu lire » and « aucun cabinet » must never look the same,
 * and « you are not signed in » is a third state that must not look like either.
 */
export function middleware(request: NextRequest) {
  if (request.cookies.get(SESSION_COOKIE)) {
    return NextResponse.next();
  }

  const login = new URL("/login", request.url);
  return NextResponse.redirect(login);
}

export const config = {
  // Everything except the sign-in screen, its own BFF hop, and Next's static output. `mot-de-passe` is NOT
  // exempt: changing a password requires a session (AC-8.6), which is the whole distinction between it and the
  // three anonymous auth actions.
  matcher: ["/((?!login|bff/session|_next/static|_next/image|favicon.ico).*)"],
};
