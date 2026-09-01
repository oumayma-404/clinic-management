/**
 * How long the app waits, with nobody touching it, before it locks or signs out.
 *
 * ⚠️ **This is a usability control, not a security boundary, and the distinction decides everything below.**
 * The boundary is server-side and unchanged: a 30-minute access token, a per-request `token_version` check, and
 * a refresh exchange that re-reads live account state. A modified client can ignore every number in this file
 * and gain nothing it did not already have from the cookie it holds. What the numbers decide is how often a
 * dentist is interrupted — so they are chosen against the working day, not against a threat model.
 *
 * ⚠️ **The limit is a function of what happens when it expires.** That is the whole rule, and getting it
 * backwards is how the old behaviour came about:
 *
 * - Where the shell can ask the operating system who is holding the device (`confirmIdentity`), expiry **locks**:
 *   the cookie is untouched, the page stays mounted, and a fingerprint or Windows Hello returns the user to the
 *   same open fiche. That costs seconds, so there is no reason to wait long — 30 minutes stays.
 * - Where it cannot, expiry is a **full sign-out**: cookie cleared, session revoked server-side, back to
 *   password and a six-digit code. On a device its owner has vouched for, paying that price after half an hour of
 *   treating a patient is the complaint this feature exists to answer, so the wait is much longer.
 *
 * The consequence worth stating out loud: a trusted device that gains a lock screen gets a *shorter* idle limit,
 * not a longer one, and that is an improvement rather than a regression.
 */

/** The default, and what an untrusted browser or a shared reception PC gets. */
export const DEFAULT_IDLE_LIMIT_MINUTES = 30

/**
 * A trusted device with no way to lock. Long enough to cover a morning of chairside work and a lunch break,
 * short enough that a machine left running overnight is not still signed in when the cleaners come through.
 */
export const TRUSTED_IDLE_LIMIT_MINUTES = 8 * 60

/**
 * The limit to enforce, in minutes.
 *
 * @param trusted whether this session was opened with « Rester connecté sur cet appareil » ticked
 * @param canLock whether the shell can confirm the device owner instead of signing out
 */
export function idleLimitMinutes(trusted: boolean, canLock: boolean): number {
  // A lock is cheap to recover from, so the ordinary wait applies however trusted the device is.
  if (canLock) return DEFAULT_IDLE_LIMIT_MINUTES

  return trusted ? TRUSTED_IDLE_LIMIT_MINUTES : DEFAULT_IDLE_LIMIT_MINUTES
}

/**
 * Whether a session credential says the device was trusted.
 *
 * ⚠️ **Read from the token only to size a timer.** The server decides what a trusted session *is* and how long
 * its credential lives; this claim is a hint for the client's own countdown, and the API never believes it — a
 * rotation reads the `SessionFamily` row instead. See `LocalAuthClaims.SessionTrusted`.
 */
export function trustedFromClaims(claims: { session_trusted?: unknown } | null | undefined): boolean {
  return claims?.session_trusted === '1' || claims?.session_trusted === true
}
