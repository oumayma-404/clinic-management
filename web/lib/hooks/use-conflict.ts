"use client"

import { useCallback, useRef, useState } from "react"
import { ApiError } from "@/lib/api/client"
import { getErrorMessage } from "@/lib/errors"

/**
 * Escalated wording. After a reload-and-retry has already failed once, repeating « rechargez puis
 * réessayez » is unhelpful — the real situation is that someone is editing the same record right now.
 */
const REPEATED_CONFLICT_FR =
  "L'enregistrement a encore été modifié pendant votre saisie. Quelqu'un travaille probablement dessus en " +
  "même temps — coordonnez-vous avant de réessayer."

export interface ConflictState {
  /** The message to show in the form banner, or null when there is nothing to report. */
  error: string | null
  /** True when the last failure was a 409 specifically — drives the « Recharger » affordance. */
  isConflict: boolean
  /** Record a thrown value. Returns true when it was a conflict, so callers can branch. */
  capture: (err: unknown, fallback?: string) => boolean
  /** Set a plain (non-conflict) message, e.g. a client-side validation failure. */
  setError: (message: string | null) => void
  /** Clear everything, including the consecutive-conflict counter. Call when the dialog opens. */
  reset: () => void
}

/**
 * Form-level error state that knows about 409s.
 *
 * <p>Every modal in this app previously handled a failed save with a toast and nothing else, so a conflict
 * would have read as a generic « échec de l'enregistrement » and the user's only move was to press save
 * again — which would fail identically, forever. This keeps the message in the form, distinguishes the
 * conflict case so the caller can offer a reload, and escalates the wording on a second consecutive
 * conflict rather than repeating advice that has already been tried.</p>
 *
 * <p>The user's input is never touched. Recovery is "here is what changed, re-apply if you still want to" —
 * discarding what they typed would make the conflict cost more than the silent overwrite it replaced.</p>
 */
export function useConflict(): ConflictState {
  const [error, setErrorState] = useState<string | null>(null)
  const [isConflict, setIsConflict] = useState(false)
  const consecutive = useRef(0)

  const capture = useCallback((err: unknown, fallback?: string) => {
    const conflict = err instanceof ApiError && err.status === 409
    if (conflict) {
      consecutive.current += 1
      setIsConflict(true)
      // The server's own message on the first hit; the escalation only once it is clear that reloading
      // and retrying did not settle it.
      setErrorState(consecutive.current > 1 ? REPEATED_CONFLICT_FR : getErrorMessage(err, fallback))
      return true
    }

    consecutive.current = 0
    setIsConflict(false)
    setErrorState(getErrorMessage(err, fallback))
    return false
  }, [])

  const setError = useCallback((message: string | null) => {
    consecutive.current = 0
    setIsConflict(false)
    setErrorState(message)
  }, [])

  const reset = useCallback(() => {
    consecutive.current = 0
    setIsConflict(false)
    setErrorState(null)
  }, [])

  return { error, isConflict, capture, setError, reset }
}
