"use client"

import { useCallback, useEffect, useRef, useState } from "react"
import { appointmentsApi } from "@/lib/api/appointments"
import type { SeanceToCollectDto } from "@/lib/api/types"
import { useMoneyVisibility } from "@/lib/money-visibility/money-visibility-context"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/**
 * Today's « À encaisser » rows, live (`seance-au-fauteuil`). Read ONCE by the agenda page and handed to both the
 * « À encaisser · N » pill and the agenda blocks, so the two cannot disagree about which séance is waiting.
 *
 * <p>`null` while « Mode discret » hides money (the read is refused then) and after a failed read: both consumers are
 * shortcuts, and « 0 » on a failed read would be a claim they cannot make.</p>
 *
 * <p>Re-read on the broadcasts a fiche save and a payment emit — the fiche's `appointments` arrives before its note
 * exists, so `invoices` and `treatmentplans` are listened to as well — debounced, since one save emits several.</p>
 */
export function useSeancesToCollect(): { rows: SeanceToCollectDto[] | null; reload: () => void } {
  const { moneyShown } = useMoneyVisibility()
  const [rows, setRows] = useState<SeanceToCollectDto[] | null>(null)
  const ticket = useRef(0)

  const load = useCallback(async () => {
    const mine = ++ticket.current
    try {
      const next = await appointmentsApi.toCollect()
      if (mine === ticket.current) setRows(next)
    } catch {
      if (mine === ticket.current) setRows(null)
    }
  }, [])

  useEffect(() => {
    if (moneyShown) void load()
    else {
      ticket.current += 1
      setRows(null)
    }
  }, [moneyShown, load])

  const timer = useRef<number | null>(null)
  useClinicRealtime(
    [RealtimeResource.Appointments, RealtimeResource.Patients, RealtimeResource.Invoices, RealtimeResource.TreatmentPlans],
    () => {
      if (!moneyShown) return
      if (timer.current !== null) window.clearTimeout(timer.current)
      timer.current = window.setTimeout(() => void load(), 400)
    },
  )
  useEffect(
    () => () => {
      if (timer.current !== null) window.clearTimeout(timer.current)
    },
    [],
  )

  const reload = useCallback(() => {
    if (moneyShown) void load()
  }, [moneyShown, load])

  return { rows: moneyShown ? rows : null, reload }
}
