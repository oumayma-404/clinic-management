"use client"

import { useState } from "react"
import { useRouter } from "next/navigation"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { ClipboardCheck, ArrowRight, Loader2 } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { ApiError } from "@/lib/api/client"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { planStatusLabel, planStatusBadgeClass, planNextActionLabel } from "./treatment-plan-labels"
import { leadPlan, planNextAction } from "./plan-next-action"
import { PlanProgressBar } from "./plan-progress-bar"

interface PatientPlanCardProps {
  plans: TreatmentPlanDto[]
  /** Show the patient's other plans — the plans tab, which lists them all. */
  onOpen: () => void
  /** Called after a mutation so the parent can refresh dependent views. */
  onChanged?: () => void
}

/**
 * The patient page's lead-in to treatment: what plan is running, how far it has got, when the next séance is,
 * and the single next step. Sits with the other summary cards, above the tabs — a plan buried in the 8th tab
 * was the whole reason the devis felt disconnected from the patient.
 *
 * Renders nothing when the patient has no plan at all (not an empty box).
 */
export function PatientPlanCard({ plans, onOpen, onChanged }: PatientPlanCardProps) {
  const [accepting, setAccepting] = useState(false)
  const router = useRouter()

  const plan = leadPlan(plans)
  if (!plan) return null

  // The lead plan now has a real home, so the card links straight into it; "+N autres" still goes to the
  // tab, which is what lists the rest.
  const openWorkspace = () => router.push(`/treatment-plans/${plan.id}`)

  const isDraft = plan.status === "Draft"
  const next = planNextAction(plan)
  const otherCount = plans.filter(
    (p) => p.id !== plan.id && p.status !== "Cancelled" && p.status !== "Completed",
  ).length

  const handleAccept = async () => {
    setAccepting(true)
    try {
      await treatmentPlansApi.accept(plan.id)
      toast.success("Devis accepté")
      onChanged?.()
    } catch (err) {
      toast.error(err instanceof ApiError ? err.message : "Échec de l'acceptation.")
    } finally {
      setAccepting(false)
    }
  }

  // A Draft devis is not debt (it contributes 0 to « Solde patient » by design), so the draft variant shows
  // the planned total but never a « Reste » — and no progress bar, since nothing can be done yet.
  return (
    <Card>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-2 text-base">
          <ClipboardCheck className="h-4 w-4" />
          {isDraft ? "Devis — Brouillon" : `Plan de traitement ${plan.number ?? ""}`.trim()}
          <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
            {planStatusLabel(plan.status)}
          </Badge>
          {plan.linkedInvoiceNumber && (
            <Badge variant="outline">Facturé — {plan.linkedInvoiceNumber}</Badge>
          )}
        </CardTitle>
      </CardHeader>

      <CardContent className="space-y-3">
        {isDraft ? (
          <p className="text-sm text-muted-foreground">
            {plan.itemsTotal} acte{plan.itemsTotal > 1 ? "s" : ""} · {formatDT(plan.totalPlanned)} — à accepter
            pour démarrer le suivi.
          </p>
        ) : (
          <>
            <PlanProgressBar done={plan.itemsDone} total={plan.itemsTotal} />
            <p className="text-sm text-muted-foreground">
              {plan.itemsDone}/{plan.itemsTotal} acte{plan.itemsTotal > 1 ? "s" : ""} réalisé
              {plan.itemsDone > 1 ? "s" : ""} · {formatDT(plan.amountPaid)} / {formatDT(plan.totalPlanned)}
            </p>
            <p className="text-sm text-foreground">
              {plan.nextAppointmentAt
                ? `Prochaine séance : ${formatDateFr(plan.nextAppointmentAt)}`
                : "Aucune séance planifiée"}
            </p>
          </>
        )}

        <div className="flex flex-wrap items-center gap-2 pt-1">
          {next.kind === "accept" ? (
            <Button size="sm" onClick={handleAccept} disabled={accepting} className="gap-2">
              {accepting && <Loader2 className="h-4 w-4 animate-spin" />}
              {planNextActionLabel("accept")}
            </Button>
          ) : (
            <Button size="sm" onClick={openWorkspace} className="gap-2">
              {planNextActionLabel(next.kind)}
              <ArrowRight className="h-4 w-4" />
            </Button>
          )}
          <Button size="sm" variant="outline" onClick={openWorkspace}>
            {isDraft ? "Ouvrir" : "Voir le plan"}
          </Button>
          {otherCount > 0 && (
            <button type="button" onClick={onOpen} className="text-xs text-muted-foreground underline">
              +{otherCount} autre{otherCount > 1 ? "s" : ""}
            </button>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
