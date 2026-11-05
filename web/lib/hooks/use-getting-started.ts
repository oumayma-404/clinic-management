import { useCallback, useEffect, useState } from "react"

import { dashboardApi } from "@/lib/api/dashboard"
import type { GettingStartedDto } from "@/lib/api/types"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"

/**
 * « Premiers pas » for the signed-in administrator (guided-first-days B).
 *
 * <p>`enabled` is false for anyone else — the endpoint is `AdminOnly`, and asking would only produce a 403.</p>
 *
 * <p>Re-read on the broadcasts of what its steps count (a patient, a visit, a fiche — all under `patients` or
 * `appointments` —, a colleague's account, the letterhead under `clinics`), and when the window comes back into view:
 * the phone sign-in that ticks « Votre téléphone » is an Auth write, and Auth broadcasts nothing.</p>
 *
 * <p>⚠️ A failed read is `failed`, and the card is then not shown at all — never a list of unticked steps, which would
 * tell a cabinet that has done everything that it has done nothing.</p>
 */
export function useGettingStarted(enabled: boolean) {
  const [data, setData] = useState<GettingStartedDto | null>(null)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    try {
      setData(await dashboardApi.getGettingStarted())
      setFailed(false)
    } catch {
      setFailed(true)
    }
  }, [])

  useEffect(() => {
    if (enabled) void load()
  }, [enabled, load])

  useClinicRealtime(
    [RealtimeResource.Patients, RealtimeResource.Appointments, RealtimeResource.Users, RealtimeResource.Clinics],
    () => {
      if (enabled) void load()
    },
  )

  useEffect(() => {
    if (!enabled) return
    const onVisible = () => {
      if (document.visibilityState === "visible") void load()
    }
    document.addEventListener("visibilitychange", onVisible)
    return () => document.removeEventListener("visibilitychange", onVisible)
  }, [enabled, load])

  return { data, failed, reload: load }
}
