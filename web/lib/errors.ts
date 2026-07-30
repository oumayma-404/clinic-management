import { toast } from "sonner"
import { ApiError } from "@/lib/api/client"

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
 * How long an error toast stays. The global default (`app/layout.tsx`) is 4 s, which is right for a success
 * confirmation but not for a refusal: a success toast repeats something the screen already shows, while an
 * error toast is the *only* place the reason exists, and several of ours are full French sentences with a
 * cause and a suggested action. Dismissed early by the `closeButton` sonner renders on every toast, so the
 * longer life never traps anyone.
 */
const ERROR_TOAST_DURATION_MS = 8000

/** Show a single, non-blocking error toast for any thrown value (replaces `alert()` / silent swallows). */
export function showErrorToast(err: unknown, fallback: string = DEFAULT_ERROR_MESSAGE): void {
  toast.error(getErrorMessage(err, fallback), { duration: ERROR_TOAST_DURATION_MS })
}
