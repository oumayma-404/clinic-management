"use client"

import { createContext, useContext, useEffect, useState } from "react"
import { usePathname, useRouter } from "next/navigation"
import { loadAuthMode } from "@/lib/hooks/use-password-policy"
import { DEMO_VISIT_COOKIE } from "@/lib/demo/demo-cookie"
import { DEFAULT_DEMO_SIGNUP_URL, rememberDemoSignupUrl } from "@/lib/demo/demo-refusal"

/** A deliberate « Se déconnecter » is not an expiry: the next visit must not be told it expired. */
export function forgetDemoVisit() {
  document.cookie = `${DEMO_VISIT_COOKIE}=; Max-Age=0; path=/`
}

function hasDemoVisit(): boolean {
  return document.cookie.split(";").some((c) => c.trim() === `${DEMO_VISIT_COOKIE}=1`)
}

/**
 * The sign-in doors a demo has no use for: a visitor never holds a password, so each of these leads to the entry
 * screen instead (spec AC-1.5).
 */
const DOORS_CLOSED_ON_THE_DEMO = [
  "/login",
  "/setup",
  "/join",
  "/signup",
  "/signup/verifier",
  "/mot-de-passe-oublie",
  "/reinitialiser-mot-de-passe",
]

interface DemoState {
  /** `null` until the server has answered — never read as « not a demo » before then. */
  isDemo: boolean | null
  signupUrl: string
}

const DemoContext = createContext<DemoState>({ isDemo: null, signupUrl: DEFAULT_DEMO_SIGNUP_URL })

/**
 * Whether this deployment is the public demo (`demo-cabinet`), from the one cached `GET /api/auth/mode` read. Mounted
 * above the session, because the entry screen is read before anybody is signed in.
 */
export function DemoProvider({ children }: { children: React.ReactNode }) {
  const [state, setState] = useState<DemoState>({ isDemo: null, signupUrl: DEFAULT_DEMO_SIGNUP_URL })
  const pathname = usePathname()
  const router = useRouter()

  useEffect(() => {
    let active = true
    loadAuthMode()
      .then((mode) => {
        if (!active) return
        rememberDemoSignupUrl(mode.demoSignupUrl ?? "")
        setState({
          isDemo: mode.isDemo === true,
          signupUrl: mode.demoSignupUrl || DEFAULT_DEMO_SIGNUP_URL,
        })
      })
      // A failed probe is « not known to be a demo »: every real deployment keeps working as it always did.
      .catch(() => active && setState((previous) => ({ ...previous, isDemo: false })))
    return () => {
      active = false
    }
  }, [])

  useEffect(() => {
    if (state.isDemo && pathname && DOORS_CLOSED_ON_THE_DEMO.includes(pathname)) {
      // Only the middleware's bounce of a session-less visitor carries `returnTo`; after a visit, that is an expiry.
      const bounced = new URLSearchParams(window.location.search).has("returnTo")
      router.replace(bounced && hasDemoVisit() ? "/demo?expired=1" : "/demo")
    }
  }, [state.isDemo, pathname, router])

  return <DemoContext.Provider value={state}>{children}</DemoContext.Provider>
}

export function useDemo(): DemoState {
  return useContext(DemoContext)
}
