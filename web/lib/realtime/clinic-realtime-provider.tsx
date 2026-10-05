"use client"

import { createContext, useCallback, useContext, useEffect, useRef } from "react"
import type { HubConnection } from "@microsoft/signalr"
import { useSession } from "@/lib/auth/session"
import { ENTITY_CHANGED_EVENT, createClinicHubConnection, type RealtimeResourceKey } from "./clinic-hub"

/** Receives the resource that changed, or `undefined` after a reconnect (catch up on everything). */
export type RealtimeListener = (resource?: RealtimeResourceKey) => void

type Subscribe = (listener: RealtimeListener) => () => void

const ClinicRealtimeContext = createContext<Subscribe | null>(null)

/**
 * The tab's ONE SignalR connection, shared by every `useClinicRealtime` caller (`features/performance-caching` 2a).
 * It used to be one per hook call — 35 call sites, so 3–5 sockets per screen, rebuilt on every sidebar click.
 * ⚠️ Connects only while a user is signed in, and starts over when the user changes, so a shared reception PC
 * never keeps a socket authenticated as the previous person.
 */
export function ClinicRealtimeProvider({ children }: { children: React.ReactNode }) {
  const { user } = useSession()
  const identity = user?.email ?? null
  const listeners = useRef(new Set<RealtimeListener>())

  const subscribe = useCallback<Subscribe>((listener) => {
    listeners.current.add(listener)
    return () => {
      listeners.current.delete(listener)
    }
  }, [])

  useEffect(() => {
    if (!identity) return

    let connection: HubConnection | null = createClinicHubConnection()
    if (!connection) return

    let disposed = false
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    // A copy, so a listener that unsubscribes while being called cannot skip its neighbour.
    const emit = (resource?: RealtimeResourceKey) => [...listeners.current].forEach((listener) => listener(resource))

    connection.on(ENTITY_CHANGED_EVENT, (resource: string) => emit(resource as RealtimeResourceKey))
    // The server sends no backlog, so a reconnect tells every listener to catch up.
    connection.onreconnected(() => emit(undefined))

    const start = async () => {
      try {
        await connection!.start()
      } catch {
        // withAutomaticReconnect only covers drops AFTER a first connect; retry that one ourselves.
        if (!disposed) retryTimer = setTimeout(start, 5000)
      }
    }
    void start()

    return () => {
      disposed = true
      if (retryTimer) clearTimeout(retryTimer)
      connection?.stop().catch(() => {})
      connection = null
    }
  }, [identity])

  return <ClinicRealtimeContext.Provider value={subscribe}>{children}</ClinicRealtimeContext.Provider>
}

/** The shared connection's subscribe function; null outside the provider (nothing is live there). */
export function useClinicRealtimeSubscribe(): Subscribe | null {
  return useContext(ClinicRealtimeContext)
}
