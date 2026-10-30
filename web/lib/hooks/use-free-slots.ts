"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { appointmentsApi } from "@/lib/api/appointments"
import type { FreeDayDto } from "@/lib/api/types"

export interface FreeSlotsState {
  /** Null until read, and after a failed read — never an empty list standing in for « je n'ai pas pu lire ». */
  days: FreeDayDto[] | null
  loading: boolean
  failed: boolean
  reload: () => void
}

/**
 * Where a visit can move to, from the server's free-slot read (the save's own rules). `from` is a clinic-local
 * `YYYY-MM-DD` (default: the clinic's today). Read on open and on a window change only: the save re-checks anyway,
 * and a grid refreshed by every minute-job broadcast would move under the user's finger.
 */
export function useFreeSlots(appointmentId: string | null, from?: string): FreeSlotsState {
  const [days, setDays] = useState<FreeDayDto[] | null>(null)
  const [loading, setLoading] = useState(Boolean(appointmentId))
  const [failed, setFailed] = useState(false)
  const sequence = useRef(0)

  const reload = useCallback(() => {
    const ticket = ++sequence.current
    if (!appointmentId) {
      setDays(null)
      setLoading(false)
      setFailed(false)
      return
    }
    setLoading(true)
    setFailed(false)
    appointmentsApi
      .freeSlots(appointmentId, from ? { from } : undefined)
      .then((response) => {
        if (ticket === sequence.current) setDays(response.days)
      })
      .catch(() => {
        if (ticket !== sequence.current) return
        setDays(null)
        setFailed(true)
      })
      .finally(() => {
        if (ticket === sequence.current) setLoading(false)
      })
  }, [appointmentId, from])

  useEffect(() => {
    reload()
  }, [reload])

  return { days, loading, failed, reload }
}
