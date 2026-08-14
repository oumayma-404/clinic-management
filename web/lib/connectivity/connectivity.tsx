"use client"

import type React from "react"
import { createContext, useContext, useEffect, useRef, useState } from "react"
import { toast } from "sonner"

export interface ConnectivityState {
  /** The clinic server answered the last poll — any HTTP status counts. Judged on **every** deployment (AC-62). */
  serverReachable: boolean
  /**
   * The internet-dependent features (AI chat, Google Agenda) may be used.
   *
   * ⚠️ **True when this deployment publishes no egress probe.** An absent signal is *absent*, never "offline"
   * (AC-63) — reading a 404 as `false` is what pinned a hosted clinic's AI chat and Google controls off for ever.
   */
  internetReachable: boolean
  /** The server actually answered the egress probe. False ⇒ `internetReachable` is an assumption, not a reading. */
  egressSignalAvailable: boolean
}

/** SSR / no-provider default: everything on, nothing claimed about egress. */
const ONLINE_DEFAULT: ConnectivityState = {
  serverReachable: true,
  internetReachable: true,
  egressSignalAvailable: false,
}

const ConnectivityContext = createContext<ConnectivityState | null>(null)

/**
 * Connectivity signal. SSR-tolerant and non-throwing (mirrors {@link useSession}): returns the
 * online default when no provider is in scope, so components that read it never crash during the
 * prerender pass.
 */
export function useConnectivity(): ConnectivityState {
  return useContext(ConnectivityContext) ?? ONLINE_DEFAULT
}

const POLL_INTERVAL_MS = 15_000
// Debounce state transitions so a flapping connection doesn't thrash the UI / spam toasts (AC-6.3).
const DEBOUNCE_MS = 3_000

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || "http://localhost:5000/api"

type Reachability = Pick<ConnectivityState, "serverReachable" | "internetReachable" | "egressSignalAvailable">

/**
 * Polls `GET /api/connectivity` on every deployment and derives the **two independent axes** (AC-62).
 *
 * ⚠️ **The poll is no longer gated on `AUTH_MODE`, and that was the defect.** `mode === "local"` reads true both
 * on a clinic's own PC (`SelfHostedLan`, which publishes the probe) *and* on the hosted multi-tenant backend
 * (`HostedMultiTenant`, where `ConnectivityController` gates on `ExposesTrustEndpoints` and **404s**). The old
 * code treated any non-OK response as `internetReachable: false`, so on a hosted deployment the AI chat and the
 * Google controls went dark permanently behind a French warning telling a dentist on cellular to check their
 * *local network*. The mode was never the right question: what matters is whether the server answers at all
 * (axis 1) and, separately, whether it published an egress reading (axis 2).
 *
 * | Poll outcome | serverReachable | egressSignalAvailable | internetReachable |
 * |---|---|---|---|
 * | 200 `{internetReachable:true}`  | true  | true  | true  |
 * | 200 `{internetReachable:false}` | true  | true  | false |
 * | 404 — no probe on this deployment | true | false | **true** (AC-63) |
 * | any other non-200 | true | false | true |
 * | `fetch` threw | **false** | false | false |
 *
 * ⚠️ Only a **200 that says so** means "no egress". Everything short of that is an absent signal, which must not
 * disable a feature. The one exception is the last row: a server we cannot reach keeps the pre-existing
 * `SelfHostedLan` behaviour byte-for-byte (plan R-4b), and the « Serveur injoignable » branch owns the wording
 * there anyway.
 */
export function ConnectivityProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<ConnectivityState>(ONLINE_DEFAULT)

  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Last snapshot actually applied to state — used to (a) skip no-op transitions and (b) decide the
  // toast direction. Starts "online" so the first poll only notifies when something is actually down.
  const appliedRef = useRef<Reachability>(ONLINE_DEFAULT)

  useEffect(() => {
    let active = true

    const applyDebounced = (next: Reachability) => {
      const prev = appliedRef.current
      if (
        prev.serverReachable === next.serverReachable &&
        prev.internetReachable === next.internetReachable &&
        prev.egressSignalAvailable === next.egressSignalAvailable
      ) {
        return // no change — leave any pending debounce alone
      }
      if (debounceRef.current) clearTimeout(debounceRef.current)
      debounceRef.current = setTimeout(() => {
        if (!active) return
        appliedRef.current = next
        setState(next)
        notifyTransition(prev, next)
      }, DEBOUNCE_MS)
    }

    const poll = async () => {
      try {
        const res = await fetch(`${API_BASE_URL}/connectivity`, { credentials: "include" })
        if (res.ok) {
          const data = await res.json().catch(() => null)
          // A 200 whose body we cannot read tells us nothing about egress — treat it as absent, not as false.
          if (data && typeof data.internetReachable === "boolean") {
            applyDebounced({
              serverReachable: true,
              internetReachable: data.internetReachable,
              egressSignalAvailable: true,
            })
            return
          }
        }
        applyDebounced({ serverReachable: true, internetReachable: true, egressSignalAvailable: false })
      } catch {
        // Network/fetch failure ⇒ the clinic server itself is unreachable.
        applyDebounced({ serverReachable: false, internetReachable: false, egressSignalAvailable: false })
      }
    }

    poll()
    const interval = setInterval(poll, POLL_INTERVAL_MS)
    return () => {
      active = false
      clearInterval(interval)
      if (debounceRef.current) clearTimeout(debounceRef.current)
    }
  }, [])

  return <ConnectivityContext.Provider value={state}>{children}</ConnectivityContext.Provider>
}

/**
 * ⚠️ Recovery is reported against **which axis actually recovered** (AC-64). One `else` branch used to say
 * « Connexion internet rétablie » for both, so a LAN server coming back announced the internet.
 */
function notifyTransition(prev: Reachability, next: Reachability) {
  if (!next.serverReachable) {
    toast.error("Serveur injoignable", {
      // Never « réseau local » (AC-64): the same server is reached over a LAN, over Wi-Fi and over a mobile
      // network, and naming one of them sends a dentist on cellular to look at the wrong thing.
      description: "Impossible de joindre le serveur. Vérifiez votre connexion, puis réessayez.",
    })
    return
  }
  if (next.egressSignalAvailable && !next.internetReachable) {
    toast.warning("Pas de connexion internet", {
      description:
        "Le serveur n'a pas accès à internet. Google Agenda est temporairement désactivé ; les autres fonctions restent disponibles.",
    })
    return
  }
  if (!prev.serverReachable) {
    toast.success("Connexion au serveur rétablie")
    return
  }
  // Only reachable from a real egress reading that flipped back — never from a signal that was merely absent.
  if (prev.egressSignalAvailable && !prev.internetReachable) {
    toast.success("Connexion internet rétablie", {
      description: "Google Agenda est de nouveau disponible.",
    })
  }
}
