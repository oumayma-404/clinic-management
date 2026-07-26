"use client"

import { useEffect, useState } from "react"
import { useSession } from "@/lib/auth/session"
import SetupWizard from "@/components/setup-wizard"
import { clinicsApi } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"

export default function SetupPage() {
  const { user, isLoading: userLoading, mode } = useSession()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const [isChecking, setIsChecking] = useState(true)

  useEffect(() => {
    // Local mode: first-run setup needs no existing session — show the wizard directly.
    if (mode === "local") {
      setIsChecking(false)
      return
    }

    // Cloud mode: require an authenticated Auth0 user, then check clinic status.
    if (userLoading || authLoading) {
      return
    }

    if (!user || !accessToken) {
      window.location.href = "/auth/login?returnTo=/setup"
      return
    }

    let cancelled = false
    clinicsApi
      .getUserStatus()
      .then((status) => {
        if (cancelled) return
        if (status.hasClinic) {
          window.location.href = "/"
          return
        }
        setIsChecking(false)
      })
      .catch((err) => {
        if (cancelled) return
        console.error("Error checking user status:", err)
        setIsChecking(false)
      })
    return () => {
      cancelled = true
    }
  }, [user, userLoading, accessToken, authLoading, mode])

  if (mode !== "local" && (userLoading || authLoading || isChecking)) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
          <p className="text-muted-foreground">Vérification du statut de votre clinique…</p>
        </div>
      </div>
    )
  }

  return <SetupWizard onComplete={() => {}} />
}
