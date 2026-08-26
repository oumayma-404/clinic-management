"use client"

import { useCallback, useEffect, useRef, useState } from "react"

import { dashboardApi } from "@/lib/api/dashboard"
import { showErrorToast } from "@/lib/errors"
import {
  DASHBOARD_BLOCK_KEYS,
  DEFAULT_HIDDEN_BLOCKS,
  type DashboardBlockKey,
} from "@/lib/dashboard-blocks"

/**
 * The signed-in user's dashboard layout, with optimistic toggling.
 *
 * Three decisions worth knowing:
 *
 * 1. **Optimistic, then reconciled.** A switch flips the local set immediately and the PUT settles behind it.
 *    Waiting for a round trip to redraw a card the user just switched off is the difference between a settings
 *    panel that feels like a control and one that feels like a form — and there is nothing to lose by being
 *    optimistic here, since the worst case is a card reappearing with an explanation.
 *
 * 2. **A failed save rolls back and says so.** Silently keeping the optimistic state would leave the user believing
 *    a layout was saved that will be gone on their next login.
 *
 * 3. **Un-customised means the defaults, not "nothing hidden".** The server stores only what the user chose and
 *    knows nothing about which blocks lead a fresh dashboard — that is a presentation opinion and it lives here.
 *    `hasStoredPreferences` is what tells the two apart: no row yet ⇒ apply `DEFAULT_HIDDEN_BLOCKS`; a row with an
 *    empty list ⇒ the user deliberately switched everything on, which must not be overwritten by the defaults.
 */
export function useDashboardPreferences() {
  const [hidden, setHidden] = useState<Set<DashboardBlockKey>>(() => new Set(DEFAULT_HIDDEN_BLOCKS))
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  // Whether the server has a row for this user. Distinguishes "never customised" (apply the defaults) from
  // "customised to show everything" (an empty hidden set the user meant).
  const hasStoredPreferences = useRef(false)
  /*
   * Writes are SERIALISED and COALESCED — never fired one-per-toggle.
   *
   * Firing per toggle is what caused « Cet enregistrement a été modifié par quelqu'un d'autre » on switching a
   * block off and straight back on. Two PUTs overlapped: the second read the row before the first had committed,
   * so its UPDATE carried the pre-first `xmin` and matched zero rows — `DbUpdateConcurrencyException` →
   * `ConflictException` → 409. The user was told a colleague had edited their own dashboard layout.
   *
   * `queued` holds at most one pending target, so a burst of toggles collapses to a single request carrying the
   * final state. That is also just correct behaviour: flipping six switches should be one write, not six.
   */
  const queuedRef = useRef<{ target: Set<DashboardBlockKey>; rollback: Set<DashboardBlockKey> } | null>(null)
  const runningRef = useRef(false)
  /*
   * The current hidden set, mirrored so the mutators can compute the next one WITHOUT doing it inside a
   * `setHidden(current => …)` updater. A state updater must be pure: React invokes it twice under StrictMode in
   * development, so queueing a network write from inside one fires it twice, and `setSaving` from the resulting
   * async work lands during another component's render.
   */
  const hiddenRef = useRef<Set<DashboardBlockKey>>(hidden)
  // Guards `setSaving` after unmount — navigating away mid-save is the normal case, not an edge one.
  const mountedRef = useRef(true)
  useEffect(() => () => { mountedRef.current = false }, [])

  /** Sets the visible layout and keeps `hiddenRef` in step, so the mutators never read a stale set. */
  const applyHidden = useCallback((next: Set<DashboardBlockKey>) => {
    hiddenRef.current = next
    setHidden(next)
  }, [])

  useEffect(() => {
    let cancelled = false

    dashboardApi
      .getPreferences()
      .then((prefs) => {
        if (cancelled) return
        /*
         * ⚠️ `isCustomised`, NOT `hiddenKpis.length`.
         *
         * The inference this replaces — « empty means fresh » — was wrong for exactly one input and it was the one
         * the customiser's own « Tout afficher » button produces: the write landed (`HiddenKpisCsv = ''`), the next
         * load read an empty set, concluded « never customised » and re-applied the defaults over it. So the one
         * layout choice a user could make with a single click was the one that could not be saved. The server now
         * says whether a row exists, which is the only side that can.
         */
        const stored = prefs.hiddenKpis.filter(isBlockKey)
        if (prefs.isCustomised) {
          hasStoredPreferences.current = true
          applyHidden(new Set(stored))
        } else {
          applyHidden(new Set(DEFAULT_HIDDEN_BLOCKS))
        }
      })
      .catch((error) => {
        if (cancelled) return
        // Never block the dashboard on its own preferences: fall back to the defaults and say so. A failed
        // preferences read must not be the reason a clinic cannot see today's figures.
        showErrorToast(error, "Vos préférences d'affichage n'ont pas pu être chargées ; disposition par défaut.")
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [])

  /**
   * Drains the queue one request at a time. Re-entrant-safe: a toggle arriving mid-flight replaces the queued
   * target and is picked up by this same loop, so only one PUT is ever open.
   */
  const pump = useCallback(async () => {
    if (runningRef.current) return
    runningRef.current = true
    if (mountedRef.current) setSaving(true)

    try {
      while (queuedRef.current) {
        const job = queuedRef.current
        queuedRef.current = null

        try {
          await dashboardApi.updatePreferences([...job.target])
          hasStoredPreferences.current = true
        } catch (error) {
          // Roll back to the state before this burst began and drop anything still queued: continuing to push
          // later targets after a failure would fight the rollback the user is now looking at.
          queuedRef.current = null
          applyHidden(job.rollback)
          showErrorToast(error, "La disposition n'a pas pu être enregistrée.")
          break
        }
      }
    } finally {
      runningRef.current = false
      if (mountedRef.current) setSaving(false)
    }
  }, [applyHidden])

  const persist = useCallback(
    (next: Set<DashboardBlockKey>, previous: Set<DashboardBlockKey>) => {
      queuedRef.current = {
        target: next,
        // Keep the EARLIEST rollback of a burst. If three toggles are coalesced and the write fails, the honest
        // thing to restore is the layout before the user started, not the state between two of their own clicks.
        rollback: queuedRef.current?.rollback ?? previous,
      }
      void pump()
    },
    [pump],
  )

  const toggle = useCallback(
    (key: DashboardBlockKey) => {
      const current = hiddenRef.current
      const next = new Set(current)
      if (next.has(key)) next.delete(key)
      else next.add(key)

      // Mirrors the server's refusal rather than letting the PUT fail: a dashboard with nothing on it is a blank
      // page whose only affordance is the panel that emptied it, and the user cannot tell it from a failed load.
      if (next.size >= DASHBOARD_BLOCK_KEYS.length) {
        showErrorToast(null, "Au moins un élément doit rester affiché.")
        return
      }

      applyHidden(next)
      persist(next, current)
    },
    [applyHidden, persist],
  )

  const resetToDefaults = useCallback(() => {
    const current = hiddenRef.current
    const next = new Set(DEFAULT_HIDDEN_BLOCKS)
    applyHidden(next)
    persist(next, current)
  }, [applyHidden, persist])

  const showAll = useCallback(() => {
    const current = hiddenRef.current
    const next = new Set<DashboardBlockKey>()
    applyHidden(next)
    persist(next, current)
  }, [applyHidden, persist])

  const isVisible = useCallback((key: DashboardBlockKey) => !hidden.has(key), [hidden])

  return { hidden, isVisible, toggle, resetToDefaults, showAll, loading, saving }
}

/** Narrows a server-sent key to one this build knows. A key from a removed block simply hides nothing. */
function isBlockKey(key: string): key is DashboardBlockKey {
  return (DASHBOARD_BLOCK_KEYS as string[]).includes(key)
}
