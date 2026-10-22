"use client"

import { useQuery, useQueryClient } from "@tanstack/react-query"
import { useCallback } from "react"
import { clinicsApi, type UserStatusDto } from "@/lib/api/clinics"
import { useSession } from "@/lib/auth/session"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"

const FRESH_FOR_MS = 5 * 60_000

/**
 * The caller's clinic status — clinic, doctors, user, opening hours, « Mode discret » — read once per tab and shared
 * (it used to be fetched 6× on `/appointments`). Refreshed by the `clinics` / `users` / `doctors` broadcasts.
 * ⚠️ A « no clinic yet » answer is never served from cache: right after `/setup` or `/join` it is the stale one.
 */
export function useUserStatus(enabled: boolean = true) {
  const { user, isLoading } = useSession()

  return useQuery({
    queryKey: queryKeys.userStatus,
    queryFn: () => clinicsApi.getUserStatus(),
    enabled: enabled && Boolean(user) && !isLoading,
    staleTime: (query) => (query.state.data?.hasClinic === true ? FRESH_FOR_MS : 0),
    meta: realtimeMeta.userStatus,
  })
}

/** For a read that must be fresh (« Mode discret », a just-saved setting): fetches, and shares the answer with the cache. */
export function useFetchUserStatus(): () => Promise<UserStatusDto> {
  const queryClient = useQueryClient()

  return useCallback(
    () =>
      queryClient.fetchQuery({
        queryKey: queryKeys.userStatus,
        queryFn: () => clinicsApi.getUserStatus(),
        staleTime: 0,
        meta: realtimeMeta.userStatus,
      }),
    [queryClient],
  )
}
