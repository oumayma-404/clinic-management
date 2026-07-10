"use client"

import { useEffect, useState, useCallback } from "react"
import { useRouter } from "next/navigation"
import { clinicsApi, type UserStatusDto } from "@/lib/api/clinics"
import { useAuthToken } from "./use-auth-token"

export interface ClinicAccessState {
  hasAccess: boolean
  isLoading: boolean
  status: UserStatusDto | null
  error: string | null
  refresh: () => void
}

/**
 * Hook to check if the current user has access to a clinic.
 * Automatically redirects to setup if user doesn't have a clinic.
 * 
 * @param redirectToSetup - Whether to redirect to setup page if no clinic (default: true)
 * @returns ClinicAccessState with access status and clinic information
 */
export function useClinicAccess(redirectToSetup: boolean = true) {
  const router = useRouter()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  
  const [state, setState] = useState<Omit<ClinicAccessState, 'refresh'>>({
    hasAccess: false,
    isLoading: true,
    status: null,
    error: null,
  })

  const checkClinicAccess = useCallback(async () => {
    // Wait for auth to finish loading
    if (authLoading) {
      return
    }

    // If not authenticated, don't check clinic access
    if (!accessToken) {
      setState({
        hasAccess: false,
        isLoading: false,
        status: null,
        error: "Not authenticated",
      })
      return
    }

    try {
      // Check user status - simple check: if hasClinic is true, user has access
      const status = await clinicsApi.getUserStatus()

      // Simple check: if user has clinic (created or joined), they have access
      const hasAccess = status.hasClinic === true

      setState({
        hasAccess,
        isLoading: false,
        status,
        error: null,
      })

      // Redirect to setup if no clinic and redirect is enabled
      if (!hasAccess && redirectToSetup) {
        router.push("/setup")
      }
    } catch (err: any) {
      console.error("Error checking clinic access:", err)
      // A thrown error here is a transient ApiError (status 0 network / >=500) — "not a member" is
      // the HTTP-200 hasClinic:false success path above. Surface an error/retry state and keep the
      // user in place; never boot an authenticated member to /setup on a blip.
      setState({
        hasAccess: false,
        isLoading: false,
        status: null,
        error: err.message || "Failed to check clinic access",
      })
    }
  }, [accessToken, authLoading, redirectToSetup, router])

  useEffect(() => {
    checkClinicAccess()
  }, [checkClinicAccess])

  return {
    ...state,
    refresh: checkClinicAccess,
  }
}
