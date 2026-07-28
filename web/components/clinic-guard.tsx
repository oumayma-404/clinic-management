"use client"

import { useEffect } from "react"
import { usePathname } from "next/navigation"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import { useSession } from "@/lib/auth/session"
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
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const { mode } = useSession()
  const { hasAccess, isLoading: clinicLoading, error, refresh } = useClinicAccess(false) // Don't auto-redirect

  // Don't show guard on setup/join/login pages
  const isSetupPage = pathname === "/setup" || pathname === "/join" || pathname === "/login"

  // Redirect to login if not authenticated — to the login page that exists in THIS mode (AC-P3.19/3.21),
  // carrying a returnTo that actually renders (AC-P3.20).
  useEffect(() => {
    if (!authLoading && !accessToken && !isSetupPage) {
      const returnTo = safeReturnTo(pathname)
      window.location.href = `${LOGIN_PATH[mode]}?returnTo=${encodeURIComponent(returnTo)}`
    }
  }, [authLoading, accessToken, isSetupPage, pathname, mode])

  if (isSetupPage) {
    return <>{children}</>
  }

  // Show loading while checking
  if (authLoading || clinicLoading) {
    return (
      fallback || (
        <div className="min-h-screen flex items-center justify-center">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
            <p className="text-muted-foreground">Chargement…</p>
          </div>
        </div>
      )
    )
  }

  // If not authenticated, redirect will happen via useEffect
  if (!accessToken) {
    return (
      fallback || (
        <div className="min-h-screen flex items-center justify-center">
          <div className="text-center">
            <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
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
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-center max-w-md px-4">
          <p className="text-lg font-medium mb-2">Connexion au serveur impossible</p>
          <p className="text-muted-foreground mb-6">
            Impossible de vérifier votre accès pour le moment. Vérifiez votre connexion et réessayez.
          </p>
          <button
            onClick={refresh}
            className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            Réessayer
          </button>
        </div>
      </div>
    )
  }

  // User doesn't have clinic, show unauthorized page
  return <UnauthorizedPage />
}

