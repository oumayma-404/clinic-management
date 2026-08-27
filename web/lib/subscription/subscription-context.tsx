"use client"

import type React from "react"
import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState } from "react"

import { authApi } from "@/lib/api/auth"
import { ApiError, onSubscriptionRequired } from "@/lib/api/client"
import { subscriptionApi, type SubscriptionDto } from "@/lib/api/subscription"
import { getErrorMessage } from "@/lib/errors"
import { useSession } from "@/lib/auth/session"

export interface SubscriptionState {
  /**
   * The cabinet's entitlement as the server last answered it, or `null` while it is unknown — before the first
   * read, on a deployment that does not work by subscription, and after a read that failed.
   *
   * ⚠️ **A failed read leaves the last good value in place** rather than clearing it: this drives a banner, and a
   * banner that disappears on a network blip tells a cabinet three days from expiry that everything is fine.
   */
  subscription: SubscriptionDto | null
  /** Whether this deployment works by subscription at all (`requiresSubscription === true`). */
  enforced: boolean
  /**
   * The same question with its two undecided answers kept apart, for the screen that has to render them
   * differently: `"unknown"` before the probe settles, `"on"`/`"off"` once it has, and **`"unreadable"`** when it
   * failed — which EC-13 requires to be a *retryable* state and never « cette installation n'a pas d'abonnement ».
   * The banner and the rail row read {@link enforced} instead: for them a probe that could not answer is « off ».
   */
  enforcement: "unknown" | "on" | "off" | "unreadable"
  /**
   * The last read's failure, or null. `refresh` swallows failures on purpose (a banner must not vanish on a blip),
   * so this is how « Abonnement » — the one screen whose whole job is to show this state — can offer « Réessayer »
   * without issuing a second read of its own.
   */
  lastError: string | null
  /** The banner is hidden for the rest of the clinic day. Only ever true while the entitlement is still valid. */
  dismissed: boolean
  /** Hide the banner until the clinic day turns over. **Per browser, never a server write** (AC-3.2). */
  dismiss: () => void
  /**
   * Re-read now. The three FR-15 triggers call it, and « Abonnement » does too after a « Réessayer ».
   *
   * Throttled to one read a minute unless `force` is passed — the 402 path passes it, because a refusal is
   * evidence the state has just changed.
   */
  refresh: (force?: boolean) => void
}

const NOT_ENFORCED: SubscriptionState = {
  subscription: null,
  enforced: false,
  enforcement: "off",
  lastError: null,
  dismissed: false,
  dismiss: () => undefined,
  refresh: () => undefined,
}

const SubscriptionContext = createContext<SubscriptionState | null>(null)

/**
 * The cabinet's subscription state. SSR-tolerant and non-throwing, mirroring {@link useSession} and
 * `useConnectivity`: with no provider in scope it reports « not enforced », so a component that reads it during a
 * prerender pass — or in a unit render — never crashes and never paints a banner.
 */
export function useSubscription(): SubscriptionState {
  return useContext(SubscriptionContext) ?? NOT_ENFORCED
}

/**
 * How often the state is re-read **while a warning or an expiry is in force**. Nothing polls outside that window:
 * FR-15 bounds this per client, and a cabinet three months from its date has nothing to learn.
 *
 * <p>Five minutes is the answer to the two halves of FR-15's own sentence — rare enough not to be a load concern
 * on a backend serving every cabinet, frequent enough that US-5's « working again within minutes » is true of a
 * grant the vendor has just recorded.</p>
 */
const REREAD_INTERVAL_MS = 5 * 60_000

/**
 * The floor between two reads, whatever asked for them.
 *
 * <p>Without it the focus/`visibilitychange` trigger made the doc above false: it is gated on `enforced && signedIn`
 * and not on the warning window, so every alt-tab on every enforced cabinet issued a `GET /api/subscription` —
 * « nothing polls outside that window » was true of the interval alone. `inFlight` only collapses *concurrent*
 * reads, so it also did not bound a 402 burst spread over a second or repeated tab switching.</p>
 */
const MIN_REREAD_GAP_MS = 60_000

/** One entry, overwritten. Holds the clinic-day key the banner was last dismissed for — see {@link clinicDayKey}. */
const DISMISSED_STORAGE_KEY = "clinic-subscription:banner-dismissed"

/**
 * A value that changes exactly when the cabinet's clinic day turns over, derived from the server's own two
 * numbers — the inclusive end date and the whole clinic-local days left before it.
 *
 * <p>⚠️ <b>Deliberately not a date computed here.</b> « The next clinic day » is a fact about Tunis, and the
 * browser is the one participant that cannot know it: a workstation set to any other timezone would turn the
 * banner back on hours early or late, which is the same defect `todayLocalIso()` exists to prevent one layer
 * over. Pairing the two server values needs no clock at all — `daysRemaining` decrements at Tunisian midnight,
 * and `endsOn` moving (a grant, a cancellation) is also a state the cabinet should be shown again.</p>
 *
 * <p>Returns `null` when the banner is not dismissible anyway: an expired or open-ended entitlement has no
 * countdown, and AC-3.3 gives the expired one no dismiss control at all.</p>
 */
function clinicDayKey(subscription: SubscriptionDto | null): string | null {
  if (!subscription || subscription.endsOn === null || subscription.daysRemaining === null) return null
  return `${subscription.endsOn}|${subscription.daysRemaining}`
}

/**
 * Owns FR-15's re-read: the app learns its subscription changed by **asking**, never by being told.
 *
 * <p><b>⚠️ A broadcast was not available, and that is why this exists.</b> Neither moment that changes the state
 * can push one — a vendor's grant runs in a separate process with no caller's token to derive a clinic from, and
 * an entitlement ending at midnight (EC-1) has no actor at all. `Subscriptions` is on
 * `RealtimeResourceResolver.ExcludedAreas` for the same reason. So there are exactly three triggers:</p>
 *
 * <ol>
 *   <li><b>An interval, only while a warning or an expiry is in force.</b> This is what makes AC-5.8 true: a
 *       cabinet that has just paid is working again within one interval, with nobody signing out.</li>
 *   <li><b>Window focus</b> — the cheap one, and the one that covers the tablet picked back up after lunch.</li>
 *   <li><b>Any 402</b>, through {@link onSubscriptionRequired}. The refused save *is* the event for EC-1: midnight
 *       passes mid-consultation, the save is refused, and the banner appears with no reload.</li>
 * </ol>
 *
 * <p><b>Mounted inside the session provider</b> (`app/layout.tsx`), because every read here is authenticated. It
 * fetches nothing until a user exists, and nothing but `auth/mode` — one capability probe per session — where
 * `requiresSubscription` is not `true`. ⚠️ Stated precisely on purpose: the earlier « nothing at all » was what a
 * reader would rely on when reasoning about the unenforced deployments, and it was never true of the probe.</p>
 */
export function SubscriptionProvider({ children }: { children: React.ReactNode }) {
  const { user, isLoading: sessionLoading } = useSession()

  const [enforcement, setEnforcement] = useState<SubscriptionState["enforcement"]>("unknown")
  const [subscription, setSubscription] = useState<SubscriptionDto | null>(null)
  const [lastError, setLastError] = useState<string | null>(null)
  const [dismissedDay, setDismissedDay] = useState<string | null>(null)

  // Guards a burst: one page load can raise several 402s at once, and each must not open its own read.
  const inFlight = useRef(false)
  const lastReadAtMs = useRef(0)
  const mounted = useRef(true)

  useEffect(() => {
    mounted.current = true
    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    setDismissedDay(readDismissedDay())
  }, [])

  const signedIn = Boolean(user) && !sessionLoading
  // The boolean every consumer but « Abonnement » wants: a probe that could not answer reads as « off », which is
  // the fail-safe direction — no banner invented from a network hiccup.
  const enforced = enforcement === "on"

  // The capability probe, once per session. `=== true` following `publicSignupEnabled`'s convention: an older API
  // answers `undefined`, and the safe direction is « no subscription » — how the other two deployment kinds behave.
  useEffect(() => {
    if (!signedIn) return
    let cancelled = false
    authApi
      .getMode()
      .then((mode) => {
        if (!cancelled) setEnforcement(mode.requiresSubscription === true ? "on" : "off")
      })
      // A failed probe leaves the feature off rather than guessing it on: a banner is a claim about the cabinet's
      // access, and inventing one from a network hiccup is worse than being a few minutes late with a real one.
      // It is recorded as `unreadable` rather than `off` all the same — « we could not ask » and « this deployment
      // has no subscriptions » are different facts, and « Abonnement » has to offer a retry for the first (EC-13).
      .catch(() => {
        if (!cancelled) setEnforcement("unreadable")
      })
    return () => {
      cancelled = true
    }
  }, [signedIn])

  const refresh = useCallback(
    (force = false) => {
      // Gated on `signedIn`, not on `enforced`: the 402 listener below forces a read and turns enforcement on from
      // the refusal itself, so requiring `enforced` here would make that path unreachable after a failed probe.
      if (!signedIn || inFlight.current) return
      if (!force && Date.now() - lastReadAtMs.current < MIN_REREAD_GAP_MS) return
      inFlight.current = true
      lastReadAtMs.current = Date.now()
      subscriptionApi
        .get()
        .then((value) => {
          if (!mounted.current) return
          setSubscription(value)
          setLastError(null)
          setEnforcement("on")
        })
        .catch((err) => {
          // A 404 is the server saying this deployment does not work by subscription — the backstop for a probe that
          // could not answer, and the one failure that genuinely turns the feature off. Every other failure keeps the
          // last known state (EC-13's reasoning, one layer down): a banner must not vanish on a dropped connection.
          if (!mounted.current) return
          if (err instanceof ApiError && err.status === 404) {
            setEnforcement("off")
            setSubscription(null)
            setLastError(null)
            return
          }
          setLastError(getErrorMessage(err))
        })
        .finally(() => {
          inFlight.current = false
        })
    },
    [signedIn],
  )

  // Trigger 0 — the first read, once the deployment is known to enforce and somebody is signed in. Forced past the
  // throttle: it is the read that populates the state, and there is nothing yet to be fresh.
  useEffect(() => {
    if (enforced && signedIn) refresh(true)
  }, [enforced, signedIn, refresh])

  const warningInForce = subscription !== null && (subscription.shouldWarn || !subscription.allowsWrites)

  // Trigger 1 — the interval, and only inside the window. Outside it this effect installs no timer at all.
  useEffect(() => {
    if (!enforced || !signedIn || !warningInForce) return
    const interval = setInterval(() => refresh(true), REREAD_INTERVAL_MS)
    return () => clearInterval(interval)
  }, [enforced, signedIn, warningInForce, refresh])

  // Trigger 2 — window focus. `visibilitychange` rides along because a native shell returning from the background
  // does not reliably raise `focus`, and that is the same event by another name; `inFlight` makes the overlap free.
  useEffect(() => {
    if (!enforced || !signedIn) return
    const onFocus = () => refresh()
    const onVisible = () => {
      if (document.visibilityState === "visible") refresh()
    }
    window.addEventListener("focus", onFocus)
    document.addEventListener("visibilitychange", onVisible)
    return () => {
      window.removeEventListener("focus", onFocus)
      document.removeEventListener("visibilitychange", onVisible)
    }
  }, [enforced, signedIn, refresh])

  // Trigger 3 — any 402, and it is **authoritative**: a refusal carrying a subscription code is positive proof that
  // this deployment enforces, so it turns `enforced` on itself rather than waiting for a probe that may already have
  // failed. Without that, one failed `auth/mode` read meant no banner, no rail row and every 402 re-read dropped for
  // the whole session — which is precisely EC-1's case (midnight passing mid-consultation). Forced past the
  // throttle for the same reason. The 404 branch in `refresh` remains the fail-safe `enforced` was standing in for.
  useEffect(
    () =>
      onSubscriptionRequired(() => {
        setEnforcement("on")
        refresh(true)
      }),
    [refresh],
  )

  const currentDay = clinicDayKey(subscription)
  // AC-3.3 rides on `currentDay` being null for an expired entitlement, so an expired banner cannot be dismissed
  // even by a value already in storage from when the cabinet was still valid.
  const dismissed = currentDay !== null && dismissedDay === currentDay

  const dismiss = useCallback(() => {
    const day = clinicDayKey(subscription)
    if (day === null) return
    setDismissedDay(day)
    writeDismissedDay(day)
  }, [subscription])

  // Memoized for `CloudBridge`'s stated reason: an inline literal hands consumers a new identity on every provider
  // render, so the banner and the whole `DashboardSidebar` (which rebuilds `buildNavSections` each render) re-render
  // on every five-minute poll — `setSubscription` stores a fresh object whose contents are usually identical.
  const value = useMemo(
    () => ({ subscription, enforced, enforcement, lastError, dismissed, dismiss, refresh }),
    [subscription, enforced, enforcement, lastError, dismissed, dismiss, refresh],
  )

  return <SubscriptionContext.Provider value={value}>{children}</SubscriptionContext.Provider>
}

/** `localStorage` throws in a locked-down browser and in private mode on some engines — a banner is not worth it. */
function readDismissedDay(): string | null {
  try {
    return window.localStorage.getItem(DISMISSED_STORAGE_KEY)
  } catch {
    return null
  }
}

function writeDismissedDay(day: string): void {
  try {
    window.localStorage.setItem(DISMISSED_STORAGE_KEY, day)
  } catch {
    // The dismissal stays in React state for this page's life, which is the whole benefit minus persistence.
  }
}
