"use client"

import { useCallback, useEffect, useState } from "react"
import { useParams, useRouter } from "next/navigation"
import { ClinicGuard } from "@/components/clinic-guard"
import { DashboardSidebar } from "@/components/dashboard-sidebar"
import { DashboardHeader } from "@/components/dashboard-header"
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
      <Shell>
        <main className="flex flex-1 items-center justify-center">
          <p className="text-muted-foreground">Chargement du plan de traitement…</p>
        </main>
      </Shell>
    )
  }

  if (error || !plan) {
    return (
      <Shell>
        <main className="flex flex-1 items-center justify-center">
          <div className="text-center">
            <h2 className="text-2xl font-semibold text-foreground">Plan introuvable</h2>
            <p className="mt-2 text-muted-foreground">{error || "Le plan recherché n'existe pas."}</p>
            <Button onClick={() => router.push("/treatment-plans")} className="mt-4">
              Retour aux plans
            </Button>
          </div>
        </main>
      </Shell>
    )
  }

  return (
    <ClinicGuard>
      <div className="flex h-screen bg-background">
        <DashboardSidebar />
        <div className="flex flex-1 flex-col overflow-hidden">
          <DashboardHeader />
          <main className="flex-1 overflow-auto p-4 md:p-6">
            <PlanWorkspace plan={plan} onChanged={load} />
          </main>
        </div>
      </div>
    </ClinicGuard>
  )
}

/** The standard page chrome, minus ClinicGuard — used by the loading and not-found states. */
function Shell({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex h-screen bg-background">
      <DashboardSidebar />
      <div className="flex flex-1 flex-col overflow-hidden">
        <DashboardHeader />
        {children}
      </div>
    </div>
  )
}
