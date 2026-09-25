"use client"

import Link from "next/link"
import { useState, type ReactNode } from "react"
import { useRouter } from "next/navigation"

import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent,
  AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"
import { CalendarPlus, ChevronRight, FilePlus2, Loader2, MoreHorizontal } from "lucide-react"
import { toast } from "sonner"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import { showErrorToast } from "@/lib/errors"
import type { TreatmentPlanDto, TreatmentPlanItemDto } from "@/lib/api/types"
import { formatDT, formatDateFr } from "@/lib/format"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"
import {
  planStatusLabel,
  planStatusBadgeClass,
  planNextActionLabel,
  planHasRecordedWork,
  planDevisLabel,
  treatmentName,
  teethSuffix,
  planItemStateLabel,
  itemWorkflowBadgeClass,
} from "./treatment-plan-labels"
import {
  activeItems,
  displayedOutstanding,
  firstUnbookedStep,
  nextStepOf,
  isPlanLive,
  planItemState,
  planItemToPreset,
  planNextAction,
  planStatusCounts,
  type PlanNextAction,
} from "./plan-next-action"
import { PlanActPips } from "./plan-act-pips"
import { SeancePips, seanceSummary } from "./seance-strip"
import { Consequences } from "./plan-consequences"

interface PatientPlansStripProps {
  plans: TreatmentPlanDto[]
  /** Show the patient's other treatments — the plans tab, which lists them all. */
  onOpen: () => void
  /** Called after a mutation so the parent can refresh dependent views. */
  onChanged?: () => void
  /**
   * Opens the fiche de soins of a séance whose slot has passed — the page's own `openVisitRecord`. Absent, the
   * button falls back to the `?addRecord=1&appointmentId=` deep link the treatment page uses.
   */
  onRecordVisit?: (appointmentId: string) => void
}

/**
 * The patient page's treatments: **one card per live treatment** — its name, its status, where its séances
 * stand, what is left to pay, and ONE button naming the next séance, which books it right here.
 *
 * <p>⚠️ <b>« Planifier : Empreinte » opens the booking dialog directly</b>, with the same preset the treatment
 * page's « Planifier la séance » builds (`planItemToPreset`). It used to navigate to the treatment page first,
 * where the act's row then had to be found — two screens for the most frequent action on the file.</p>
 *
 * <p>⚠️ <b>A Draft (a treatment followed without a devis) is asked for its next séance, never « À accepter ».</b>
 * Numbering the devis is not the next step of the work, and a headline nagging for it on every followed
 * treatment was read as something being wrong. « Créer le devis » stays one press away in the card's « ⋯ »,
 * behind the same confirmation it always had.</p>
 *
 * <p>Closed treatments (terminé, arrêté, annulé, non réclamé) stay as counts under the cards — finished treatment
 * is information, just not an action. Renders nothing only when the patient has no treatment whatsoever.</p>
 */
export function PatientPlansStrip({ plans, onOpen, onChanged, onRecordVisit }: PatientPlansStripProps) {
  const [accepting, setAccepting] = useState(false)
  /**
   * The treatment whose devis is waiting for a yes — M20. Creating the devis spends a per-clinic-per-year number
   * and turns the total into a live créance, and its only exits are an annulation with a motif; so it is never
   * fired straight off the click.
   */
  const [acceptTarget, setAcceptTarget] = useState<TreatmentPlanDto | null>(null)
  /** The booking dialog, opened from a card with that treatment's next séance preset. */
  const [booking, setBooking] = useState<{
    plan: TreatmentPlanDto
    presets: PresetPlanAct[]
    defaultDay?: Date
  } | null>(null)
  const router = useRouter()

  if (plans.length === 0) return null

  const handleAccept = async (planId: string) => {
    setAccepting(true)
    try {
      await treatmentPlansApi.accept(planId)
      toast.success("Devis créé")
      setAcceptTarget(null)
      onChanged?.()
    } catch (err) {
      // `showErrorToast`: the 8-second duration and the network « Réessayer » live there.
      showErrorToast(err, "Échec de la création du devis.")
    } finally {
      setAccepting(false)
    }
  }

  // Same order `leadPlan` reads them in: most recently accepted (else created) first.
  const livePlans = plans
    .filter((p) => isPlanLive(p.status))
    .sort(
      (a, b) =>
        new Date(b.acceptedDate ?? b.createdAt).getTime() - new Date(a.acceptedDate ?? a.createdAt).getTime(),
    )
  const otherCounts = planStatusCounts(plans).filter((c) => !isPlanLive(c.status))

  const openWorkspace = (plan: TreatmentPlanDto) => router.push(`/treatment-plans/${plan.id}`)

  const book = (plan: TreatmentPlanDto, preset: PresetPlanAct, item: TreatmentPlanItemDto) => {
    // The protocol's own interval as the day the sheet opens on — never a past date. `defaultDay`, never
    // `defaultDate`: a due date is midnight, and `defaultDate` would open the form on 00:00 (N20). A LATER séance
    // (the next one already booked) opens on its own earliest day.
    const step = item.steps?.find((s) => s.id === preset.preselectedStepId)
    const dueIso = step && item.nextStepId && step.id !== item.nextStepId ? step.earliestOn : item.nextStepDueFrom
    const due = dueIso ? new Date(dueIso) : null
    setBooking({ plan, presets: [preset], defaultDay: due && due.getTime() > Date.now() ? due : undefined })
  }

  const recordVisit = (plan: TreatmentPlanDto, appointmentId: string) => {
    if (onRecordVisit) onRecordVisit(appointmentId)
    else router.push(`/patients/${plan.patientId}?addRecord=1&appointmentId=${appointmentId}`)
  }

  return (
    <section aria-label="Traitements" className="flex flex-col gap-2">
      {livePlans.length === 0 ? (
        /* No live treatment, but history: the band stays, carrying the counts and the way into the list. */
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-y py-3">
          <span className="text-sm font-medium text-muted-foreground">Aucun traitement en cours</span>
          <StatusChips counts={planStatusCounts(plans)} onOpen={onOpen} className="ms-auto" />
          <Button size="sm" variant="outline" onClick={onOpen}>
            Voir les traitements
          </Button>
        </div>
      ) : (
        <>
          <ul className={cn("grid gap-2", livePlans.length > 1 && "xl:grid-cols-2")}>
            {livePlans.map((plan) => (
              <TreatmentCard
                key={plan.id}
                plan={plan}
                busy={accepting && acceptTarget?.id === plan.id}
                onOpenWorkspace={() => openWorkspace(plan)}
                onBook={(preset, item) => book(plan, preset, item)}
                onRecord={(appointmentId) => recordVisit(plan, appointmentId)}
                onCreateDevis={() => setAcceptTarget(plan)}
                onOpenAll={onOpen}
              />
            ))}
          </ul>

          {(otherCounts.length > 0 || plans.length > 1) && (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2 text-sm">
              {otherCounts.length > 0 && <StatusChips counts={otherCounts} onOpen={onOpen} />}
              {plans.length > 1 && (
                // A real `Button variant="link"`: its `touch-target` raises the hit area on a coarse pointer.
                <Button variant="link" size="sm" onClick={onOpen} className="ms-auto h-auto px-0 text-xs">
                  Tous les traitements
                </Button>
              )}
            </div>
          )}

          <PlanActsFold plans={livePlans} />
        </>
      )}

      {/* The treatment page's own booking dialog, with the same preset — see `book`. */}
      {booking && (
        <CreateAppointmentDialog
          open
          onOpenChange={(o) => !o && setBooking(null)}
          presetPatientId={booking.plan.patientId}
          presetPatientName={booking.plan.patientName ?? undefined}
          presetPlanId={booking.plan.id}
          presetPlanActs={booking.presets}
          defaultDay={booking.defaultDay}
          onSuccess={() => {
            setBooking(null)
            onChanged?.()
          }}
        />
      )}

      {/* M20 — modal, not dismissible by an outside click, focus on the cancel. It names the money. */}
      <AlertDialog
        open={!!acceptTarget}
        onOpenChange={(open) => { if (!open && !accepting) setAcceptTarget(null) }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {acceptTarget ? `Créer le devis de ${treatmentName(acceptTarget)} ?` : "Créer le devis ?"}
            </AlertDialogTitle>
            <AlertDialogDescription asChild>
              {/* The same three bullets as the treatment page's « Créer le devis », in plain words. */}
              {acceptTarget ? (
                <Consequences
                  items={[
                    <>Un <b className="text-foreground">numéro de devis</b> est attribué</>,
                    <>
                      <b className="text-foreground">{formatDT(acceptTarget.totalPlanned)}</b> sont à payer par le
                      patient
                    </>,
                    <><b className="text-foreground">Définitif</b> : une erreur s&apos;annule avec un motif</>,
                  ]}
                />
              ) : (
                <span />
              )}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={accepting}>Retour</AlertDialogCancel>
            <AlertDialogAction
              disabled={accepting}
              onClick={(event) => {
                event.preventDefault()
                if (acceptTarget) void handleAccept(acceptTarget.id)
              }}
            >
              {accepting && <Loader2 className="h-4 w-4 animate-spin" />}
              {planNextActionLabel("accept")}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  )
}

/**
 * What the card's one button does. {@link planNextAction} for a treatment with a devis; for a Draft, the same
 * work order (a séance to record, then one to book) — never « accept », which lives in the « ⋯ ».
 */
function cardNextAction(plan: TreatmentPlanDto, now: Date = new Date()): PlanNextAction {
  const live = activeItems(plan)
  let next: PlanNextAction = { kind: "open" }
  if (plan.status !== "Draft") next = planNextAction(plan, now)
  else {
    const toRecord = live.find((i) => planItemState(i, now) === "to-record")
    const toSchedule = live.find((i) => planItemState(i, now) === "to-schedule")
    if (toRecord) next = { kind: "record", itemId: toRecord.id }
    else if (toSchedule) next = { kind: "schedule", itemId: toSchedule.id }
  }
  if (next.kind !== "collect" && next.kind !== "open") return next
  // The next séance is booked but a later one is not: « Planifier : Scellement » — the treatment page's own rule.
  const later = isPlanLive(plan.status)
    ? live.find((i) => i.status !== "Done" && planItemState(i, now) === "scheduled" && firstUnbookedStep(i) !== null)
    : undefined
  return later ? { kind: "schedule", itemId: later.id } : next
}

/** The act a card speaks for: the one its next action names, else the first unfinished one. */
function leadItemOf(plan: TreatmentPlanDto, next: PlanNextAction): TreatmentPlanItemDto | null {
  const live = activeItems(plan)
  if ("itemId" in next) {
    const named = live.find((i) => i.id === next.itemId)
    if (named) return named
  }
  return live.find((i) => i.status !== "Done") ?? live[0] ?? null
}

function sortedSteps(item: TreatmentPlanItemDto) {
  return [...(item.steps ?? [])].sort((a, b) => a.sequenceNumber - b.sequenceNumber)
}

/** The appointment a « to-record » act's séance was booked on — the one its fiche belongs to. */
function recordAppointmentOf(item: TreatmentPlanItemDto): string | null {
  const step = nextStepOf(item)
  return (step ? step.scheduledAppointmentId : item.scheduledAppointmentId) ?? null
}

function TreatmentCard({
  plan,
  busy,
  onOpenWorkspace,
  onBook,
  onRecord,
  onCreateDevis,
  onOpenAll,
}: {
  plan: TreatmentPlanDto
  busy: boolean
  onOpenWorkspace: () => void
  onBook: (preset: PresetPlanAct, item: TreatmentPlanItemDto) => void
  onRecord: (appointmentId: string) => void
  onCreateDevis: () => void
  onOpenAll: () => void
}) {
  const name = treatmentName(plan)
  const hasWork = planHasRecordedWork(plan)
  const next = cardNextAction(plan)
  const live = activeItems(plan)
  const lead = leadItemOf(plan, next)
  const leadSteps = lead ? sortedSteps(lead) : []
  const owed = displayedOutstanding(plan)
  const isDraft = plan.status === "Draft"

  const facts: ReactNode[] = [
    ...progressFacts(plan, live, lead, leadSteps),
    owed && owed.amount > 0.0005 ? (
      <span key="owed" className="text-muted-foreground">
        Reste à payer{owed.isBilled && owed.invoiceNumber ? ` (note n° ${owed.invoiceNumber})` : ""}{" "}
        <b
          className={cn(
            "font-semibold tabular-nums",
            // Red only once the work is done and the money is not.
            plan.itemsDone === plan.itemsTotal ? "text-destructive" : "text-foreground",
          )}
        >
          {formatDT(owed.amount)}
        </b>
      </span>
    ) : null,
    // A Draft carries no debt, so it states its price rather than a « reste ».
    isDraft && plan.totalPlanned > 0 ? (
      <span key="price" className="text-muted-foreground">
        Prix du traitement{" "}
        <b className="font-semibold tabular-nums text-foreground">{formatDT(plan.totalPlanned)}</b>
      </span>
    ) : null,
  ].filter(Boolean)

  return (
    <li className="flex min-w-0 flex-col gap-2 rounded-md border bg-card p-3">
      {/* Identity · status · the devis paper, with the menu pinned top-right so it never wraps onto a line alone. */}
      <div className="flex items-start gap-2">
      <div className="flex min-w-0 flex-1 flex-wrap items-center gap-x-2 gap-y-1">
        <Link
          href={`/treatment-plans/${plan.id}`}
          className="inline-flex min-w-0 items-center font-semibold underline-offset-4 [overflow-wrap:anywhere] hover:underline coarse:min-h-11"
        >
          {name}
        </Link>
        <Badge variant="secondary" className={planStatusBadgeClass(plan.status, hasWork)}>
          {planStatusLabel(plan.status, hasWork)}
        </Badge>
        <span className="text-2xs text-muted-foreground">
          {planDevisLabel(plan)}
          {/* Only once amended, so a patient holding an earlier printout can tell which one they signed. */}
          {plan.revisionNumber > 0 && ` · révision ${plan.revisionNumber}`}
        </span>
        {plan.linkedInvoiceNumber && (
          <Link href={`/factures?search=${encodeURIComponent(plan.linkedInvoiceNumber)}`}>
            <Badge variant="outline" className="hover:bg-accent">
              Facturé — {plan.linkedInvoiceNumber}
            </Badge>
          </Link>
        )}
      </div>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost"
              size="icon-sm"
              className="shrink-0"
              disabled={busy}
              aria-label={`Actions — ${name}`}
            >
              {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <MoreHorizontal className="h-4 w-4" />}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem className="coarse:py-3" onSelect={onOpenWorkspace}>
              {planNextActionLabel("open")}
            </DropdownMenuItem>
            {isDraft && (
              <DropdownMenuItem className="coarse:py-3" onSelect={onCreateDevis}>
                {planNextActionLabel("accept")}
              </DropdownMenuItem>
            )}
            <DropdownMenuItem className="coarse:py-3" onSelect={onOpenAll}>
              Tous les traitements
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      {/* Where the séances stand, and the money — facts only, a middle dot between two. */}
      {facts.length > 0 && (
        <div className="flex flex-col items-start gap-y-1 text-sm sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-2">
          {/* One fact per line on a phone — a wrapped dotted row left a « · » alone at a line's start. */}
          {facts.map((fact, index) => (
            <span key={index} className="inline-flex min-w-0 items-center gap-x-2">
              {index > 0 && <span aria-hidden="true" className="hidden text-muted-foreground sm:inline">·</span>}
              {fact}
            </span>
          ))}
        </div>
      )}

      <CardAction
        plan={plan}
        name={name}
        next={next}
        lead={lead}
        onOpenWorkspace={onOpenWorkspace}
        onBook={onBook}
        onRecord={onRecord}
      />
    </li>
  )
}

/**
 * The séance facts: the lead act's dots + « Empreinte prévue le 28/09 »; for several step-less acts, one dot per
 * act + a worded count and the next séance; for one step-less act, its séance when there is one.
 */
function progressFacts(
  plan: TreatmentPlanDto,
  live: TreatmentPlanItemDto[],
  lead: TreatmentPlanItemDto | null,
  leadSteps: ReturnType<typeof sortedSteps>,
): ReactNode[] {
  if (lead && leadSteps.length > 0) {
    return [
      <span key="seances" className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1">
        {/* On a treatment of several acts, name the act the dots are about. */}
        {live.length > 1 && (
          <span className="text-muted-foreground">
            {lead.designationFr}
            {teethSuffix(lead.toothNumbers)}
          </span>
        )}
        <SeancePips steps={leadSteps} />
        <span className="font-medium">{seanceSummary(leadSteps)}</span>
      </span>,
    ]
  }

  if (live.length > 1) {
    return [
      <PlanActPips key="acts" items={live} done={plan.itemsDone} total={plan.itemsTotal} plan={plan} />,
      plan.nextAppointmentAt ? (
        <span key="next" className="text-muted-foreground">
          Prochaine séance le <b className="font-semibold text-foreground">{formatDateFr(plan.nextAppointmentAt)}</b>
        </span>
      ) : null,
    ].filter(Boolean)
  }

  if (!lead) return []
  const state = planItemState(lead)
  if (state === "scheduled" && lead.scheduledAt) {
    return [
      <span key="next" className="text-muted-foreground">
        Séance prévue le <b className="font-semibold text-foreground">{formatDateFr(lead.scheduledAt)}</b>
      </span>,
    ]
  }
  if (state === "to-record") {
    return [<span key="record" className="font-medium text-warning-ink">Séance à enregistrer</span>]
  }
  if (state === "done") return [<span key="done" className="font-medium text-success">Fait</span>]
  // « À planifier » is what the button below says.
  return []
}

/** The card's ONE button — the next séance to book, the fiche to record, the money to take, or the page. */
function CardAction({
  plan,
  name,
  next,
  lead,
  onOpenWorkspace,
  onBook,
  onRecord,
}: {
  plan: TreatmentPlanDto
  name: string
  next: PlanNextAction
  lead: TreatmentPlanItemDto | null
  onOpenWorkspace: () => void
  onBook: (preset: PresetPlanAct, item: TreatmentPlanItemDto) => void
  onRecord: (appointmentId: string) => void
}) {
  if (next.kind === "schedule" && lead) {
    const preset = planItemToPreset(plan, lead, (i) => i.procedureTypeId ?? undefined)
    // The séance the dialog will preselect — so the button names what gets booked.
    const stepLabel =
      preset.steps?.find((s) => s.id === preset.preselectedStepId)?.label ?? nextStepOf(lead)?.label ?? null
    return (
      <Button
        size="sm"
        // The label holds a free-text séance name: it wraps rather than overflowing the card (§ 10.1).
        className="h-auto min-h-8 max-w-full gap-1.5 self-start whitespace-normal py-1.5 text-start coarse:min-h-11"
        onClick={() => onBook(preset, lead)}
        // Starts with the visible label, so a voice command naming the button still reaches it.
        aria-label={stepLabel ? `Planifier : ${stepLabel} — ${name}` : `Planifier — ${name}`}
      >
        <CalendarPlus className="h-4 w-4 shrink-0" aria-hidden="true" />
        {stepLabel ? `Planifier : ${stepLabel}` : "Planifier"}
      </Button>
    )
  }

  if (next.kind === "record" && lead) {
    const appointmentId = recordAppointmentOf(lead)
    return (
      <Button
        size="sm"
        className="gap-1.5 self-start coarse:h-11"
        onClick={() => (appointmentId ? onRecord(appointmentId) : onOpenWorkspace())}
        aria-label={`${planNextActionLabel("record")} — ${name}`}
      >
        <FilePlus2 className="h-4 w-4" aria-hidden="true" />
        {planNextActionLabel("record")}
      </Button>
    )
  }

  if (next.kind === "collect") {
    // Collecting lives on the treatment page, where the échéancier is.
    return (
      <Button
        size="sm"
        className="gap-1.5 self-start coarse:h-11"
        onClick={onOpenWorkspace}
        aria-label={`${planNextActionLabel("collect")} — ${name}`}
      >
        {planNextActionLabel("collect")}
        <ChevronRight className="h-4 w-4" aria-hidden="true" />
      </Button>
    )
  }

  return (
    <Button
      size="sm"
      variant="outline"
      className="gap-1.5 self-start coarse:h-11"
      onClick={onOpenWorkspace}
      aria-label={`${planNextActionLabel("open")} — ${name}`}
    >
      {planNextActionLabel("open")}
      <ChevronRight className="h-4 w-4" aria-hidden="true" />
    </Button>
  )
}

/**
 * Every live treatment, act by act — état, next séance, last séance — folded shut.
 *
 * <p><b>Folded</b>: the cards already say what to do next, so the detail is the second question and one press
 * away (a native `&lt;details&gt;`, keyboard-operable with no state). Stacked rows, never a `&lt;table&gt;` — at
 * 320 px three columns of French act names cannot be a grid. Withdrawn acts are excluded (`activeItems`).</p>
 *
 * <p>⚠️ Every état is derived by the same helpers the treatment page uses (`planItemState`, `nextStepOf`), and
 * the séances by the one strip's helpers — never a bare « 2 / 6 », which three reviewers read as « two of six
 * done » on a treatment with one séance behind it.</p>
 */
function PlanActsFold({ plans }: { plans: TreatmentPlanDto[] }) {
  const groups = plans
    .map((p) => ({ plan: p, items: activeItems(p) }))
    .filter((g) => g.items.length > 0)
  if (groups.length === 0) return null
  // Nothing to unfold for one act with no séances — its card already says everything the fold would.
  if (groups.length === 1 && groups[0].items.length === 1 && (groups[0].items[0].steps?.length ?? 0) === 0) {
    return null
  }

  const several = groups.length > 1

  return (
    <details className="group">
      {/* `list-none` + the marker rule: Safari paints its own triangle from `::-webkit-details-marker`. */}
      <summary className="flex w-full cursor-pointer list-none items-center gap-1.5 py-1 text-xs text-muted-foreground touch-target hover:text-foreground [&::-webkit-details-marker]:hidden">
        <ChevronRight className="h-3.5 w-3.5 shrink-0 transition-transform group-open:rotate-90" aria-hidden="true" />
        Détail des séances
      </summary>

      <ul className="mt-1 flex flex-col gap-1.5">
        {groups.map((g) => (
          <li key={g.plan.id} className="flex flex-col gap-1.5">
            {several && (
              <span className="ps-2 text-2xs font-medium uppercase tracking-wider text-muted-foreground">
                {treatmentName(g.plan)}
              </span>
            )}
            <ul className="flex flex-col gap-1.5">
              {g.items.map((item) => (
                <PlanActLine key={item.id} item={item} />
              ))}
            </ul>
          </li>
        ))}
      </ul>
    </details>
  )
}

/** One act: what it is, where it has got to, and its séances in words. */
function PlanActLine({ item }: { item: TreatmentPlanItemDto }) {
  const state = planItemState(item)
  const steps = sortedSteps(item)
  // The most recent séance actually carried out — the act's own date when it has no séances.
  const lastDone = steps.length > 0
    ? steps.filter((s) => s.doneDate).map((s) => s.doneDate!).sort().at(-1) ?? null
    : item.doneDate

  return (
    <li className="flex flex-wrap items-center gap-x-2 gap-y-0.5 rounded-md bg-muted/40 px-2 py-1.5 text-xs">
      <span className="font-medium text-foreground">
        {item.designationFr}
        {item.toothNumbers.length > 0 && (
          <span className="font-normal text-muted-foreground"> · {item.toothNumbers.join(", ")}</span>
        )}
      </span>
      <Badge variant="secondary" className={cn("shrink-0 text-2xs", itemWorkflowBadgeClass(state))}>
        {planItemStateLabel(item, state)}
      </Badge>

      {steps.length > 0 && (
        <span className="flex items-center gap-1.5 text-muted-foreground">
          <SeancePips steps={steps} />
          {seanceSummary(steps)}
        </span>
      )}

      {lastDone && (
        <span className="ms-auto shrink-0 text-muted-foreground">
          dernière séance {formatDateFr(lastDone)}
        </span>
      )}
    </li>
  )
}

/**
 * The patient's other treatments, one chip per statut — each a button into the plans tab.
 *
 * <p>`gap-2` and a padded button rather than `touch-target`: these sit side by side, and a 44 px overlay on a
 * 20 px chip overhangs its neighbour so the later sibling wins the hit test.</p>
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
    <span className={cn("flex flex-wrap items-center gap-2", className)}>
      {counts.map(({ status, count }) => (
        <button
          key={status}
          type="button"
          onClick={onOpen}
          className="-my-1 rounded-full py-1 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          aria-label={`${count} traitement${count > 1 ? "s" : ""} ${planStatusLabel(status).toLowerCase()}`}
        >
          <Badge variant="secondary" className={`${planStatusBadgeClass(status)} hover:brightness-95`}>
            {count} {planStatusLabel(status).toLowerCase()}
          </Badge>
        </button>
      ))}
    </span>
  )
}
