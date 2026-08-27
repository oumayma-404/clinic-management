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

  /*
   * ⚠️ A « Vérification du statut de votre clinique… » spinner stood here, gated on `mode !== "local"` — i.e. it
   * rendered only on the Auth0-backed deployment, while that provider resolved an existing session before
   * first-run setup could decide whether to redirect. That kind is retired, every deployment is now "local", and
   * the branch was already unreachable on both compose files (which set AUTH_MODE=local). First-run setup has no
   * session to wait for, so rendering the wizard straight away is what it already did in practice.
   */
  return <SetupWizard onComplete={() => {}} />
}
