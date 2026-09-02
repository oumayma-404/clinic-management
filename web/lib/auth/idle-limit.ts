/**
 * How long the app waits, with nobody touching it, before it locks or signs out.
 *
 * ⚠️ **This is a usability control, not a security boundary, and the distinction decides everything below.**
 * The boundary is server-side and unchanged: a 30-minute access token, a per-request `token_version` check, and
 * a refresh exchange that re-reads live account state. A modified client can ignore every number in this file
 * and gain nothing it did not already have from the cookie it holds. What the numbers decide is how often a
 * dentist is interrupted — so they are chosen against the working day, not against a threat model.
 *
 * ⚠️ **`trusted` decides HOW LONG the wait is; `canLock` decides WHAT HAPPENS at the end of it.** Those are two
 * separate questions and this function used to answer both with one: `if (canLock) return 30` came first, so a
 * lockable device got half an hour *however* trusted it was, and « Rester connecté sur cet appareil » had no
 * effect on interruptions at all on the one platform where it was most likely to be ticked.
 *
 * The reasoning was that a lock costs only seconds to clear, so there is no reason to wait long before showing
 * one. It is true that it costs seconds — and it was still wrong, because it priced the interruption at the cost
 * of dismissing it rather than at the cost of *being* interrupted. Windows Hello every thirty minutes, all day,
 * on the practitioner's own laptop, while a patient is in the chair: that is the complaint « remember this
 * device » exists to answer, and answering it only for devices that cannot lock inverted the feature.
 *
 * So the wait now follows the device, and `canLock` still chooses the ending:
 *
 * - **Lockable** — expiry **locks**: the cookie is untouched, the page stays mounted, and Windows Hello or a
 *   fingerprint returns the user to the same open fiche.
 * - **Not lockable** — expiry is a **full sign-out**: cookie cleared, session revoked server-side, back to
 *   password and a six-digit code.
 *
 * A trusted device therefore waits 8 h either way, and gets the *cheaper* of the two endings where it can.
 * An untrusted one — a shared reception PC, a browser where nobody ticked the box — still waits 30 minutes,
 * which is the case the short limit was written for.
 */

/** The default, and what an untrusted browser or a shared reception PC gets. */
export const DEFAULT_IDLE_LIMIT_MINUTES = 30

/**
 * A device its owner ticked « Rester connecté sur cet appareil » on. Long enough to cover a morning of chairside
 * work and a lunch break, short enough that a machine left running overnight is not still signed in when the
 * cleaners come through.
 *
 * ⚠️ Applies whether or not the device can lock. It used to apply only where it could NOT, which is the
 * inversion the note on `idleLimitMinutes` describes.
 */
export const TRUSTED_IDLE_LIMIT_MINUTES = 8 * 60

/**
 * The limit to enforce, in minutes.
 *
 * @param trusted whether this session was opened with « Rester connecté sur cet appareil » ticked
 * @param canLock whether the shell can confirm the device owner instead of signing out
 */
export function idleLimitMinutes(trusted: boolean, canLock: boolean): number {
  // ⚠️ `canLock` is deliberately NOT read here any more — see the note above. It is still a parameter because
  // it is part of the question this function answers ("what limit does THIS session get") and dropping it would
  // move the decision to the caller, which is where it was before there was one place for it. A caller that
  // stops passing it should be a compile error, not a silent change of policy.
  void canLock

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
