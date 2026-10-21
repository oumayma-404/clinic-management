"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { doctorsApi, type DoctorLeaveDto } from "@/lib/api/doctors"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

export interface DoctorLeavesState {
  leaves: DoctorLeaveDto[]
  loading: boolean
  /** The read failed and there is nothing to show — never rendered as « aucun congé ». */
  failed: boolean
  refetch: () => void
}

/** Shared read loop: latest request wins, a `doctorleaves` broadcast refreshes it on every open screen. */
function useLeavesRead(key: string | null, read: () => Promise<DoctorLeaveDto[]>): DoctorLeavesState {
  const [leaves, setLeaves] = useState<DoctorLeaveDto[]>([])
  const [loading, setLoading] = useState(Boolean(key))
  const [failed, setFailed] = useState(false)
  const readRef = useRef(read)
  readRef.current = read
  const sequence = useRef(0)
  // A refresh that fails over a list already read for this key keeps that list rather than replacing it.
  const readKey = useRef<string | null>(null)

  const refetch = useCallback(() => {
    if (!key) {
      setLeaves([])
      setLoading(false)
      setFailed(false)
      return
    }
    const ticket = ++sequence.current
    setLoading(true)
    readRef
      .current()
      .then((rows) => {
        if (ticket !== sequence.current) return
        readKey.current = key
        setLeaves(rows)
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

  useClinicRealtime(RealtimeResource.DoctorLeaves, refetch)

  return { leaves, loading, failed, refetch }
}

/** One practitioner's congés, earliest first — the « Congés » card. */
export function useDoctorLeaves(doctorId: string | null | undefined): DoctorLeavesState {
  return useLeavesRead(doctorId ?? null, () => doctorsApi.getLeaves(doctorId as string))
}

/** The cabinet's congés touching an inclusive day window — what the agenda draws. */
export function useClinicLeaves(fromDay: string, toDay: string): DoctorLeavesState {
  return useLeavesRead(`${fromDay}|${toDay}`, () => doctorsApi.getClinicLeaves(fromDay, toDay))
}
