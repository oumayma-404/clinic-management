"use client"

import { useCallback, useEffect, useState } from "react"
import { useParams, useRouter } from "next/navigation"
import { ClinicGuard } from "@/components/clinic-guard"
import { AppShell } from "@/components/app-shell"
import { Button } from "@/components/ui/button"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { PlanWorkspace } from "@/components/treatment-plans/plan-workspace"

export default function TreatmentPlanWorkspacePage() {
  const params = useParams()
  const router = useRouter()
  const planId = typeof params?.id === "string" ? params.id : Array.isArray(params?.id) ? params.id[0] : ""

  const [plan, setPlan] = useState<TreatmentPlanDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    if (!planId) {
      setError("Le plan recherché n'existe pas.")
      setLoading(false)
      return
    }
    try {
      setError(null)
      const data = await treatmentPlansApi.get(planId)
      setPlan(data)
    } catch (err) {
      // A garbage id, a deleted plan and another clinic's plan all arrive here identically — the API answers
      // 404 for a cross-clinic id rather than 403, so this page can never confirm a plan exists elsewhere.
      setError(err instanceof ApiError ? err.message : "Le plan recherché n'existe pas.")
      setPlan(null)
    } finally {
      setLoading(false)
    }
  }, [planId])

  useEffect(() => {
    load()
  }, [load])

  // Three keys, as on every plan surface: the acts' états come from Appointment rows and « Facturé » from
  // Invoice rows, and RealtimeBroadcastBehavior keys off the *command's* namespace — so a peer cancelling
  // the séance broadcasts "appointments", never "treatmentplans".
  useClinicRealtime(
    [RealtimeResource.TreatmentPlans, RealtimeResource.Appointments, RealtimeResource.Invoices],
    load,
  )

  // Loading / not-found render OUTSIDE ClinicGuard, matching patients/[id]: the guard would otherwise show
  // its own spinner over a page that has already failed, and the user would never see why.
  if (loading) {
    return (
      <AppShell width="none" gutter={false} mainClassName="flex items-center justify-center">
        <p className="text-muted-foreground">Chargement du plan de traitement…</p>
      </AppShell>
    )
  }

  if (error || !plan) {
    return (
      <AppShell width="none" gutter={false} mainClassName="flex items-center justify-center">
        <div className="text-center">
          <h2 className="text-2xl font-semibold text-foreground">Plan introuvable</h2>
          <p className="mt-2 text-muted-foreground">{error || "Le plan recherché n'existe pas."}</p>
          <Button onClick={() => router.push("/treatment-plans")} className="mt-4">
            Retour aux plans
          </Button>
        </div>
      </AppShell>
    )
  }

  return (
    <ClinicGuard>
      <AppShell width="none">
        <PlanWorkspace plan={plan} onChanged={load} />
      </AppShell>
    </ClinicGuard>
  )
}
