"use client"

import { useCallback, useEffect, useRef, useState } from "react"

/**
 * Confirm-before-discard for a dialog or sheet that has entered data (AC-23).
 *
 * **No form in this app had one.** `grep beforeunload` returned nothing, so every channel that closes a
 * dialog — the ✕, `Escape`, a tap on the overlay, the Android back gesture — silently threw away a
 * half-filled patient record. On a phone that matters more than on a desktop: the overlay is most of the
 * screen, so an outside tap is a *likely* accident rather than a deliberate one.
 *
 * ## Why it wraps `onOpenChange` rather than each control
 *
 * Radix funnels **every** dismissal through the root's `onOpenChange(false)` — the close button, `Escape`,
 * the outside pointer-down, and any programmatic close. Guarding that one function therefore covers the
 * whole contract without touching a single control, and a control added later is covered for free. The one
 * channel that does *not* pass through it is the **back gesture**, which is why this hook also owns a
 * history entry (below).
 *
 * ## Why dirtiness is observed, not declared
 *
 * The alternative is an `isDirty` boolean per form, which means every one of the five heavy forms has to
 * derive "has the user typed anything" from its own state — five implementations of one question, and each
 * new field is a chance to forget. Instead this listens for `input`/`change` events that originate inside
 * the open dialog. It is the browser telling us the user typed, rather than us inferring it.
 *
 * ⚠️ It deliberately does **not** compare against initial values, so re-typing the original value still
 * counts as dirty. That errs toward asking, which is the safe direction: a needless confirm costs a tap, a
 * missed one costs the visit's notes.
 */
export interface DirtyGuard {
  /** Pass to `<Dialog onOpenChange={…}>` in place of the raw handler. */
  onOpenChange: (open: boolean) => void
  /** True while the confirmation is showing. */
  confirmOpen: boolean
  /** Discard and close. */
  confirmDiscard: () => void
  /** Keep the dialog open, leave the input intact. */
  cancelDiscard: () => void
  /** Call after a successful save, so the close that follows does not ask. */
  markClean: () => void
}

export function useDirtyGuard(
  open: boolean,
  onOpenChange: (open: boolean) => void,
): DirtyGuard {
  const dirty = useRef(false)
  const [confirmOpen, setConfirmOpen] = useState(false)
  // Read through a ref by the history effect below, which must not re-subscribe when the caller re-renders.
  const onOpenChangeRef = useRef(onOpenChange)
  onOpenChangeRef.current = onOpenChange

  // Reset on every open: the previous session's typing must not make a freshly opened dialog dirty.
  useEffect(() => {
    if (open) {
      dirty.current = false
      setConfirmOpen(false)
    }
  }, [open])

  /*
   * Dirtiness, observed at the document because the dialog is portalled out of the caller's tree and the
   * hook has no handle on its DOM. Scoped by `closest()` to the open content, so a keystroke in the page
   * behind — the header's patient search, say — cannot mark the dialog dirty.
   */
  useEffect(() => {
    if (!open) return
    const mark = (event: Event) => {
      const target = event.target as HTMLElement | null
      if (target?.closest('[data-slot="dialog-content"],[data-slot="sheet-content"]')) {
        dirty.current = true
      }
    }
    document.addEventListener("input", mark, true)
    document.addEventListener("change", mark, true)
    return () => {
      document.removeEventListener("input", mark, true)
      document.removeEventListener("change", mark, true)
    }
  }, [open])

  const markClean = useCallback(() => {
    dirty.current = false
  }, [])

  const guardedOpenChange = useCallback(
    (next: boolean) => {
      if (next || !dirty.current) {
        onOpenChange(next)
        return
      }
      setConfirmOpen(true)
    },
    [onOpenChange],
  )

  /*
   * The back gesture (AC-23). It never reaches `onOpenChange`, so the hook owns a history entry while the dialog
   * is open and treats `popstate` as a close request; the entry is popped on close so no dead back step is left.
   * On a *clean* dialog back must still close it — what a phone expects — so this runs the guarded path always.
   *
   * ⚠️ **The push is deferred by a tick, and that is the whole fix for a dialog that mounts already open.**
   * React double-invokes effects on mount in development, so such a dialog pushed an entry, tore down, called
   * `history.back()` and pushed again — a real `popstate` the dialog never asked for, which closed it a frame
   * after it appeared. Ignoring the pop here was not enough: the browser and the router see it too. Deferring
   * means the teardown of that discarded first run has nothing to undo, so no pop is ever emitted. The two
   * surfaces this broke are the post-visit fiche deep-link (`?addRecord=1&appointmentId=…`) and the plan
   * workspace's « Planifier », which remounted its dialog by changing its `key` as it opened.
   */
  useEffect(() => {
    if (!open) return
    const marker = { dialogGuard: true }
    let pushed = false
    const pushTimer = window.setTimeout(() => {
      window.history.pushState(marker, "")
      pushed = true
    }, 0)

    const onPop = () => {
      // Still on a marker ⇒ our own teardown popped it, not the user. A real back lands on the page entry.
      if (window.history.state?.dialogGuard) return
      if (dirty.current) {
        // Re-arm: the browser already consumed our entry, so without this a second back would leave the page.
        window.history.pushState(marker, "")
        setConfirmOpen(true)
        return
      }
      onOpenChangeRef.current(false)
    }

    window.addEventListener("popstate", onPop)
    return () => {
      window.clearTimeout(pushTimer)
      window.removeEventListener("popstate", onPop)
      // Only undo an entry we actually got as far as pushing.
      if (pushed && window.history.state?.dialogGuard) window.history.back()
    }
  }, [open])

  const confirmDiscard = useCallback(() => {
    dirty.current = false
    setConfirmOpen(false)
    onOpenChange(false)
  }, [onOpenChange])

  const cancelDiscard = useCallback(() => setConfirmOpen(false), [])

  return { onOpenChange: guardedOpenChange, confirmOpen, confirmDiscard, cancelDiscard, markClean }
}
