"use client"

import { useEffect } from "react"
import { usePathname } from "next/navigation"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"
import { Button } from "@/components/ui/button"
import UnauthorizedPage from "./unauthorized-page"

interface ClinicGuardProps {
  children: React.ReactNode
  fallback?: React.ReactNode
}

/**
 * Where a session expiry sends the user, per mode (AC-P3.19/3.21).
 *
 * `/auth/login` is an Auth0 route that only exists in Cloud — there is no `app/auth/login/page.tsx`. In Local
 * mode the hardcoded redirect therefore dumped an expired session on a Next 404 (§ 7.2), which is also where
 * `middleware.ts` would never have sent them: it redirects to `/login`. Deriving the target from the mode is
 * what keeps the Cloud path byte-for-byte unchanged.
 */
const LOGIN_PATH: Record<"cloud" | "local", string> = {
  cloud: "/auth/login",
  local: "/login",
}

/**
 * Routes with no page of their own must never be a `returnTo` target (AC-P3.20): signing in would land the
 * user on a 404 immediately after authenticating. `/auth/*` is Auth0 plumbing and `/bff/*` is the frontend's
 * own token/session API — neither renders anything.
 */
function safeReturnTo(pathname: string): string {
  if (!pathname || !pathname.startsWith("/")) return "/"
  if (pathname === "/login" || pathname.startsWith("/auth/") || pathname.startsWith("/bff/")) return "/"
  return pathname
}

/**
 * Component that protects routes by ensuring user has clinic access.
 * Shows unauthorized page if user doesn't have a clinic.
 * Redirects to login if user is not authenticated.
 */
export function ClinicGuard({ 
  children, 
  fallback
}: ClinicGuardProps) {
  const pathname = usePathname()
  const { accessToken, isLoading: authLoading, tokenError } = useAuthToken()
  const { mode } = useSession()
  const { hasAccess, isLoading: clinicLoading, error, refresh } = useClinicAccess(false) // Don't auto-redirect

  // Don't show guard on setup/join/login pages
  const isSetupPage = pathname === "/setup" || pathname === "/join" || pathname === "/login"

  // Redirect to login if not authenticated — to the login page that exists in THIS mode (AC-P3.19/3.21),
  // carrying a returnTo that actually renders (AC-P3.20).
  //
  // Only when the session is actually over. A token the server merely could not issue right now
  // (`tokenError === 'unavailable'`: offline, rate-limited, 5xx) is NOT a sign-out: redirecting on it sent
  // the user to /login, which still saw a session and pushed straight back here — the redirect loop. Those
  // get the retry screen below instead.
  useEffect(() => {
    if (!authLoading && !accessToken && tokenError !== 'unavailable' && !isSetupPage) {
      const returnTo = safeReturnTo(pathname)
      window.location.href = `${LOGIN_PATH[mode]}?returnTo=${encodeURIComponent(returnTo)}`
    }
  }, [authLoading, accessToken, tokenError, isSetupPage, pathname, mode])

  if (isSetupPage) {
    return <>{children}</>
  }

  // Show loading while checking
  if (authLoading || clinicLoading) {
    return (
      fallback || (
        <div className="min-h-dvh flex items-center justify-center">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
            <p className="text-muted-foreground">Chargement…</p>
          </div>
        </div>
      )
    )
  }

  // The server could not issue a token right now (offline, rate-limited, 5xx). The session is probably
  // intact, so say so and offer a retry instead of bouncing to /login — which is what used to spin.
  if (!accessToken && tokenError === 'unavailable') {
    return (
      <div className="min-h-dvh flex items-center justify-center">
        <div className="text-center max-w-md px-4">
          <p className="text-lg font-medium mb-2">Connexion au serveur impossible</p>
          <p className="text-muted-foreground mb-6">
            Le serveur de la clinique n&apos;a pas pu confirmer votre session. Vérifiez qu&apos;il est démarré,
            puis réessayez.
          </p>
          {/* The primitive, not a hand-rolled button: same paint, plus the 44px `touch-target` hit area and
              the press feedback. This is the ONLY interactive element on a full-screen blocking error state —
              there is no other way out of this screen — and it was a 36px target. */}
          <Button onClick={() => window.location.reload()}>Réessayer</Button>
        </div>
      </div>
    )
  }

  // If not authenticated, redirect will happen via useEffect
  if (!accessToken) {
    return (
      fallback || (
        <div className="min-h-dvh flex items-center justify-center">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-primary mx-auto mb-4"></div>
            <p className="text-muted-foreground">Redirection vers la connexion…</p>
          </div>
        </div>
      )
    )
  }

  // Simple check: if user has clinic (hasClinic === true), let them in
  // Only show unauthorized if explicitly no clinic (hasClinic === false)
  if (hasAccess) {
    // User has clinic, render children
    return <>{children}</>
  }

  // Transient failure (network / >=500) — NOT a legitimate "not a member". "Not a member" is the
  // HTTP-200 hasClinic:false path (error === null). Show a distinct retry state and keep the user in
  // place instead of booting an authenticated member to the "Access Restricted" screen (AC-9).
  if (error) {
    return (
      <div className="min-h-dvh flex items-center justify-center">
        <div className="text-center max-w-md px-4">
          <p className="text-lg font-medium mb-2">Connexion au serveur impossible</p>
          <p className="text-muted-foreground mb-6">
            Impossible de vérifier votre accès pour le moment. Vérifiez votre connexion et réessayez.
          </p>
          {/* Same reasoning as the retry above — the sole control on a blocking screen. */}
          <Button onClick={refresh}>Réessayer</Button>
        </div>
      </div>
    )
  }

  // User doesn't have clinic, show unauthorized page
  return <UnauthorizedPage />
}

