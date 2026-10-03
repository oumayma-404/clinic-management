"use client"

import type React from "react"
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"

import { onMoneyHidden } from "@/lib/api/client"
import { clinicsApi } from "@/lib/api/clinics"
import { useSession } from "@/lib/auth/session"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/** « Mode discret » as this browser last read it. `unknown` until the first read answers. */
export type MoneyVisibility = "unknown" | "shown" | "hidden"

export interface MoneyVisibilityState {
  visibility: MoneyVisibility
  /** May an amount be painted? False while `unknown`: a money screen waits rather than flashing figures. */
  moneyShown: boolean
  /** Are the money destinations withheld from the menus? Only once the server says so, or every reload flickers the rail. */
  moneyHidden: boolean
  /** Apply what the caller's own hide/show just returned, without waiting for the broadcast. */
  setVisibility: (visibility: Exclude<MoneyVisibility, "unknown">) => void
  refresh: () => void
}

const NO_PROVIDER: MoneyVisibilityState = {
  visibility: "unknown",
  moneyShown: false,
  moneyHidden: false,
  setVisibility: () => undefined,
  refresh: () => undefined,
}

const MoneyVisibilityContext = createContext<MoneyVisibilityState | null>(null)

/** SSR-tolerant: with no provider in scope it reports `unknown`, so nothing money-shaped is painted. */
export function useMoneyVisibility(): MoneyVisibilityState {
  return useContext(MoneyVisibilityContext) ?? NO_PROVIDER
}

/** Floor between two focus-triggered reads; a broadcast, a refusal and the first read bypass it. */
const MIN_FOCUS_REREAD_MS = 30_000
/** Retry while the first read has not answered — the money screens wait on it. */
const RETRY_WHILE_UNKNOWN_MS = 10_000

/** Re-reads on a `clinics` broadcast. A child so the hub connection exists only while somebody is signed in. */
function MoneyRealtimeListener({ onChange }: { onChange: () => void }) {
  useClinicRealtime(RealtimeResource.Clinics, onChange)
  return null
}

/**
 * Owns the cabinet's « Mode discret » for the whole app: the rail, the bottom bar, the three Finances pages and the
 * dashboard all read this one value, so they cannot disagree. The server enforces it too — this only decides what
 * is painted.
 *
 * <p>Four triggers: the first read, a `clinics` broadcast (another PC pressed the button), window focus (the hub
 * fails silently), and any 403 `money_hidden` (a screen opened before the hide).</p>
 */
export function MoneyVisibilityProvider({ children }: { children: React.ReactNode }) {
  const { user, isLoading: sessionLoading } = useSession()
  const signedIn = Boolean(user) && !sessionLoading

  const [visibility, setVisibilityState] = useState<MoneyVisibility>("unknown")
  const inFlight = useRef(false)
  const lastReadAtMs = useRef(0)
  const mounted = useRef(true)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  // Signing out forgets the answer: the next account may belong to another cabinet.
  useEffect(() => {
    if (!signedIn) setVisibilityState("unknown")
  }, [signedIn])

  const read = useCallback(
    (force: boolean) => {
      if (!signedIn || inFlight.current) return
      if (!force && Date.now() - lastReadAtMs.current < MIN_FOCUS_REREAD_MS) return
      inFlight.current = true
      lastReadAtMs.current = Date.now()
      clinicsApi
        .getUserStatus()
        .then((status) => {
          if (mounted.current) setVisibilityState(status.clinic?.isMoneyHidden === true ? "hidden" : "shown")
        })
        // A failed read keeps the last answer; `unknown` stays `unknown` and the retry below asks again.
        .catch(() => undefined)
        .finally(() => {
          inFlight.current = false
        })
    },
    [signedIn],
  )

  const refresh = useCallback(() => read(true), [read])

  useEffect(() => {
    if (signedIn) read(true)
  }, [signedIn, read])

  useEffect(() => {
    if (!signedIn || visibility !== "unknown") return
    const retry = setInterval(() => read(true), RETRY_WHILE_UNKNOWN_MS)
    return () => clearInterval(retry)
  }, [signedIn, visibility, read])

  useEffect(() => {
    if (!signedIn) return
    const onFocus = () => read(false)
    const onVisible = () => {
      if (document.visibilityState === "visible") read(false)
    }
    window.addEventListener("focus", onFocus)
    document.addEventListener("visibilitychange", onVisible)
    return () => {
      window.removeEventListener("focus", onFocus)
      document.removeEventListener("visibilitychange", onVisible)
    }
  }, [signedIn, read])

  // The refusal is authoritative: hide at once, then confirm.
  useEffect(
    () =>
      onMoneyHidden(() => {
        setVisibilityState("hidden")
        read(true)
      }),
    [read],
  )

  const setVisibility = useCallback((next: Exclude<MoneyVisibility, "unknown">) => setVisibilityState(next), [])

  const value = useMemo<MoneyVisibilityState>(
    () => ({
      visibility,
      moneyShown: visibility === "shown",
      moneyHidden: visibility === "hidden",
      setVisibility,
      refresh,
    }),
    [visibility, setVisibility, refresh],
  )

  return (
    <MoneyVisibilityContext.Provider value={value}>
      {signedIn && <MoneyRealtimeListener onChange={refresh} />}
      {children}
    </MoneyVisibilityContext.Provider>
  )
}
