"use client"

import type React from "react"
import { Auth0Provider, useUser } from "@auth0/nextjs-auth0/client"
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"
import { clinicsApi } from "@/lib/api/clinics"
import { clearCachedAccessToken } from "@/lib/api/client"

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

  const logout = useCallback((options?: { returnTo?: string }) => {
    // AC-3.6: logout returns to the login screen; the configured server address
    // (NEXT_PUBLIC_API_URL / shell config) is untouched.
    // Drop the in-memory access token too: the full navigation below would discard it anyway, but the
    // cache must not outlive the session on any path that stops short of reloading the page.
    clearCachedAccessToken()
    /*
     * AC-42 — an INACTIVITY logout remembers where the user was, a deliberate one does not.
     *
     * Coming back to a phone after lunch and being dropped on the dashboard, having lost the fiche that was
     * open, is the part of a timeout that actually costs work. A user who *chose* « Se déconnecter » is
     * finished with the screen, so passing no `returnTo` is the right default.
     */
    const target = options?.returnTo
      ? `/login?returnTo=${encodeURIComponent(options.returnTo)}`
      : "/login"
    fetch("/bff/auth/local-logout", { method: "POST" })
      .catch(() => {})
      .finally(() => {
        window.location.href = target
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

  /*
   * Inactivity auto-logout — only armed while logged in (AC-42).
   *
   * ⚠️ **This used to be a security hole, not an inconvenience.** The old version stored nothing but the
   * `setTimeout` handle, so the limit was only ever enforced by a timer *running*. A backgrounded or frozen
   * tab — a phone locked in a pocket, exactly the case a clinic cares about — has its timers throttled or
   * suspended, so the callback simply never fired and the session stayed **open past the limit**. And because
   * `reset()` re-armed the *full* 30 minutes on every event, the first mousemove after coming back silently
   * extended the session rather than ending it.
   *
   * The fix is to make **wall-clock time the authority** and the timer only a wake-up call:
   * `lastActivityAtMs` is the fact, `arm()` derives the delay from it, and every path — a real event, the tab
   * becoming visible, the timer firing early because the browser felt like it — re-derives instead of
   * assuming. A timer that fires late or not at all can then only *delay* the logout to the next wake-up,
   * never skip it.
   *
   * ⚠️ Local-only, deliberately: Cloud has **no** inactivity timer at all (Auth0 owns session lifetime
   * there), so this provider is the only place the rule exists.
   */
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const lastActivityAtMs = useRef(Date.now())

  useEffect(() => {
    if (!user) return

    const expireNow = () => {
      // The screen the user was on, so the timeout does not also cost them their place (AC-42).
      logout({ returnTo: window.location.pathname + window.location.search })
    }

    const arm = () => {
      if (timerRef.current) clearTimeout(timerRef.current)
      const remaining = INACTIVITY_LIMIT_MS - (Date.now() - lastActivityAtMs.current)
      if (remaining <= 0) {
        expireNow()
        return
      }
      // On fire, `arm` runs again rather than logging out directly: if the browser delivered the callback
      // early (or the clock moved), the next line re-checks the real elapsed time instead of trusting it.
      timerRef.current = setTimeout(arm, remaining)
    }

    const noteActivity = () => {
      lastActivityAtMs.current = Date.now()
      arm()
    }

    const events: (keyof WindowEventMap)[] = ["mousemove", "keydown", "click", "scroll", "touchstart"]
    events.forEach((e) => window.addEventListener(e, noteActivity, { passive: true }))

    /*
     * ⚠️ `visibilitychange` is on `document`, not `window`, so it cannot join the array above — and it is
     * **not** activity: coming back to the tab must re-check the clock, never restart it. This is the event
     * that actually closes the hole, because it is the first thing to run when a locked phone is unlocked.
     */
    const onVisibility = () => {
      if (document.visibilityState === "visible") arm()
    }
    document.addEventListener("visibilitychange", onVisibility)

    noteActivity()

    return () => {
      events.forEach((e) => window.removeEventListener(e, noteActivity))
      document.removeEventListener("visibilitychange", onVisibility)
      if (timerRef.current) clearTimeout(timerRef.current)
    }
  }, [user, logout])

  const value: SessionState = { user, isLoading, mode: "local", logout }
  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}
