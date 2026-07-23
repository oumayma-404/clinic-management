"use client"

import { useEffect } from "react"
import { usePathname } from "next/navigation"
import { useClinicAccess } from "@/lib/hooks/use-clinic-access"
import { useAuthToken } from "@/lib/hooks/use-auth-token"
import UnauthorizedPage from "./unauthorized-page"

interface ClinicGuardProps {
  children: React.ReactNode
  fallback?: React.ReactNode
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
  const { hasAccess, isLoading: clinicLoading, error, refresh } = useClinicAccess(false) // Don't auto-redirect

  // Don't show guard on setup/join/login pages
  const isSetupPage = pathname === "/setup" || pathname === "/join" || pathname === "/login"

  // Redirect to login if not authenticated
  useEffect(() => {
    if (!authLoading && !accessToken && !isSetupPage) {
      window.location.href = `/auth/login?returnTo=${encodeURIComponent(pathname)}`
    }
  }, [authLoading, accessToken, isSetupPage, pathname])

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

