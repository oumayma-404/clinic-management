import { useEffect, useRef } from "react"
import type { HubConnection } from "@microsoft/signalr"
import { APPOINTMENTS_CHANGED_EVENT, createClinicHubConnection } from "./clinic-hub"

/**
 * Subscribes to clinic-scoped real-time appointment changes. Invokes `onAppointmentsChanged` whenever
 * the server broadcasts a change to this clinic, and again after a dropped connection reconnects (so
 * the client catches up on anything missed while offline, AC-4).
 *
 * Additive (AC-5): if the hub is unreachable the page keeps working via manual refresh — connection
 * failures are logged by SignalR, never surfaced, and the initial connect is retried until unmount.
 */
export function useClinicRealtime(onAppointmentsChanged: () => void) {
  // Hold the latest callback in a ref so a re-render of the caller doesn't tear down and rebuild the
  // connection; the effect connects exactly once per mount.
  const callbackRef = useRef(onAppointmentsChanged)
  callbackRef.current = onAppointmentsChanged

  useEffect(() => {
    let connection: HubConnection | null = createClinicHubConnection()
    if (!connection) return

    let disposed = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    connection.on(APPOINTMENTS_CHANGED_EVENT, () => callbackRef.current())
    connection.onreconnected(() => callbackRef.current())

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
  }, [])
}
