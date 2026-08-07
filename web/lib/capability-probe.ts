/**
 * The deployment-capability probe both public onboarding pages run before deciding whether to offer their form
 * (`/join` reads `selfRegistrationEnabled`, `/signup` reads `publicSignupEnabled`).
 *
 * <p>One home because the two had byte-identical copies of `withTimeout` down to the comment, under two names for
 * the same constant — and a page that probes for longer than the other is a page that feels broken on the same
 * network.</p>
 */

/** How long a capability probe may take before the page stops waiting and shows the form anyway. */
export const CAPABILITY_PROBE_TIMEOUT_MS = 5000

/**
 * Rejects if `promise` has not settled within `ms`. A wrapper rather than `AbortSignal.timeout` passed into the
 * fetch, because `apiGet` takes no signal — and giving the whole client layer one is a change well outside a
 * single page's probe. The request may still be in flight afterwards; nothing here reads its result.
 */
export function withTimeout<T>(promise: Promise<T>, ms: number = CAPABILITY_PROBE_TIMEOUT_MS): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined

  return Promise.race([
    promise,
    new Promise<never>((_, reject) => {
      timer = setTimeout(() => reject(new Error("La vérification a expiré.")), ms)
    }),
    // Clearing the timer matters on the success path: without it the pending timeout keeps the page's callback
    // alive for the full window after the probe has already answered.
  ]).finally(() => clearTimeout(timer))
}
