import { toast } from "sonner"
import { ApiError, ApiErrorCode } from "@/lib/api/client"

// Single French-first fallback used across the app when a thrown value carries no usable message.
export const DEFAULT_ERROR_MESSAGE = "Une erreur inattendue s'est produite."

/**
 * Extract a user-facing message from any thrown value. `ApiError`/`Error` messages (already localized by
 * the shared client for transport failures, or sent by the backend) win; otherwise the French fallback.
 * This is the single formatting point for error text, so components never build ad-hoc English strings.
 */
export function getErrorMessage(err: unknown, fallback: string = DEFAULT_ERROR_MESSAGE): string {
  if (err instanceof ApiError) {
    return err.message?.trim() ? err.message : fallback
  }
  if (err instanceof Error) {
    return err.message?.trim() ? err.message : fallback
  }
  if (typeof err === "string" && err.trim()) {
    return err
  }
  return fallback
}

/** True when the failure is a concurrent-edit conflict (HTTP 409) rather than a fault. */
export function isConflictError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 409
}

/** True when the failure is a permission denial (HTTP 403) rather than a fault. */
export function isForbiddenError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 403
}

/**
 * True when the cabinet's subscription is what refused this write (HTTP 402) — never a fault, never a rights
 * denial, and never a lost session (`clinic-subscription` AC-4.5).
 *
 * <p>Keyed on the **status**, not on the three codes: a caller asking this question wants « was this the
 * subscription? », and a 402 from our own front door can only be the gate. `ApiError.code` stays available for
 * the caller that needs to tell expiry from suspension.</p>
 *
 * <p>⚠️ It deliberately offers no retry: paying is the remedy, and the same request will refuse identically a
 * second later. `showErrorToast` already withholds « Réessayer » for anything but a transport failure.</p>
 */
export function isPaymentRequiredError(err: unknown): boolean {
  return err instanceof ApiError && err.status === 402
}

/**
 * True when the request never reached the server — the one failure class where **retrying is the right
 * advice** (AC-43).
 *
 * ⚠️ Keyed on the `network` code, not on `status === 0`: the client also raises `status: 0` for an
 * unexpected throw, and offering « Réessayer » for a fault would send the user round a loop that cannot
 * succeed.
 */
export function isNetworkError(err: unknown): boolean {
  return err instanceof ApiError && err.code === ApiErrorCode.Network
}

/**
 * How long an error toast stays. The global default (`app/layout.tsx`) is 4 s, which is right for a success
 * confirmation but not for a refusal: a success toast repeats something the screen already shows, while an
 * error toast is the *only* place the reason exists, and several of ours are full French sentences with a
 * cause and a suggested action. Dismissed early by the `closeButton` sonner renders on every toast, so the
 * longer life never traps anyone.
 */
const ERROR_TOAST_DURATION_MS = 8000

/** Everything `showErrorToast` can be told, for the call sites that need more than a fallback string. */
export interface ErrorToastOptions {
  /** Shown when the thrown value carries no usable message of its own. */
  fallback?: string
  /**
   * Offered as « Réessayer » — but **only** for a transport failure (see below). Pass the same function the
   * failed action would re-run.
   */
  onRetry?: () => void
  /**
   * A bold headline above the message, sonner's `toast.error(title, { description })` shape.
   *
   * <p>It exists purely to make the ~70 hand-rolled call sites a mechanical swap. Roughly a third of them read
   * `toast.error("Échec de la suppression", { description: msg })`, and without this the conversion would have
   * to drop their title — a lossy rewrite is a rewrite nobody performs, and the sweep stalls again.</p>
   */
  title?: string
}

/**
 * Show a single, non-blocking error toast for any thrown value (replaces `alert()` / silent swallows).
 *
 * <p><b>This is the only place an error toast should be raised.</b> It supplies the 8-second duration
 * ({@link ERROR_TOAST_DURATION_MS}) and the network-only « Réessayer », neither of which a hand-rolled
 * `toast.error(...)` inherits — those take the global 4 s meant for success confirmations, and with
 * `visibleToasts: 3` on a phone an error about a failed payment can be pushed off screen before it is read.</p>
 *
 * <p><b>Adopting it is a one-line swap</b>, by design:</p>
 * <pre>
 *   toast.error(err instanceof ApiError ? err.message : "Échec de la suppression.")
 *   → showErrorToast(err, "Échec de la suppression.")
 *
 *   toast.error("Échec de la sauvegarde", { description: msg })
 *   → showErrorToast(err, { title: "Échec de la sauvegarde" })
 *
 *   toast.error("Le motif est requis.")            // no thrown value at all
 *   → showErrorToast(null, "Le motif est requis.")
 * </pre>
 *
 * <p>`err` is `unknown` and every shape is handled — `ApiError`, `Error`, a bare string, `null`, or something
 * that is none of those — so no caller ever has to narrow it first. That narrowing (`err instanceof ApiError ?
 * err.message : "…"`) is precisely the boilerplate the hand-rolled sites were re-deriving, and it drops the
 * message of a plain `Error` on the floor.</p>
 *
 * <p>`onRetry` is optional and is honoured only for `isNetworkError`, never for a refusal: a 409 or a 403 will
 * refuse identically the second time, and a retry button that cannot work is worse than none. Callers that have
 * nothing to re-run simply omit it and the toast is unchanged.</p>
 */
export function showErrorToast(
  err: unknown,
  /** A fallback string, or the full option bag. The string form is the common case and stays a bare argument. */
  fallbackOrOptions?: string | ErrorToastOptions,
  /** Legacy positional form of `options.onRetry`. Kept so existing call sites need no edit. */
  onRetry?: () => void,
): void {
  const options: ErrorToastOptions =
    typeof fallbackOrOptions === "string" ? { fallback: fallbackOrOptions } : (fallbackOrOptions ?? {})

  const message = getErrorMessage(err, options.fallback ?? DEFAULT_ERROR_MESSAGE)
  const retry = options.onRetry ?? onRetry
  const action = retry && isNetworkError(err) ? { label: "Réessayer", onClick: retry } : undefined

  // With a title the message becomes the description, which is sonner's two-line shape; without one the message
  // *is* the toast. Not `title ?? message` for both — that would print the message twice.
  if (options.title) {
    toast.error(options.title, { description: message, duration: ERROR_TOAST_DURATION_MS, action })
    return
  }
  toast.error(message, { duration: ERROR_TOAST_DURATION_MS, action })
}
