"use client"

import { useCallback, useEffect } from "react"
import { useRouter } from "next/navigation"
import type { UserStatusDto } from "@/lib/api/clinics"
import { useAuthToken } from "./use-auth-token"
import { useUserStatus } from "./use-user-status"

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
 * Reads the tab's shared clinic status (`useUserStatus`), so every guard, rail and picker costs one request together.
 *
 * @param redirectToSetup - Whether to redirect to setup page if no clinic (default: true)
 * @returns ClinicAccessState with access status and clinic information
 */
export function useClinicAccess(redirectToSetup: boolean = true): ClinicAccessState {
  const router = useRouter()
  const { accessToken, isLoading: authLoading } = useAuthToken()
  const query = useUserStatus(!authLoading && Boolean(accessToken))

  const status = query.data ?? null
  const hasAccess = status?.hasClinic === true
  // « Not a member » is the HTTP-200 `hasClinic: false` answer; a thrown read is transient (network / ≥ 500).
  const failed = query.isError && !status

  useEffect(() => {
    if (status && !hasAccess && redirectToSetup) router.push("/setup")
  }, [status, hasAccess, redirectToSetup, router])

  const { refetch } = query
  const refresh = useCallback(() => {
    void refetch()
  }, [refetch])

  if (authLoading) return { hasAccess: false, isLoading: true, status: null, error: null, refresh }
  if (!accessToken) return { hasAccess: false, isLoading: false, status: null, error: "Not authenticated", refresh }

  return {
    hasAccess,
    isLoading: query.isPending && !failed,
    status,
    // Never boot an authenticated member to /setup on a blip: a failed read is an error state, kept in place.
    error: failed ? query.error.message || "La vérification de l'accès au cabinet a échoué" : null,
    refresh,
  }
}
