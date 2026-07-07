"use client"

import { useRouter } from "next/navigation"
import { useEffect, useState } from "react"
import { useUser } from "@auth0/nextjs-auth0/client"
import SetupWizard from "@/components/setup-wizard"
import { clinicsApi } from "@/lib/api/clinics"
import { useAuthToken } from "@/lib/hooks/use-auth-token"

export default function SetupPage() {
  const router = useRouter()
  const { user, isLoading: userLoading } = useUser()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const [isChecking, setIsChecking] = useState(true)

  useEffect(() => {
    // Only check once when component mounts or auth state changes
    if (!isChecking) return // Already checked, don't check again
    
    checkUserStatus()
  }, [user, userLoading, accessToken, authLoading])

  const checkUserStatus = async () => {
    // Wait for auth to load
    if (userLoading || authLoading) {
      return
    }

    // If not authenticated, redirect to login
    if (!user || !accessToken) {
      window.location.href = "/auth/login?returnTo=/setup"
      return
    }

    try {
      const status = await clinicsApi.getUserStatus()
      if (status.hasClinic) {
        // User has clinic, redirect to app
        window.location.href = "/"
        return
      }
      // User doesn't have clinic, show setup wizard
      setIsChecking(false)
    } catch (err) {
      console.error("Error checking user status:", err)
      setIsChecking(false)
    }
  }

  if (userLoading || authLoading || isChecking) {
    return (
      <div className="min-h-screen flex items-center justify-center">
        <div className="text-center">
          <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600 mx-auto mb-4"></div>
          <p className="text-muted-foreground">Checking your clinic status...</p>
        </div>
      </div>
    )
  }

  return <SetupWizard onComplete={() => {}} />
}

