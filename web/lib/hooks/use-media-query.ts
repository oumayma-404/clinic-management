"use client"

import { useEffect, useState } from "react"

/**
 * Subscribe to a CSS media query from JS.
 *
 * The app is CSS-only responsive everywhere it can be — a media query in a class name costs nothing and cannot
 * desynchronise from what is painted. This exists for the two things CSS genuinely cannot do:
 *
 *   1. Close the nav drawer when the layout crosses to the rail (EC-1). `md:hidden` hides the drawer's *content*
 *      while Radix's overlay, scroll lock and focus trap stay mounted — the page ends up untouchable with
 *      nothing on screen to explain why. Only JS can close it.
 *   2. Choose a prop rather than a class, e.g. where the toasts anchor. `<Toaster position>` is a value, not CSS.
 *
 * ⚠️ Returns `false` during SSR and on the very first client render, then the real value after mount. That is
 * deliberate: reading `matchMedia` while rendering would produce server/client markup that disagrees, and this
 * hook's callers must therefore treat `false` as "not yet known", never as "definitely a mouse". Anything that
 * must be right on the first paint belongs in CSS, not here.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(false)

  useEffect(() => {
    if (typeof window === "undefined" || typeof window.matchMedia !== "function") return

    const mql = window.matchMedia(query)
    const onChange = (event: MediaQueryListEvent) => setMatches(event.matches)

    setMatches(mql.matches)
    mql.addEventListener("change", onChange)
    return () => mql.removeEventListener("change", onChange)
  }, [query])

  return matches
}

/** The `md:` breakpoint, as JS sees it. Kept next to the hook so the value has one home. */
export const MD_BREAKPOINT_QUERY = "(min-width: 48rem)"

/** A finger, not a mouse — the JS twin of the `coarse:` CSS variant. */
export const COARSE_POINTER_QUERY = "(pointer: coarse)"
