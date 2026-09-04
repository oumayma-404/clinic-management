/**
 * **The routes a visitor reaches with no session, by definition** — and the one list that says so.
 *
 * <p>It lived inside `middleware.ts` and was therefore known to exactly one consumer: the server-side route
 * gate. The client had its own idea of the same question, hand-written as `pathname.startsWith("/login")` in
 * three guards in `lib/auth/session.tsx`, and the two disagreed about every door but that one — which is what
 * took public signup down in production. Any anonymous call from `/signup` 401s the token exchange (the API
 * refuses it correctly: no cookie, no token), the exchange fires `onSessionExpired`, and the visitor is told
 * « Session expirée » and dropped on the login screen — about a session they never had, three fields into
 * creating their cabinet. `/setup`, `/join` and both password-reset doors sit on the same fault line.</p>
 *
 * <p>⚠️ **Matched by EXACT path**, which is what `middleware.ts` has always enforced: a route with a child needs
 * both entries. `/signup/verifier` is where the emailed link lands and is reached with no session by
 * definition, which is the whole point of it.</p>
 *
 * <p>⚠️ **The two password-reset routes belong here for that same reason, and omitting them is a
 * self-cancelling bug**: somebody who has forgotten their password has no session by definition, so gating
 * « mot de passe oublié » on one sends them to the login screen they just failed at — and the reset link in
 * their inbox lands on `/login?returnTo=…` instead of the form, quietly spending nothing and explaining
 * nothing. Neither page reads clinic data and neither issues a session; the emailed single-use token is the
 * only credential either one has.</p>
 */
export const PUBLIC_ROUTES = [
  '/login',
  '/setup',
  '/join',
  '/signup',
  '/signup/verifier',
  '/mot-de-passe-oublie',
  '/reinitialiser-mot-de-passe',
] as const;

/**
 * Whether this path is one of the doors above.
 *
 * <p>Exact match, mirroring the middleware's own `includes` — a path the list does not name is guarded, and a
 * guarded path is one where an absent session really is worth reporting.</p>
 */
export function isPublicRoute(pathname: string): boolean {
  return (PUBLIC_ROUTES as readonly string[]).includes(pathname);
}
