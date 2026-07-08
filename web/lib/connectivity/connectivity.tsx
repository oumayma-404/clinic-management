"use client"

import type React from "react"
import { createContext, useContext, useEffect, useRef, useState } from "react"
import { toast } from "sonner"
import { useSession } from "@/lib/auth/session"

export interface ConnectivityState {
  /** The clinic server responded to the last poll (even with an error status). */
  serverReachable: boolean
  /** The server reports it can reach the internet — AI chat + Google Calendar depend on this. */
  internetReachable: boolean
  /** True only in Local (offline-LAN) mode; Cloud is statically online and never polls. */
  isLocal: boolean
}

// Cloud (and SSR / no-provider) default: everything online, so all consumers behave exactly as today.
const ONLINE_DEFAULT: ConnectivityState = {
  serverReachable: true,
  internetReachable: true,
  isLocal: false,
}

const ConnectivityContext = createContext<ConnectivityState | null>(null)

/**
 * Connectivity signal. SSR-tolerant and non-throwing (mirrors {@link useSession}): returns the
 * online default when no provider is in scope, so components that read it never crash during the
 * prerender pass or in Cloud mode.
 */
export function useConnectivity(): ConnectivityState {
  return useContext(ConnectivityContext) ?? ONLINE_DEFAULT
}

const POLL_INTERVAL_MS = 15_000
// Debounce state transitions so a flapping connection doesn't thrash the UI / spam toasts (AC-6.3).
const DEBOUNCE_MS = 3_000

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000/api"

type Reachability = { serverReachable: boolean; internetReachable: boolean }

export function ConnectivityProvider({ children }: { children: React.ReactNode }) {
  const { mode } = useSession()
  const isLocal = mode === "local"

  const [state, setState] = useState<ConnectivityState>({ ...ONLINE_DEFAULT, isLocal })

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Last snapshot actually applied to state — used to (a) skip no-op transitions and (b) decide the
  // toast direction. Starts "online" so the first poll only notifies when something is actually down.
  const appliedRef = useRef<Reachability>({ serverReachable: true, internetReachable: true })

  useEffect(() => {
    // Cloud mode: never poll. Supply the static online default so AI chat + Google controls are
    // enabled exactly as before Phase 3 (R-3).
    if (!isLocal) {
      setState({ ...ONLINE_DEFAULT, isLocal: false })
      return
    }

    let active = true

    const applyDebounced = (next: Reachability) => {
      const prev = appliedRef.current
      if (prev.serverReachable === next.serverReachable && prev.internetReachable === next.internetReachable) {
        return // no change — leave any pending debounce alone
      }
      if (debounceRef.current) clearTimeout(debounceRef.current)
      debounceRef.current = setTimeout(() => {
        if (!active) return
        appliedRef.current = next
        setState({ ...next, isLocal: true })
        notifyTransition(next)
      }, DEBOUNCE_MS)
    }

    const poll = async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/connectivity`, { credentials: "include" })
        // Any HTTP response ⇒ the server is reachable; the body carries internet reachability.
        let internetReachable = false
        if (res.ok) {
          const data = await res.json().catch(() => null)
          internetReachable = Boolean(data?.internetReachable)
        }
        applyDebounced({ serverReachable: true, internetReachable })
      } catch {
        // Network/fetch failure ⇒ the clinic server itself is unreachable.
        applyDebounced({ serverReachable: false, internetReachable: false })
      }
    }

    poll()
    const interval = setInterval(poll, POLL_INTERVAL_MS)
    return () => {
      active = false
      clearInterval(interval)
      if (debounceRef.current) clearTimeout(debounceRef.current)
    }
  }, [isLocal])

  return <ConnectivityContext.Provider value={state}>{children}</ConnectivityContext.Provider>
}

function notifyTransition(next: Reachability) {
  if (!next.serverReachable) {
    toast.error("Serveur injoignable", {
      description: "Impossible de joindre le serveur de la clinique. Vérifiez votre connexion au réseau local.",
    })
  } else if (!next.internetReachable) {
    toast.warning("Pas de connexion internet", {
      description: "L'assistant IA et Google Agenda sont temporairement désactivés. Les autres fonctions restent disponibles.",
    })
  } else {
    toast.success("Connexion internet rétablie", {
      description: "L'assistant IA et Google Agenda sont de nouveau disponibles.",
    })
  }
}
