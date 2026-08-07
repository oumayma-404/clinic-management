"use client"

import { useCallback, useEffect, useState } from "react"
import { useParams, useRouter } from "next/navigation"
import { ClinicGuard } from "@/components/clinic-guard"
import { AppShell } from "@/components/app-shell"
import { Button } from "@/components/ui/button"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import { getErrorMessage, isNetworkError } from "@/lib/errors"
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
  /**
   * The thrown value, not a pre-rendered string.
   *
   * <p>It used to be `string | null`, which is what made the failure screen lie: by the time the render ran,
   * the only thing left of « the server never answered » was its message, so the branch had nothing to test and
   * showed the same heading for every cause.</p>
   */
  const [loadError, setLoadError] = useState<unknown>(null)

  const load = useCallback(async () => {
    if (!planId) {
      // No id in the URL at all — genuinely nothing to look up. Modelled as a 404 rather than a fault so the
      // screen below shows « Plan introuvable » without offering a retry that would re-run the same nothing.
      setLoadError(new ApiError(404, "Le plan recherché n'existe pas."))
      setLoading(false)
      return
    }
    try {
      setLoadError(null)
      const data = await treatmentPlansApi.get(planId)
      setPlan(data)
    } catch (err) {
      // A garbage id, a deleted plan and another clinic's plan all arrive here identically — the API answers
      // 404 for a cross-clinic id rather than 403, so this page can never confirm a plan exists elsewhere.
      setLoadError(err)
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

  if (loadError || !plan) {
    /*
     * Three outcomes, three screens — the same distinction `patients/[id]` makes and for the same reason.
     *
     * ⚠️ The old branch was `if (error || !plan)` under a hard-coded « Plan introuvable » heading, with the
     * caught message printed underneath. So a dropped Wi-Fi link produced a heading asserting the devis does
     * not exist over a body explaining that the server is unreachable — the two halves of the screen
     * contradicting each other — and the only button was « Retour aux plans », which throws away the page the
     * user was on for a failure that a second attempt would very likely fix.
     *
     * « Introuvable » is a **statement about the clinic's records** and must be reserved for the one status
     * that actually says so. A 404 is worth going back from; everything else is worth retrying.
     */
    const notFound = loadError instanceof ApiError && loadError.status === 404
    const offline = isNetworkError(loadError)

    const heading = notFound
      ? "Plan introuvable"
      : offline
        ? "Connexion au serveur impossible"
        : "Le devis n'a pas pu être chargé"

    const body = notFound
      ? getErrorMessage(loadError, "Le plan recherché n'existe pas.")
      : offline
        ? "Le devis n'a pas pu être chargé. Vérifiez votre connexion, puis réessayez."
        : getErrorMessage(loadError, "Une erreur est survenue lors du chargement du devis.")

    return (
      <AppShell width="none" gutter={false} mainClassName="flex items-center justify-center">
        <div className="max-w-md text-center">
          <h2 className="text-2xl font-semibold text-foreground">{heading}</h2>
          <p className="mt-2 text-muted-foreground">{body}</p>
          {/* On a real 404 « Retour aux plans » is the only move and stays primary. On a failure the primary
              action is the one that can actually succeed — and it re-runs `load`, so the user never loses the
              URL they arrived on. */}
          <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
            {!notFound && (
              <Button
                onClick={() => {
                  setLoading(true)
                  load()
                }}
              >
                Réessayer
              </Button>
            )}
            <Button
              variant={notFound ? "default" : "outline"}
              onClick={() => router.push("/treatment-plans")}
            >
              Retour aux plans
            </Button>
          </div>
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
