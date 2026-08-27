"use client"

import Link from "next/link"
import { useState } from "react"
import { useRouter } from "next/navigation"

import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { ArrowRight, Loader2 } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { showErrorToast } from "@/lib/errors"
import type { TreatmentPlanDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { planStatusLabel, planStatusBadgeClass, planNextActionLabel } from "./treatment-plan-labels"
import { leadPlan, planHeadline, planNextAction, planStatusCounts } from "./plan-next-action"
import { PlanActPips } from "./plan-act-pips"

interface PatientPlansStripProps {
  plans: TreatmentPlanDto[]
  /** Show the patient's other plans — the plans tab, which lists them all. */
  onOpen: () => void
  /** Called after a mutation so the parent can refresh dependent views. */
  onChanged?: () => void
}

/**
 * The patient page's treatment band: which plan is running, the one thing to do next, the money still owed, and
 * that the patient's other plans exist — including the finished ones.
 *
 * <p>Replaced `patient-plan-card.tsx`, a ~250 px `Card` that spent that height on four facts about one plan. It is
 * a **band in the page flow** (`border-y`, no card) rather than an object floating on it, at roughly 76 px. Same
 * reasoning as the dashboard's `KpiGrid`: when everything on a page is a bordered rectangle, the borders stop
 * carrying meaning.</p>
 *
 * <p>Four things the card got wrong, all fixed here:</p>
 * <ol>
 *   <li>A 0 %-progress plan drew a full-width grey slab — `PlanProgressBar` only hides itself at *zero acts*, not
 *       at zero done. Pips keep meaning at zero (see {@link PlanActPips}).</li>
 *   <li>« 0/2 actes réalisé » — the participle was pluralised on `itemsDone`.</li>
 *   <li>« Reste » was never shown, though it is the money question staff actually have.</li>
 *   <li>`Completed`/`Cancelled` plans were filtered out of « +N autres », making finished treatment
 *       unrepresentable. They are chips now, and the band still renders when they are all a patient has.</li>
 * </ol>
 *
 * <p>Renders nothing only when the patient has no plans whatsoever.</p>
 */
export function PatientPlansStrip({ plans, onOpen, onChanged }: PatientPlansStripProps) {
  const [accepting, setAccepting] = useState(false)
  const router = useRouter()

  if (plans.length === 0) return null

  const plan = leadPlan(plans)

  const handleAccept = async (planId: string) => {
    setAccepting(true)
    try {
      await treatmentPlansApi.accept(planId)
      toast.success("Devis accepté")
      onChanged?.()
    } catch (err) {
      // `showErrorToast`, not a hand-rolled `toast.error`: the 8-second error duration and the network-only
      // « Réessayer » live there, and this toast is raised on the patient page where a 4-second refusal can be
      // pushed off screen by the next one before it is read.
      showErrorToast(err, "Échec de l'acceptation.")
    } finally {
      setAccepting(false)
    }
  }

  /*
   * No active or draft plan, but the patient has history. The card rendered NOTHING here, which quietly asserted
   * "this patient has never had a treatment plan" about someone who has completed three. The band stays, carrying
   * the chips and a way into the list.
   */
  if (!plan) {
    return (
      <section aria-label="Plans de traitement" className="flex flex-wrap items-center gap-x-3 gap-y-2 border-y py-3">
        <span className="text-sm font-medium text-muted-foreground">Aucun plan en cours</span>
        <StatusChips counts={planStatusCounts(plans)} onOpen={onOpen} className="ml-auto" />
        <Button size="sm" variant="outline" onClick={onOpen}>
          Voir les plans
        </Button>
      </section>
    )
  }

  const isDraft = plan.status === "Draft"
  const next = planNextAction(plan)
  const otherCounts = planStatusCounts(plans, plan.id)
  const openWorkspace = () => router.push(`/treatment-plans/${plan.id}`)

  return (
    <section aria-label="Plans de traitement" className="flex flex-col gap-2 border-y py-3">
      {/* Line 1 — identity · the one thing to do · the way in. */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
        <span className="text-sm font-semibold">
          {isDraft ? "Devis — brouillon" : `Plan ${plan.number ?? ""}`.trim()}
        </span>
        <Badge variant="secondary" className={planStatusBadgeClass(plan.status)}>
          {planStatusLabel(plan.status)}
        </Badge>
        {/* « · révision N » only once amended, so a patient holding an earlier printout can tell which they signed. */}
        {plan.revisionNumber > 0 && (
          <span className="text-xs text-muted-foreground">révision {plan.revisionNumber}</span>
        )}
        {plan.linkedInvoiceNumber && (
          <Link href={`/factures?search=${encodeURIComponent(plan.linkedInvoiceNumber)}`}>
            <Badge variant="outline" className="hover:bg-accent">
              Facturé — {plan.linkedInvoiceNumber}
            </Badge>
          </Link>
        )}

        {/*
          The headline: the largest text on the band, because it is the only line that says what to DO — and it
          re-derives as visit times pass. Everything on line 2 is context for it.
        */}
        <span className="ml-auto flex items-center gap-2">
          {/* `bg-warning-ink`, not `bg-amber-500`. This dot marks the « à enregistrer » état, which the badge
              renders with `--warning-ink` and the pip now does too — three ambers for one état meant the band,
              the row badge and the pip were each a slightly different colour for the same fact, and only one of
              them followed the palette. */}
          {next.kind === "record" && (
            <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-warning-ink" aria-hidden="true" />
          )}
          <span className="text-base font-semibold">{planHeadline(plan)}</span>
        </span>

        {next.kind === "accept" ? (
          <Button size="sm" onClick={() => handleAccept(plan.id)} disabled={accepting} className="gap-2">
            {accepting && <Loader2 className="h-4 w-4 animate-spin" />}
            {planNextActionLabel("accept")}
          </Button>
        ) : (
          <Button size="sm" onClick={openWorkspace} className="gap-2">
            {planNextActionLabel(next.kind)}
            <ArrowRight className="h-4 w-4" />
          </Button>
        )}
      </div>

      {/* Line 2 — the facts, hairline-separated so they read as distinct figures without four boxes. */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
        {isDraft ? (
          <>
            {/*
              A draft devis is not debt — it contributes 0 to « Solde patient » by design — so it shows its planned
              total and never a « Reste », and no progress, since nothing can be done before acceptance.
            */}
            <span className="tabular-nums text-muted-foreground">
              {plan.itemsTotal} acte{plan.itemsTotal > 1 ? "s" : ""}
            </span>
            <Separator />
            <Fact label="Total" value={formatDT(plan.totalPlanned)} />
          </>
        ) : (
          <>
            <PlanActPips items={plan.items} done={plan.itemsDone} total={plan.itemsTotal} />
            <Separator />
            {/*
              Red once the clinical work is finished but the money is not: at that point the outstanding balance is
              the only thing left holding the plan open, so it stops being a neutral figure.
            */}
            <Fact
              label="Reste"
              value={formatDT(plan.outstanding)}
              tone={plan.outstanding > 0 && plan.itemsDone === plan.itemsTotal ? "alert" : "default"}
            />
            <Separator />
            {plan.nextAppointmentAt ? (
              <Fact label="Prochaine séance" value={formatDateFr(plan.nextAppointmentAt)} />
            ) : (
              <span className="text-muted-foreground">Aucune séance planifiée</span>
            )}
          </>
        )}

        {otherCounts.length > 0 && (
          <>
            <Separator />
            <StatusChips counts={otherCounts} onOpen={onOpen} />
          </>
        )}

        {/* Omitted for a lone plan rather than rendered as a dead « 0 autre ».

            A real `Button variant="link"`, not a bare styled `<button>`: the hand-rolled one was ~16 px tall
            with no padding, well under the 44 px touch floor, so on a phone it sat between two other tap
            targets and was effectively unhittable. `size="sm"` paints the same small text while the shared
            `touch-target` utility raises the hit area on a coarse pointer without changing what is drawn. */}
        {plans.length > 1 && (
          <Button variant="link" size="sm" onClick={onOpen} className="ml-auto h-auto px-0 text-xs">
            Tous les plans
          </Button>
        )}
      </div>
    </section>
  )
}

/** A hairline between two figures — cheaper than a box each, and it survives wrapping. */
function Separator() {
  return <span aria-hidden="true" className="hidden min-h-4 w-px self-stretch bg-border sm:block" />
}

/** A labelled figure: quiet label, emphasised value, tabular digits so columns of them line up. */
function Fact({ label, value, tone = "default" }: { label: string; value: string; tone?: "default" | "alert" }) {
  return (
    <span className="text-muted-foreground">
      {label}{" "}
      <b className={tone === "alert" ? "font-semibold tabular-nums text-destructive" : "font-semibold tabular-nums text-foreground"}>
        {value}
      </b>
    </span>
  )
}

/**
 * The patient's other plans, one chip per statut.
 *
 * Each chip is a button into the plans tab rather than inert text: the count is only useful if you can act on it.
 * The accessible name spells the statut out (« 2 plans terminés ») — « 2 terminés » beside a green pill reads fine
 * visually but is meaningless read aloud in isolation.
 */
function StatusChips({
  counts,
  onOpen,
  className,
}: {
  counts: { status: string; count: number }[]
  onOpen: () => void
  className?: string
}) {
  return (
    /*
     * `gap-2` and a padded button, because these chips are adjacent tap targets.
     *
     * Each was a bare `<button>` wrapping a `<Badge>` — a ~20 px target — with `gap-1` (4 px) between them in a
     * row that wraps. On a phone, tapping « 2 terminés » regularly landed on « 1 annulé » beside it. They all
     * call the same `onOpen`, so a mis-tap was harmless *today*; that is exactly the kind of latent defect that
     * becomes a real one the moment a chip gets its own destination.
     *
     * `py-1` on the button rather than `touch-target`: these sit side by side, and a 44 px overlay on a 20 px
     * control overhangs its neighbour by 12 px each side and the later sibling wins the hit test — the failure
     * this codebase now documents in several places. Real padding plus a wider gap keeps paint and hit area
     * honest.
     */
    <span className={`flex flex-wrap items-center gap-2 ${className ?? ""}`}>
      {counts.map(({ status, count }) => (
        <button
          key={status}
          type="button"
          onClick={onOpen}
          className="-my-1 rounded-full py-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          aria-label={`${count} plan${count > 1 ? "s" : ""} ${planStatusLabel(status).toLowerCase()}`}
        >
          <Badge variant="secondary" className={`${planStatusBadgeClass(status)} hover:brightness-95`}>
            {count} {planStatusLabel(status).toLowerCase()}
          </Badge>
        </button>
      ))}
    </span>
  )
}
