import { useEffect, useRef } from "react"
import type { RealtimeResourceKey } from "./clinic-hub"
import { useClinicRealtimeSubscribe } from "./clinic-realtime-provider"

/**
 * Subscribes to clinic-scoped real-time change signals for one or more resources. Invokes `onChanged`
 * whenever the server broadcasts a change to one of `resources` for this clinic, and again after a
 * dropped connection reconnects (so the client catches up on anything missed while offline, AC-4).
 *
 * The server broadcasts a single `entityChanged` event carrying the changed resource key; this hook
 * filters to the resources the caller cares about, so an edit to an unrelated entity does not trigger
 * a needless refetch. `onChanged` receives the resource that changed (undefined on a reconnect
 * catch-up), so a page watching several resources can route each to its own refetch.
 *
 * It listens on the tab's ONE connection, owned by `ClinicRealtimeProvider` — calling it adds a listener,
 * never a socket. Outside the provider it does nothing, which is what an unreachable hub already meant here.
 *
 * Additive (AC-5): if the hub is unreachable the page keeps working via manual refresh — connection
 * failures are logged by SignalR, never surfaced.
 */
export function useClinicRealtime(
  resources: RealtimeResourceKey | RealtimeResourceKey[],
  onChanged: (resource?: RealtimeResourceKey) => void,
) {
  const subscribe = useClinicRealtimeSubscribe()

  // Hold the latest callback in a ref so a re-render of the caller doesn't resubscribe.
  const callbackRef = useRef(onChanged)
  callbackRef.current = onChanged

  // Stable primitive dependency: a fresh array literal each render would otherwise resubscribe every render.
  const resourceKey = (Array.isArray(resources) ? resources : [resources]).join(",")

  useEffect(() => {
    if (!subscribe) return

    const watched = new Set(resourceKey.split(",").filter(Boolean))

    return subscribe((resource) => {
      if (resource === undefined || watched.has(resource)) callbackRef.current(resource)
    })
  }, [subscribe, resourceKey])
}
