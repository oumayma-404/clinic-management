"use client"

import { useEffect, useRef, useState } from "react"

/**
 * Mirrors a screen's filters into its own query string, so **F5 keeps them and a URL can be shared**.
 *
 * ## Why this exists
 *
 * Four screens held every narrowing control in component state alone and wiped their query string on entry, so
 * nothing about what was on screen was addressable: the agenda's day and its « Terminés » / praticien filters,
 * « Fichiers »' search + sort + « avec fichiers », the journal's six filters, and the lab-orders stage. Pressing
 * F5 — or handing a colleague a link to « les bons en retard » — silently returned the default view. The agenda
 * was the worst case: `?date=2026-08-24` plus a filter became the current week with no filter and 26 blocks.
 *
 * ⚠️ **It writes, it does not read.** Each screen already parses `window.location.search` once on mount (the
 * repo's idiom — no `useSearchParams`, so no Suspense boundary), and that is where a *stale* or *malformed* value
 * has to be tolerated. Splitting the two directions keeps this hook from having an opinion about defaults.
 *
 * ⚠️ **`replaceState`, never `push`.** Typing in a search box must not put a history entry behind every
 * keystroke — the back button has to leave the screen, not walk the filter back one character at a time.
 *
 * ⚠️ **Only the keys it is given.** A screen that consumes a one-shot deep-link param (`?appointmentId=`, which
 * `/appointments` deliberately strips so a refresh does not reopen a dialog) clears it before this runs, and this
 * never re-adds it: an absent key is dropped, a present one is written.
 *
 * @param values The filter keys and their current values. `undefined`, `null` and `""` are DROPPED, so a cleared
 *   filter leaves no `?k=` behind and the default view has a clean URL. Pass a stable object literal — the effect
 *   keys on the serialised value, not on identity.
 * @param enabled Set false while the screen is still hydrating its own state from the URL, so the first render
 *   cannot overwrite the incoming query string with the defaults it has not read yet.
 */
export function useUrlFilters(values: Record<string, string | number | boolean | null | undefined>, enabled = true) {
  // Serialised rather than compared by identity: every caller passes an inline literal, which is a new object on
  // every render — an identity-keyed effect would run constantly.
  const serialised = JSON.stringify(values)
  const lastWritten = useRef<string | null>(null)

  useEffect(() => {
    if (!enabled || typeof window === "undefined") return

    const next = new URLSearchParams()
    for (const [key, value] of Object.entries(JSON.parse(serialised) as Record<string, unknown>)) {
      if (value === undefined || value === null || value === "" || value === false) continue
      next.set(key, String(value))
    }

    const query = next.toString()
    const url = query ? `${window.location.pathname}?${query}` : window.location.pathname

    // Guarded so a re-render with identical filters does not touch history at all.
    if (lastWritten.current === url) return
    lastWritten.current = url
    window.history.replaceState({}, "", url)
  }, [serialised, enabled])
}

/**
 * The query string as it was on the FIRST client render, for seeding filter state from a link or a reload.
 *
 * ⚠️ Read in a lazy `useState` initialiser, not in an effect. An effect leaves one commit in which the state is
 * still the default — and the screens that use this fetch on their state, so that commit fires a request for the
 * unfiltered view and then a second one for the real filters, which is the race
 * `app/caisse/page.tsx` had to grow a request-generation guard for. On the server it is simply empty, so the
 * prerender and the first client paint agree on the defaults.
 *
 * ⚠️ Never re-read afterwards: it is a SEED. A later `replaceState` from {@link useUrlFilters} must not feed back
 * into the state that produced it.
 */
export function useUrlFilterSeed(): URLSearchParams {
  const [seed] = useState(() =>
    typeof window === "undefined" ? new URLSearchParams() : new URLSearchParams(window.location.search),
  )
  return seed
}
