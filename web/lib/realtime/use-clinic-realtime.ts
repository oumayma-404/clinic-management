import { useEffect, useRef } from "react"
import type { HubConnection } from "@microsoft/signalr"
import { ENTITY_CHANGED_EVENT, createClinicHubConnection, type RealtimeResourceKey } from "./clinic-hub"

/**
 * Subscribes to clinic-scoped real-time change signals for one or more resources. Invokes `onChanged`
 * whenever the server broadcasts a change to one of `resources` for this clinic, and again after a
 * dropped connection reconnects (so the client catches up on anything missed while offline, AC-4).
 *
 * The server broadcasts a single `entityChanged` event carrying the changed resource key; this hook
 * filters to the resources the caller cares about, so an edit to an unrelated entity does not trigger
 * a needless refetch. `onChanged` receives the resource that changed (undefined on a reconnect
 * catch-up), so a page watching several resources can route each to its own refetch over ONE
 * connection instead of calling this hook (and opening a socket) per resource.
 *
 * Additive (AC-5): if the hub is unreachable the page keeps working via manual refresh — connection
 * failures are logged by SignalR, never surfaced, and the initial connect is retried until unmount.
 */
export function useClinicRealtime(
  resources: RealtimeResourceKey | RealtimeResourceKey[],
  onChanged: (resource?: RealtimeResourceKey) => void,
) {
  // Hold the latest callback in a ref so a re-render of the caller doesn't tear down and rebuild the
  // connection; the effect connects exactly once per mount (per distinct resource set).
  const callbackRef = useRef(onChanged)
  callbackRef.current = onChanged

  // Stable primitive dependency: re-subscribe only when the resource set actually changes, not on
  // every render (a fresh array literal each render would otherwise thrash the connection).
  const resourceKey = (Array.isArray(resources) ? resources : [resources]).join(",")

  useEffect(() => {
    let connection: HubConnection | null = createClinicHubConnection()
    if (!connection) return

    const watched = new Set(resourceKey.split(",").filter(Boolean))

    let disposed = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    connection.on(ENTITY_CHANGED_EVENT, (resource: string) => {
      if (watched.has(resource)) callbackRef.current(resource as RealtimeResourceKey)
    })
    // withAutomaticReconnect resumes the connection after a drop; refetch on reconnect to catch up on
    // any change missed while disconnected (the server sends no backlog). No specific resource → all.
    connection.onreconnected(() => callbackRef.current(undefined))

    const start = async () => {
      try {
        await connection!.start()
      } catch {
        // withAutomaticReconnect only covers drops AFTER a successful connect; retry the FIRST connect
        // ourselves (server not up yet / transient offline) until the component unmounts.
        if (!disposed) {
          retryTimer = setTimeout(start, 5000)
        }
      }
    }
    void start()

    return () => {
      disposed = true
      if (retryTimer) clearTimeout(retryTimer)
      connection?.stop().catch(() => {})
      connection = null
    }
  }, [resourceKey])
}
