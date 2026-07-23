"use client"

import type React from "react"
import { Auth0Provider, useUser } from "@auth0/nextjs-auth0/client"
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"
import { clinicsApi } from "@/lib/api/clinics"

export type AuthMode = "cloud" | "local"

export interface SessionUser {
  name?: string
  email?: string
  picture?: string
  role?: string
}

export interface SessionState {
  user: SessionUser | null
  isLoading: boolean
  mode: AuthMode
  /** Logs the user out (Auth0 redirect in Cloud; clears the local cookie in Local). */
  logout: () => void
}

const SessionContext = createContext<SessionState | null>(null)

const DEFAULT_SESSION: SessionState = {
  user: null,
  isLoading: true,
  mode: "cloud",
  logout: () => {},
}

/**
 * Unified session hook — works in both Cloud (Auth0) and Local (offline) modes.
 * Returns a safe "loading" default when no provider is in scope (e.g. during the
 * static prerender pass, before client hydration), rather than throwing.
 */
export function useSession(): SessionState {
  return useContext(SessionContext) ?? DEFAULT_SESSION
}

// ---------------------------------------------------------------------------
// Cloud (Auth0) — bridges Auth0's useUser into the unified SessionContext.
// ---------------------------------------------------------------------------
function CloudBridge({ children }: { children: React.ReactNode }) {
  const { user, isLoading } = useUser()

  // Auth0's profile does not carry the clinic role, so resolve it server-side once the user is known:
  // GET /api/clinics/user-status returns the DB-resolved role. Without this, role-gated admin UI —
  // reminder settings, CNAM nomenclature, medication & dental-act catalogs — was unreachable in Cloud
  // because `role` stayed unset here (feature cloud-security-and-tenant-isolation, AC-1).
  const [role, setRole] = useState<string | undefined>(undefined)

  useEffect(() => {
    if (!user) {
      setRole(undefined)
      return
    }
    let active = true
    clinicsApi
      .getUserStatus()
      .then((status) => {
        if (active) setRole(status.role ?? status.user?.role ?? undefined)
      })
      .catch(() => {
        // A membership/token hiccup must not break the session; role stays unset (non-admin UI).
        if (active) setRole(undefined)
      })
    return () => {
      active = false
    }
  }, [user?.email])

  // Memoize so the context value (and the mapped user object) keep a stable identity across
  // re-renders; otherwise every CloudBridge render hands consumers a new `user`, needlessly
  // retriggering effects like useAuthToken's token fetch.
  const value = useMemo<SessionState>(
    () => ({
      user: user
        ? { name: user.name ?? undefined, email: user.email ?? undefined, picture: user.picture ?? undefined, role }
        : null,
      isLoading: Boolean(isLoading),
      mode: "cloud",
      logout: () => {
        window.location.href = "/auth/logout"
      },
    }),
    [user?.name, user?.email, user?.picture, isLoading, role],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function CloudSessionProvider({ children }: { children: React.ReactNode }) {
  return (
    <Auth0Provider>
      <CloudBridge>{children}</CloudBridge>
    </Auth0Provider>
  )
}

// ---------------------------------------------------------------------------
// Local (offline) — cookie-backed session + inactivity auto-logout.
// ---------------------------------------------------------------------------
const INACTIVITY_LIMIT_MS = 30 * 60 * 1000 // AC-3.5: default 30 minutes

export function LocalSessionProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)

  const logout = useCallback(() => {
    // AC-3.6: logout returns to the login screen; the configured server address
    // (NEXT_PUBLIC_API_URL / shell config) is untouched.
    fetch("/bff/auth/local-logout", { method: "POST" })
      .catch(() => {})
      .finally(() => {
        window.location.href = "/login"
      })
  }, [])

  useEffect(() => {
    let active = true
    fetch("/bff/auth/session", { credentials: "include" })
      .then((res) => {
        if (res.status === 401) {
          // Stale/expired session cookie — clear it so the API client stops attaching an
          // expired bearer token (which would otherwise 401 every call with no recovery).
          fetch("/bff/auth/local-logout", { method: "POST" }).catch(() => {})
          return null
        }
        return res.ok ? res.json() : null
      })
      .then((data) => {
        if (active) setUser(data?.user ?? null)
      })
      .catch(() => {
        if (active) setUser(null)
      })
      .finally(() => {
        if (active) setIsLoading(false)
      })
    return () => {
      active = false
    }
  }, [])

  // Inactivity auto-logout — only armed while logged in.
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  useEffect(() => {
    if (!user) return

    const reset = () => {
      if (timerRef.current) clearTimeout(timerRef.current)
      timerRef.current = setTimeout(() => logout(), INACTIVITY_LIMIT_MS)
    }

    const events: (keyof WindowEventMap)[] = ["mousemove", "keydown", "click", "scroll", "touchstart"]
    events.forEach((e) => window.addEventListener(e, reset, { passive: true }))
    reset()

    return () => {
      events.forEach((e) => window.removeEventListener(e, reset))
      if (timerRef.current) clearTimeout(timerRef.current)
    }
  }, [user, logout])

  const value: SessionState = { user, isLoading, mode: "local", logout }
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
