"use client"

import type React from "react"
import { toast } from "sonner"
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"
import { clinicsApi } from "@/lib/api/clinics"
import {
  clearCachedAccessToken,
  onMustChangePassword,
  onSecondFactorRequired,
  onSessionExpired,
} from "@/lib/api/client"
import { canConfirmIdentityInShell, SessionLockGate } from "@/components/session-lock-gate"
import { idleLimitMinutes } from "@/lib/auth/idle-limit"
import { isPublicRoute } from "@/lib/auth/public-routes"

/**
 * ⚠️ One member, and that is the point.
 *
 * The seam is kept — the server still publishes AUTH_MODE and consumers still read `useSession().mode` — but
 * Auth0 ("cloud") is retired along with the CloudBrowser deployment kind, so exactly one thing issues tokens
 * now: this product. Narrowing the union rather than leaving a member nothing can produce is what turns a
 * stale `mode === "cloud"` into a compile error instead of a branch that silently never runs.
 */
export type AuthMode = "local"

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
  /**
   * Whether this session was opened with « Rester connecté sur cet appareil ».
   *
   * <p>Exposed so « Mes appareils » and the header can say so. It is a **description of the session**, never a
   * permission: nothing in the app should branch on it to decide what a user may do.</p>
   */
  trusted: boolean
  /** Logs the user out — clears the local session cookie and returns to the login screen. */
  logout: () => void
}

const SessionContext = createContext<SessionState | null>(null)

const DEFAULT_SESSION: SessionState = {
  user: null,
  isLoading: true,
  mode: "local",
  // ⚠️ The loading default is the SHORT session, not the long one. This value is read before the cookie has been
  // decoded, and being wrong in this direction costs a user one extra sign-in; being wrong in the other leaves an
  // unattended browser open for eight hours because a fetch had not resolved yet.
  trusted: false,
  logout: () => {},
}

/**
 * The session hook.
 * Returns a safe "loading" default when no provider is in scope (e.g. during the
 * static prerender pass, before client hydration), rather than throwing.
 */
export function useSession(): SessionState {
  return useContext(SessionContext) ?? DEFAULT_SESSION
}

// ---------------------------------------------------------------------------
// Local (offline) — cookie-backed session + inactivity auto-logout.
// ---------------------------------------------------------------------------

/*
 * ⚠️ **The limit is no longer a constant, and the reason is in `lib/auth/idle-limit.ts`.** It used to be a flat
 * 30 minutes on every surface, which meant a dentist treating one patient with the app open on the operatory PC
 * was signed out mid-appointment — cookie cleared, session revoked, back to a password and a six-digit code from
 * a phone that is across the room. The wait now depends on what expiry actually *costs*: seconds where the shell
 * can lock, a full sign-in where it cannot.
 */

export function LocalSessionProvider({ children }: { children: React.ReactNode }) {
  const [user, setUser] = useState<SessionUser | null>(null)
  const [isLoading, setIsLoading] = useState(true)
  const [trusted, setTrusted] = useState(false)

  const logout = useCallback((options?: { returnTo?: string; reason?: "inactivity" }) => {
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
    /*
     * The reason is sent so the session table can tell a chosen sign-out from a timeout — until now both were
     * stamped identically, and the practice's own records could not answer how often the limit was firing.
     * `Content-Type` is set explicitly: without it the route's `request.json()` still parses, but a proxy that
     * strips an unlabelled body would silently turn every timeout back into « demandée par l'utilisateur ».
     */
    fetch("/bff/auth/local-logout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ reason: options?.reason ?? "user" }),
    })
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
        if (!active) return
        setUser(data?.user ?? null)
        // Absent ⇒ false. A BFF that predates this field, or any answer we could not read, must give the
        // SHORT session rather than the long one.
        setTrusted(data?.trusted === true)
      })
      .catch(() => {
        if (!active) return
        setUser(null)
        setTrusted(false)
      })
      .finally(() => {
        if (active) setIsLoading(false)
      })
    return () => {
      active = false
    }
  }, [])

  /*
   * A forced password change is a destination, not an error message (AC-76).
   *
   * The login path is already covered: `/bff/auth/local-login` writes `local_must_change_password` and
   * `middleware.ts` holds the user on `/change-password` until it is gone. What that cookie cannot cover is an
   * admin resetting the password of somebody **already signed in** — no login happens, so no cookie is written,
   * and every call from then on 403s. Routing here rather than in `client.ts` keeps navigation out of the data
   * layer, the same split `<ClientVersionGate>` uses for the 426.
   *
   * ⚠️ A full navigation, not `router.push`: the same middleware gate must run, and the in-memory access token
   * must not survive into the new state. And it is guarded on the current path — the change-password screen
   * itself makes calls, and a redirect to the page you are on is a reload loop.
   */
  useEffect(() => {
    return onMustChangePassword(() => {
      if (window.location.pathname.startsWith("/change-password")) return
      // Same reason as the two below: a public door has no signed-in account, so a refusal there cannot be
      // « your password must change » — and bouncing a visitor mid-signup onto that screen is the same defect
      // wearing a different destination.
      if (isPublicRoute(window.location.pathname)) return
      window.location.href = "/change-password"
    })
  }, [])

  /*
   * « Session expirée » — the token exchange refused the session outright (401/403).
   *
   * ⚠️ **The state this replaces is the worst one the app can be in.** Once renewal is refused, every request
   * 401s and every screen shows its own generic error: an application that looks completely usable and does
   * nothing, indefinitely, with no route back to sign-in. It reads as « the software is broken » rather than
   * « you have been signed out », which is precisely why it was never reported as the latter.
   *
   * `returnTo` is passed, unlike a deliberate sign-out: the user did not choose this, and coming back to the
   * fiche that was open is the difference between an interruption and lost work — the same reasoning the
   * inactivity timeout already applies.
   *
   * ⚠️ **Guarded on every PUBLIC route, not just `/login`, and the difference is a production outage.** The
   * guard used to read `startsWith("/login")`, which is true of exactly one of the seven doors a visitor reaches
   * with no session. On the other six a 401 from the token exchange is not an expiry at all — it is the API
   * correctly refusing a request that carries no cookie, because nobody is signed in yet — and treating it as
   * one told a visitor three fields into creating their cabinet that their session had expired, then dropped
   * them on the login screen. Public signup was unusable: `SetupWizard` read the profile-image upload policy
   * (an authenticated endpoint) on mount, and every attempt ended on `/login?returnTo=%2Fsignup`.
   *
   * The list is `lib/auth/public-routes.ts`, shared with `middleware.ts` — the server gate and this one now
   * answer « does a session belong here? » from the same place, which is what stops the next door added to one
   * from being missed by the other.
   *
   * And `onSessionExpired` deliberately never fires for a 429 or a transport blip — see its own note — so one
   * rate-limited minute cannot eject a whole practice.
   */
  useEffect(() => {
    return onSessionExpired(() => {
      if (isPublicRoute(window.location.pathname)) return
      toast.error("Session expirée", {
        description: "Votre session a expiré. Reconnectez-vous pour continuer.",
        duration: 6000,
      })
      logout({ returnTo: window.location.pathname + window.location.search })
    })
  }, [logout])

  /*
   * The same shape one requirement along (`hosted-security-hardening` FR-1.2): an administrator this
   * deployment obliges to hold a second factor, who has none.
   *
   * ⚠️ **A refusal with no destination is an app that looks usable and is dead**, which is why this is not
   * optional. The requirement is re-checked per request, so it does not only arrive at sign-in — a session
   * that predates it, or an account promoted to administrator while signed in, meets it in the middle of
   * ordinary work, on whatever call happens to be next.
   *
   * It carries the address so the enrolment step opens with it already filled: the user has just been told
   * they cannot proceed, and asking them to retype what the app already knows is where people give up.
   *
   * Guarded on the public routes exactly as the one above is, and for both of its reasons: `/login` itself makes
   * calls, and on a door reached without a session a refusal says nothing about anybody's second factor.
   */
  useEffect(() => {
    return onSecondFactorRequired(() => {
      if (isPublicRoute(window.location.pathname)) return
      const address = user?.email ? `&email=${encodeURIComponent(user.email)}` : ""
      window.location.href = `/login?enrol=1${address}`
    })
  }, [user?.email])

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
   * ⚠️ This is the only place the rule exists — there is no second session implementation left for it
   * to drift from, the Auth0-backed one having been retired.
   */
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const lastActivityAtMs = useRef(Date.now())

  /*
   * In a native shell the limit **pauses** the session instead of ending it (AC-57): `<SessionLockGate>` covers
   * the app, asks the OS to confirm the device owner, and on success re-arms the timer with the cookie, the page
   * and the user's place all untouched. Three unsuccessful attempts — or a device that cannot ask — fall through
   * to the ordinary logout below.
   *
   * ⚠️ `locked` is a **dependency of the effect**, not a flag read inside it. Tearing the listeners down is the
   * point: while the gate is up, a tap on it must not count as activity (that would extend the very session the
   * limit just paused) and the timer must not fire a second expiry behind it.
   *
   * ⚠️ Absent bridge ⇒ this is never entered and the path below is byte-identical to what it was (AC-58).
   */
  const [locked, setLocked] = useState(false)

  const resumeFromLock = useCallback(() => {
    lastActivityAtMs.current = Date.now()
    setLocked(false)
  }, [])

  // Deliberately does **not** clear `locked`: `logout` clears the cookie and then navigates, and uncovering the
  // app for those few frames would put the record back on screen at the one moment nobody has confirmed anything.
  const abandonLock = useCallback(() => {
    logout({ returnTo: window.location.pathname + window.location.search, reason: "inactivity" })
  }, [logout])

  useEffect(() => {
    if (!user || locked) return

    /*
     * ⚠️ Recomputed inside the effect, not hoisted to a constant: `canConfirmIdentityInShell()` reads
     * `window.__clinicShell`, which does not exist during the server render and is installed by the shell at
     * document start. Read once at module scope it would be `false` for the life of the page even in a shell
     * that can lock — the limit would be right by luck on the web and wrong on the device the lock exists for.
     *
     * The effect re-runs on `user` and on `locked`, which covers every point at which either input can change.
     */
    const canLock = canConfirmIdentityInShell()
    const limitMs = idleLimitMinutes(trusted, canLock) * 60 * 1000

    const expireNow = () => {
      if (canLock) {
        setLocked(true)
        return
      }
      // The screen the user was on, so the timeout does not also cost them their place (AC-42).
      logout({ returnTo: window.location.pathname + window.location.search, reason: "inactivity" })
    }

    const arm = () => {
      if (timerRef.current) clearTimeout(timerRef.current)
      const remaining = limitMs - (Date.now() - lastActivityAtMs.current)
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
  }, [user, locked, logout, trusted])

  const value: SessionState = { user, isLoading, mode: "local", trusted, logout }
  return (
    <SessionContext.Provider value={value}>
      {children}
      {/* Rendered over the still-mounted app, never instead of it — resuming to the fiche that was open is the
          whole point, and unmounting `children` would reload the page the resume exists to preserve. */}
      {locked && <SessionLockGate onConfirmed={resumeFromLock} onFallBackToPassword={abandonLock} />}
    </SessionContext.Provider>
  )
}
