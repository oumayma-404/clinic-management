"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import {
  teamApi,
  type MyStaffLeaveDto,
  type StaffFileDto,
  type StaffLeaveListFilter,
  type StaffLeaveRequestsPageDto,
  type TeamLeaveOverviewDto,
} from "@/lib/api/team"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

export interface TeamReadState<T> {
  data: T | null
  loading: boolean
  /** The read failed and there is nothing to show — never rendered as an empty list. */
  failed: boolean
  refetch: () => void
}

type KeyedTeamReadState<T> = TeamReadState<T> & {
  /** The key the shown `data` was read for — it outlives a key change until the new read lands. */
  dataKey: string | null
}

/** Latest request wins; a `team` broadcast refreshes it on every open screen. A null key reads nothing. */
function useTeamRead<T>(key: string | null, read: () => Promise<T>): KeyedTeamReadState<T> {
  const [data, setData] = useState<T | null>(null)
  const [dataKey, setDataKey] = useState<string | null>(null)
  const [loading, setLoading] = useState(Boolean(key))
  const [failed, setFailed] = useState(false)
  const readRef = useRef(read)
  readRef.current = read
  const sequence = useRef(0)
  // A refresh that fails over data already read for this key keeps it rather than replacing it with a failure.
  const readKey = useRef<string | null>(null)

  const refetch = useCallback(() => {
    if (!key) {
      setData(null)
      setLoading(false)
      setFailed(false)
      return
    }
    const ticket = ++sequence.current
    setLoading(true)
    readRef
      .current()
      .then((value) => {
        if (ticket !== sequence.current) return
        readKey.current = key
        setData(value)
        setDataKey(key)
        setFailed(false)
      })
      .catch(() => {
        if (ticket === sequence.current && readKey.current !== key) setFailed(true)
      })
      .finally(() => {
        if (ticket === sequence.current) setLoading(false)
      })
  }, [key])

  useEffect(() => {
    refetch()
  }, [refetch])

  useClinicRealtime(RealtimeResource.Team, refetch)

  return { data, loading, failed, refetch, dataKey }
}

/** The signed-in secretary's balance and requests. */
export function useMyStaffLeave(enabled: boolean): TeamReadState<MyStaffLeaveDto> {
  return useTeamRead(enabled ? "me" : null, () => teamApi.myLeave())
}

/** The cabinet's requests for the admin — `pending` unpaged, `decided` one page at a time. */
export function useStaffLeaveRequests(
  enabled: boolean,
  filter: StaffLeaveListFilter,
  page?: number,
  pageSize?: number,
): TeamReadState<StaffLeaveRequestsPageDto> {
  const { dataKey, ...state } = useTeamRead(enabled ? `${filter}|${page ?? ""}|${pageSize ?? ""}` : null, () =>
    teamApi.listLeaveRequests(filter, page, pageSize),
  )
  // Another filter's rows are not this filter's; another page of the same filter may stay while the next one loads.
  return dataKey?.startsWith(`${filter}|`) ? state : { ...state, data: null }
}

/** Each active secretary's balance and next congé, for the admin's team strip. */
export function useTeamLeaveOverview(enabled: boolean): TeamReadState<TeamLeaveOverviewDto> {
  return useTeamRead(enabled ? "overview" : null, () => teamApi.overview())
}

/** One secretary's file for the admin. */
export function useStaffFile(userId: string | null): TeamReadState<StaffFileDto> {
  return useTeamRead(userId, () => teamApi.staffFile(userId as string))
}

/** The « 1 » beside « Utilisateurs »: requests waiting for an admin; 0 while unknown or switched off. */
export function usePendingLeaveCount(enabled: boolean): number {
  const { data } = useTeamRead(enabled ? "pending-count" : null, () => teamApi.pendingCount())
  return enabled ? (data?.count ?? 0) : 0
}
