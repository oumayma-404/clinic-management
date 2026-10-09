"use client"

import { useEffect } from "react"

import { useSession } from "@/lib/auth/session"
import { handBack, takeCarriedOnce } from "@/lib/forms/carried-draft"

/**
 * `clinic-pc-copy` D23 / AC-3.2 — after the app switched server, the form that was open comes back. The shell opens
 * the new server on its home page; this takes the carried draft once, and when it belongs to another screen, gives it
 * back to the shell and goes there — where the form's deep link opens it and the form claims the draft.
 *
 * ⚠️ Compares the PATH only, never the query: a deep link drops its own query once read, so comparing it would send
 * the page round in a circle. ⚠️ Inert without the shell's `takeCarriedDraft` (a browser, an older shell).
 */
export function CarriedDraftResume() {
  const { user } = useSession()
  const userId = user?.email?.toLowerCase() ?? null

  useEffect(() => {
    if (!userId) return
    let cancelled = false
    void takeCarriedOnce(userId).then((draft) => {
      if (cancelled || !draft) return
      const target = new URL(draft.path, window.location.origin)
      if (target.pathname === window.location.pathname) return
      handBack(draft)
      window.location.assign(target.pathname + target.search)
    })
    return () => {
      cancelled = true
    }
  }, [userId])

  return null
}
