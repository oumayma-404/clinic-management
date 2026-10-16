/**
 * The ONE place this app writes browser history by hand (`check:responsive` N53).
 *
 * Next.js keeps its own marker in every history entry's state (`__NA` + its route tree) and treats a Back onto an
 * entry WITHOUT it as a page it does not know: it reloads the whole document. A raw `history.replaceState({}, …)`
 * erased that marker, so after a full page load, closing any guarded dialog (`useDirtyGuard` pops its entry with
 * `history.back()`) reloaded the page — and with it whatever the user had open. Every write here keeps it.
 */

/** Changes the address bar without a navigation, keeping the entry's state. */
export function replaceUrl(url: string | URL) {
  window.history.replaceState(window.history.state, "", url)
}

/**
 * Adds an entry for the current page (a dialog's « Back closes me »). It carries Next's own keys (they start with
 * `__`) so popping onto it is not a reload, and never another guard's marker, whose owner reads it to know where it is.
 */
export function pushMarker(marker: Record<string, unknown>) {
  const current: unknown = window.history.state
  const next: Record<string, unknown> = {}
  if (current && typeof current === "object") {
    for (const [key, value] of Object.entries(current)) if (key.startsWith("__")) next[key] = value
  }
  window.history.pushState({ ...next, ...marker }, "")
}
