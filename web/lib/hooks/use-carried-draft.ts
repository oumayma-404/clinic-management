"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { useSession } from "@/lib/auth/session"
import {
  carry,
  claimCarried,
  dropCarried,
  takeCarriedOnce,
  type CarriedDraft,
} from "@/lib/forms/carried-draft"

const CARRY_DEBOUNCE_MS = 400

export interface CarriedDraftOptions<S> {
  /** Which form (`"fiche"`, …). */
  form: string
  /** Which record — null while the form does not know yet (nothing is carried or claimed then). */
  formKey: string | null
  /** The deep link that opens this form on this record. */
  path: string
  open: boolean
  /** Only a form somebody typed into is carried. */
  dirty: boolean
  /** What to carry — read on each change, so keep it serialisable (no functions, no Sets). */
  state: S
  /** Puts a claimed draft back on screen. */
  restore: (state: S, draft: CarriedDraft<S>) => void
}

export interface CarriedDraftState {
  /** The form came back from another server and its first save has not been refused yet (AC-3.2). */
  switched: boolean
  /**
   * Call at the top of a save: true the FIRST time after a switch — refuse that save with `SWITCH_REFUSAL` and
   * offer « Recharger » — false after that, so the next Enregistrer goes through.
   */
  consumeSwitch: () => boolean
}

/**
 * `clinic-pc-copy` D23 — a form survives the app switching server (AC-3.2). While open and typed into, its state goes
 * to the shell; when it reopens after a switch, the state comes back through `restore`. Inert without a shell.
 */
export function useCarriedDraft<S>({ form, formKey, path, open, dirty, state, restore }: CarriedDraftOptions<S>): CarriedDraftState {
  const { user } = useSession()
  const userId = user?.email?.toLowerCase() ?? null
  const [switched, setSwitched] = useState(false)
  const switchedRef = useRef(false)
  const restoreRef = useRef(restore)
  restoreRef.current = restore

  // Claim once per open of a record: a draft taken on this page load for this form and this record.
  const claimedFor = useRef<string | null>(null)
  useEffect(() => {
    if (!open) {
      claimedFor.current = null
      switchedRef.current = false
      setSwitched(false)
      return
    }
    if (!formKey || !userId || claimedFor.current === formKey) return
    claimedFor.current = formKey
    let cancelled = false
    void takeCarriedOnce(userId).then(() => {
      if (cancelled) return
      const draft = claimCarried<S>(form, formKey)
      if (!draft) return
      restoreRef.current(draft.state, draft)
      const fromElsewhere = draft.origin !== window.location.origin
      switchedRef.current = fromElsewhere
      setSwitched(fromElsewhere)
    })
    return () => {
      cancelled = true
    }
  }, [open, form, formKey, userId])

  // Carry while typed into; forget on close.
  const serialised = open && dirty && formKey && userId ? safeJson(state) : null
  useEffect(() => {
    if (!serialised || !formKey || !userId) return
    const timer = window.setTimeout(() => {
      carry({ form, key: formKey, path, userId, state: JSON.parse(serialised) as unknown })
    }, CARRY_DEBOUNCE_MS)
    return () => window.clearTimeout(timer)
  }, [serialised, form, formKey, path, userId])

  useEffect(() => {
    if (open && formKey) {
      return () => dropCarried(form, formKey)
    }
    return undefined
  }, [open, form, formKey])

  const consumeSwitch = useCallback(() => {
    if (!switchedRef.current) return false
    switchedRef.current = false
    setSwitched(false)
    return true
  }, [])

  return { switched, consumeSwitch }
}

function safeJson(value: unknown): string | null {
  try {
    return JSON.stringify(value)
  } catch {
    return null
  }
}
