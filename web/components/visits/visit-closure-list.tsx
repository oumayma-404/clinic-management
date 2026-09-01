"use client"

import { Fragment, useState, type ReactNode } from "react"
import Link from "next/link"
import { useRouter } from "next/navigation"
import { Check, ClipboardCheck, EyeOff, FileText, Receipt, Undo2, UserCheck, UserX } from "lucide-react"
import { toast } from "sonner"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { EmptyState } from "@/components/ui/empty-state"
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/components/ui/table"
import { BillDentalRecordDialog } from "@/components/factures/bill-dental-record-dialog"
import { appointmentsApi } from "@/lib/api/appointments"
import { dentalRecordsApi } from "@/lib/api/dental-records"
import type { DentalRecordDto, VisitClosureStep, VisitToCloseDto } from "@/lib/api/types"
import { showErrorToast } from "@/lib/errors"
import { formatDateTime } from "@/lib/format"
import { ZONES, zoneChipClass, zoneForPath, type ZoneKey } from "@/lib/zones"
import { cn } from "@/lib/utils"
import { NothingToBillDialog } from "./nothing-to-bill-dialog"
import { DisregardVisitsDialog } from "./disregard-visits-dialog"
import { visitClosureDayGroups, type VisitClosureDayGroup } from "./visit-closure-days"

/**
 * « À clôturer » — the séances still owing a presence, a fiche or a money document.
 *
 * <p><b>The row asks ONE question.</b> The three answers are drawn as progress, but only `nextStep` gets controls:
 * three simultaneous red actions on a visit that ended an hour ago is nagging, and two of them cannot be answered
 * yet — a visit nobody has confirmed happened is not « missing » a fiche, and a séance with no fiche has no acts
 * to price. The cascade comes from the server (`VisitClosureRules`); re-deriving it here would be a second copy of
 * the rule, and the copy that drifts is the one that starts asking the wrong question.</p>
 *
 * <p><b>It rebuilds nothing.</b> « Ajouter la fiche » lands on the patient page's existing record modal through the
 * deep link the post-visit prompt already uses, and « Encaisser » lands on the same page's own billing action. The
 * two presence buttons are an ordinary status update.</p>
 *
 * <p>`_LG` and not `_md:` for the table/cards hinge: an iPad portrait is 820 px and would otherwise get the
 * desktop grid *and* the 256 px rail for a row carrying a name, a time, a practitioner, three states and two
 * buttons.</p>
 */

interface VisitClosureListProps {
  visits: VisitToCloseDto[]
  loading?: boolean
  /** Refetch after any action — a closed visit leaves the list, and the count above it moves. */
  onChanged: () => void
  /**
   * Showing the séances somebody has taken off the list rather than the ones still asking a question.
   *
   * <p>The rows are the same rows and the layout is identical, so the only honest difference is what they can
   * be asked: a set-aside séance offers « Remettre dans la liste » and nothing else. Offering « Venu » on one
   * would be asking a question about a row the practice has said is not a question.</p>
   */
  disregardedView?: boolean
  /** Rendered when there is genuinely nothing left. A *failed* read must not reach this — see the page. */
  emptyTitle?: string
  emptyDescription?: string
  /**
   * Rendered as the last row **inside** the list's own surface — in practice `/a-cloturer`'s pager.
   *
   * <p>`DataTablePagination` carries a `border-t` and no border of its own, precisely so it reads as a card's
   * footer; rendered as a page-level sibling it was a filet flottant under a borderless slab. Passed in rather
   * than owned here because the page owns the paging state, and the agenda strip has no pager at all.</p>
   */
  footer?: ReactNode
}

export function VisitClosureList({
  visits,
  loading = false,
  onChanged,
  disregardedView = false,
  emptyTitle = "Rien à clôturer 🎉",
  // ⚠️ Two sentences, and the second is the point. « Toutes les séances passées ont… » states a fact about the
  // data; on the one screen whose whole job is to list what is *unfinished*, an empty list is an achievement,
  // and saying so is the difference between « the software found nothing » and « you are up to date ».
  emptyDescription =
    "Toutes les séances passées ont leur présence, leur fiche et leur encaissement. " +
    "Beau travail — le dossier du cabinet est à jour.",
  footer,
}: VisitClosureListProps) {
  const router = useRouter()
  const [busyId, setBusyId] = useState<string | null>(null)
  const [nothingToBillFor, setNothingToBillFor] = useState<VisitToCloseDto | null>(null)
  /** The séance(s) « Retirer de la liste » is being asked about — one row, or every row on screen. */
  const [disregarding, setDisregarding] = useState<VisitToCloseDto[] | null>(null)
  const [billing, setBilling] = useState<{ record: DentalRecordDto; patientName: string } | null>(null)

  /** « Venu » / « Absent ». The two legal answers to the presence question, and an ordinary status update. */
  const answerPresence = async (visit: VisitToCloseDto, came: boolean) => {
    setBusyId(visit.appointmentId)
    try {
      await appointmentsApi.update(visit.appointmentId, { status: came ? "Completed" : "NoShow" })
      toast.success(came ? "Séance marquée comme honorée." : "Patient marqué comme absent.")
      onChanged()
    } catch (err) {
      // The dialog-stays-open rule, one surface over: the row stays exactly as it was and the message says why.
      showErrorToast(err)
    } finally {
      setBusyId(null)
    }
  }

  /**
   * Put a set-aside séance back — on the list, and back into the dashboard's figures.
   *
   * <p>Load-bearing rather than a courtesy: a removal nobody can undo is a black hole, and it is what makes the
   * mark safe to offer over a whole screen at once.</p>
   */
  const restoreToWorklist = async (visit: VisitToCloseDto) => {
    setBusyId(visit.appointmentId)
    try {
      await appointmentsApi.disregardVisits([visit.appointmentId], false)
      toast.success("Séance remise dans la liste.")
      onChanged()
    } catch (err) {
      showErrorToast(err)
    } finally {
      setBusyId(null)
    }
  }

  /** The patient page's own record modal, through the deep link the post-visit prompt already uses. */
  const openFiche = (visit: VisitToCloseDto) =>
    router.push(
      `/patients/${encodeURIComponent(visit.patientId)}?addRecord=1&appointmentId=${encodeURIComponent(
        visit.appointmentId,
      )}`,
    )

  /**
   * « Encaisser » — the fiche's own `BillDentalRecordDialog`, opened here rather than on the patient page.
   *
   * The fiche is fetched on the click because the row carries only its id; that is one request on an explicit
   * action, and the alternative was landing the user on a records tab to find the séance themselves.
   */
  const openBilling = async (visit: VisitToCloseDto) => {
    if (!visit.dentalRecordId) return
    setBusyId(visit.appointmentId)
    try {
      const records = await dentalRecordsApi.list(visit.patientId)
      const record = records.find((r) => r.id === visit.dentalRecordId)
      if (!record) {
        showErrorToast(new Error("Fiche de soins introuvable."))
        return
      }
      setBilling({ record, patientName: visit.patientName })
    } catch (err) {
      showErrorToast(err)
    } finally {
      setBusyId(null)
    }
  }

  const zone = zoneForPath("/a-cloturer")
  const dayGroups = visitClosureDayGroups(visits)

  return (
    <>
      {/*
        One bordered surface, the shape every other list in the app has (`invoices-table`,
        `treatment-plans-table`, `caisse-ledger-table`). `ui/table.tsx` paints `bg-card` and takes its radius from
        the parent — with no parent radius it rendered as a square, borderless white slab on the tinted page
        ground, which was the whole of « ces pages ne suivent pas la charte ». The page's pager renders inside it.
      */}
      <div className="rounded-md border bg-card">
        {loading ? (
          <>
            {/*
              ⚠️ Two skeletons, one per tree, and the table one is what was MISSING. The card list has always had
              its own `loading` state; the table branch had none, so `!loading && visits.length === 0` fell
              through to a `<tbody>` with no rows — headers over a void. Changing the période from 7 to 90 days
              therefore painted « aucune ligne » for the length of the fetch, which is the exact reading this
              screen takes care to avoid for a failed read.
            */}
            <div
              className={cn(TABLE_ONLY_LG, "space-y-3 p-4")}
              role="status"
              aria-label="Chargement des séances à clôturer"
            >
              <div className="flex gap-4 border-b pb-3">
                {["w-1/5", "w-1/5", "w-1/6", "w-1/4", "w-1/6"].map((width, i) => (
                  <div key={i} className={cn("h-4 animate-pulse rounded bg-muted", width)} />
                ))}
              </div>
              {[0, 1, 2, 3].map((i) => (
                <div key={i} className="flex gap-4">
                  {["w-1/5", "w-1/5", "w-1/6", "w-1/4", "w-1/6"].map((width, j) => (
                    <div key={j} className={cn("h-4 animate-pulse rounded bg-muted", width)} />
                  ))}
                </div>
              ))}
            </div>
            <CardList
              className={CARDS_ONLY_LG}
              items={[]}
              getKey={(v: VisitToCloseDto) => v.appointmentId}
              ariaLabel="Séances à clôturer"
              loading
              title={(v: VisitToCloseDto) => v.patientName}
              fields={() => []}
            />
          </>
        ) : visits.length === 0 ? (
          <EmptyState
            icon={ClipboardCheck}
            title={emptyTitle}
            description={emptyDescription}
            chipClassName={zoneChipClass(zone)}
          />
        ) : (
          <>
            {/* ── Desktop / large tablet ─────────────────────────────────────────────────────────────── */}
            <Table containerClassName={TABLE_ONLY_LG}>
              <TableHeader sticky>
                <TableRow>
                  <TableHead>Patient</TableHead>
                  <TableHead>Séance</TableHead>
                  <TableHead>Praticien</TableHead>
                  <TableHead>État</TableHead>
                  <TableHead className="text-end">À faire</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {dayGroups.map((group) => (
                  <Fragment key={group.key || group.label}>
                    {/* A day separator, not a column: the list is most-recent-first over up to 90 days, and a
                        column of near-identical timestamps gives the eye nothing to navigate by. */}
                    <TableRow className="hover:bg-transparent">
                      <TableCell
                        colSpan={5}
                        className="bg-muted/40 py-1.5 font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground"
                      >
                        <DayHeading group={group} />
                      </TableCell>
                    </TableRow>
                    {group.visits.map((visit) => (
                      <TableRow key={visit.appointmentId}>
                        <TableCell className="relative font-medium">
                          {/* The row's one question, as a colour, before a word is read. `reminder-log-table`'s
                              stripe: absolute in the first cell, so it needs no column of its own. */}
                          <span
                            aria-hidden="true"
                            className="absolute inset-y-0 start-0 w-[3px]"
                            style={{ backgroundColor: stripeFor(visit) }}
                          />
                          <Link
                            href={`/patients/${encodeURIComponent(visit.patientId)}`}
                            className="underline-offset-4 hover-hover:hover:underline"
                          >
                            {visit.patientName}
                          </Link>
                        </TableCell>
                        <TableCell className="text-muted-foreground">
                          <div>{formatDateTime(visit.appointmentDateTime)}</div>
                          {visit.procedures.length > 0 && (
                            <div className="text-xs">{visit.procedures.join(" · ")}</div>
                          )}
                          {/* The motif belongs in BOTH trees. It was rendered in the cards and nowhere here, so
                              a « Rien à facturer » visit explained itself on a phone and not at the desk. */}
                          {visit.nothingToBillReason && (
                            <div className="text-xs">Rien à facturer : {visit.nothingToBillReason}</div>
                          )}
                          {/* ⚠️ The note that settled the money, NAMED. `invoiceId`/`invoiceNumber` were the only
                              two fields of the DTO nothing read: the « Encaissement » tick said the money was done
                              and not by what, on the screen whose whole job is « what is still owed ». A motif is
                              already explained here; a settled encaissement was not. */}
                          {visit.invoiceNumber && (
                            <div className="text-xs">
                              Facturé sur{" "}
                              <Link
                                href={`/factures?search=${encodeURIComponent(visit.invoiceNumber)}`}
                                className="font-medium text-primary underline-offset-4 hover:underline"
                              >
                                n° {visit.invoiceNumber}
                              </Link>
                            </div>
                          )}
                        </TableCell>
                        <TableCell className="text-muted-foreground">{visit.doctorName ?? "—"}</TableCell>
                        <TableCell>
                          <ClosureProgress visit={visit} />
                        </TableCell>
                        <TableCell className="text-end">
                          <div className="flex flex-wrap justify-end gap-2">
                            <RowActions
                              visit={visit}
                              busy={busyId === visit.appointmentId}
                              onPresence={answerPresence}
                              onFiche={openFiche}
                              onBilling={openBilling}
                              onNothingToBill={setNothingToBillFor}
                              onDisregard={(v) => setDisregarding([v])}
                              onRestore={restoreToWorklist}
                              disregardedView={disregardedView}
                            />
                          </div>
                        </TableCell>
                      </TableRow>
                    ))}
                  </Fragment>
                ))}
              </TableBody>
            </Table>

            {/* ── Phone / tablet portrait ────────────────────────────────────────────────────────────── */}
            <div className={CARDS_ONLY_LG}>
              {dayGroups.map((group) => (
                <section key={group.key || group.label}>
                  <h3 className="border-b bg-muted/40 px-4 py-1.5 font-mono text-2xs uppercase tracking-[0.07em] text-muted-foreground">
                    <DayHeading group={group} />
                  </h3>
                  <CardList
                    items={group.visits}
                    getKey={(v) => v.appointmentId}
                    ariaLabel={`Séances à clôturer — ${group.label}`}
                    title={(v) => v.patientName}
                    // The table's stripe, through the primitive's own accent bar — same hue, same meaning.
                    accent={stripeFor}
                    href={(v) => `/patients/${encodeURIComponent(v.patientId)}`}
                    subtitle={(v) => formatDateTime(v.appointmentDateTime)}
                    status={(v) => <ClosureProgress visit={v} />}
                    fields={(v) => [
                      v.doctorName ? { label: "Praticien", value: v.doctorName } : null,
                      v.procedures.length > 0 ? { label: "Actes", value: v.procedures.join(" · ") } : null,
                      v.nothingToBillReason
                        ? { label: "Rien à facturer", value: v.nothingToBillReason }
                        : null,
                      // Same fact in the card tree — see the note in the table above.
                      v.invoiceNumber ? { label: "Facturé sur", value: `n° ${v.invoiceNumber}` } : null,
                    ]}
                    primaryAction={(v) => (
                      <div className="flex flex-wrap gap-2">
                        <RowActions
                          visit={v}
                          busy={busyId === v.appointmentId}
                          onPresence={answerPresence}
                          onFiche={openFiche}
                          onBilling={openBilling}
                          onNothingToBill={setNothingToBillFor}
                          onDisregard={(v) => setDisregarding([v])}
                          onRestore={restoreToWorklist}
                          disregardedView={disregardedView}
                        />
                      </div>
                    )}
                  />
                </section>
              ))}
            </div>
          </>
        )}

        {/*
          « Tout retirer » — the bulk case, and the reason it is a screenful rather than a checkbox selection.
          The practice reaching for this has a hundred-odd séances that should never have existed, and the
          honest unit is « ce que je vois »: the confirmation names the exact count, so there is no ambiguity
          about what a press covers. ⚠️ Not rendered in the set-aside view — « tout remettre » would undo a
          deliberate action in one click, which is the opposite of the care this control is built with.
        */}
        {!disregardedView && !loading && visits.length > 0 ? (
          <div className="flex justify-end border-t px-4 py-2">
            <Button
              type="button"
              size="sm"
              variant="ghost"
              className="min-h-11 text-muted-foreground coarse:min-h-11"
              onClick={() => setDisregarding(visits)}
            >
              <EyeOff aria-hidden="true" className="me-1.5 size-4" />
              {/* ⚠️ The article moves with the count, not just the noun: « Retirer les 1 séance affichée » is
                  what pluralising the noun alone gives, and it reads as broken French. */}
              {visits.length === 1
                ? "Retirer la séance affichée"
                : `Retirer les ${visits.length.toLocaleString("fr-TN")} séances affichées`}
            </Button>
          </div>
        ) : null}

        {footer}
      </div>

      <DisregardVisitsDialog
        visits={disregarding}
        onOpenChange={(open) => !open && setDisregarding(null)}
        onDone={() => {
          setDisregarding(null)
          onChanged()
        }}
      />

      <NothingToBillDialog
        visit={nothingToBillFor}
        onOpenChange={(open) => !open && setNothingToBillFor(null)}
        onSuccess={() => {
          setNothingToBillFor(null)
          onChanged()
        }}
      />

      <BillDentalRecordDialog
        record={billing?.record ?? null}
        patientName={billing?.patientName ?? ""}
        onOpenChange={(open) => !open && setBilling(null)}
        onSuccess={() => {
          setBilling(null)
          onChanged()
        }}
      />
    </>
  )
}

/**
 * The three answers a séance owes, and **the zone each one is recorded in**.
 *
 * <p>A row asks one question, and there are only ever three of them — so the colour that carries the most is
 * <i>which</i> question this row is asking, not how it is going. That is a « where does this go? », which is
 * exactly what a zone hue answers: « Venue » is a status on the agenda, « Fiche » is the clinical record,
 * « Encaissement » is la caisse, and each action on the row navigates to that zone's own surface. The three hues
 * are also the only three in the palette that are legible against each other at chip size.</p>
 *
 * <p>Deliberately <b>not</b> `ui/status-tone.ts`: those six tones mean « nothing to do / booked / agreed /
 * underway / finished / failed », and none of the three steps is any of those — they are all « pas encore », on
 * a visit where nothing has gone wrong. Borrowing a status tone would say something false in colour.</p>
 *
 * <p>Exhaustive over `VisitClosureStep`, so a fourth step added to the wire is a `tsc` error rather than a row
 * that renders with no hue at all.</p>
 */
const CLOSURE_STEPS: Record<VisitClosureStep, {
  label: string
  zone: ZoneKey
  /** Whether the visit has already answered this step. Read from the server's flags, never re-derived. */
  answered: (visit: VisitToCloseDto) => boolean
}> = {
  Presence: { label: "Venue", zone: "daily", answered: (v) => v.presenceAnswered },
  Fiche: { label: "Fiche", zone: "clinical", answered: (v) => v.ficheRecorded },
  Billing: { label: "Encaissement", zone: "money", answered: (v) => v.billingSettled },
}

/** The order the cascade asks them in. Server-derived per row (`nextStep`); this is only the drawing order. */
const CLOSURE_STEP_ORDER: VisitClosureStep[] = ["Presence", "Fiche", "Billing"]

/**
 * The row's left stripe — its next step's hue, as a raw colour for an inline `backgroundColor`.
 *
 * <p>Interpolated rather than written out three times because a `style` value is not a class: Tailwind's
 * source scan is irrelevant here, so deriving it is what keeps a step's stripe and its badge from disagreeing.</p>
 *
 * <p>⚠️ <b>`--zone-*`, never `--color-zone-*`.</b> The `--color-` aliases are `@theme inline` entries, and
 * Tailwind v4 emits such a variable to `:root` only when it judges it **used** — so which ones exist at runtime
 * is a property of the current build, not of the stylesheet. Measured in the browser:
 * <b>`--color-zone-clinical` was absent</b> while `--color-zone-daily` and `--color-zone-money` resolved, and an
 * earlier reading had all three empty. So a `var(--color-…)` in an inline `style` paints <b>transparent</b>
 * whenever it loses that coin flip — silently, because an unresolvable custom property is not an error. The raw
 * tokens are declared unconditionally on `:root` and again under `.dark`, so they always resolve *and* follow the
 * theme; the `zone.wash`/`zone.text` <i>classes</i> above were never affected.
 * (`reminder-log-table.tsx`'s `STRIPE` was the source of this pattern and had the identical defect; both are
 * fixed.)</p>
 */
function stripeFor(visit: VisitToCloseDto): string {
  return `var(--zone-${CLOSURE_STEPS[visit.nextStep].zone})`
}

/**
 * The three answers as progress, never as three alarms.
 *
 * <p>A satisfied step is stated in words with a tick, not only by colour — a greyscale printout and a screen
 * reader get the same facts. The pending ones are neutral: they are not failures, they are the rest of the visit.</p>
 *
 * <p>⚠️ <b>A pending step is an empty circle, not a « ✗ ».</b> The cross was the drawing contradicting the
 * paragraph above it: it is the universal mark of failure, and there were three of them on every row of a screen
 * that exists to be emptied — so an ordinary afternoon's work read as a page of errors. The one step the row is
 * actually asking for is picked out instead (`nextStep`, which the server already computes), so the row states
 * its question rather than making the reader infer it from three identical badges.</p>
 *
 * <p>⚠️ <b>The « fait » badge had no colour at all, and no layer could say so.</b> It asked for
 * `text-success-ink`/`border-success-ink` — and `--success-ink` does not exist; `globals.css` defines that
 * darkened step for amber only, because `--warning` is the one token that fails contrast against its own wash.
 * Tailwind generates nothing for an undeclared token, so the tick rendered in the outline badge's default ink on
 * every row of the page. It is the `--success`/`--success-wash` pair every other surface uses now.</p>
 *
 * <p>And the picked-out step takes <b>its own step's hue</b> rather than one shared `--primary`: with three
 * questions in the list, the badge's colour is the fastest way to see which rows are waiting on money and which
 * on a fiche — see `CLOSURE_STEPS`.</p>
 */
function ClosureProgress({ visit }: { visit: VisitToCloseDto }) {
  return (
    <ul className="flex flex-wrap gap-1.5">
      {CLOSURE_STEP_ORDER.map((key) => {
        const step = CLOSURE_STEPS[key]
        const done = step.answered(visit)
        const isNext = !done && visit.nextStep === key
        const zone = ZONES[step.zone]
        return (
          <li key={key}>
            <Badge
              variant="outline"
              className={cn(
                "gap-1 font-normal",
                done && "border-success/25 bg-success-wash text-success",
                isNext && cn("font-medium", zone.wash, zone.text, zone.border),
                !done && !isNext && "text-muted-foreground",
              )}
            >
              {done ? (
                <Check aria-hidden="true" className="size-3" />
              ) : (
                // A hollow ring: « pas encore », with no claim that anything went wrong. The asked-for step
                // fills its own, so the row's question is legible without reading a word.
                <span
                  aria-hidden="true"
                  className={cn(
                    "size-2 rounded-full border-[1.5px] border-current",
                    isNext && "bg-current",
                  )}
                />
              )}
              <span className="sr-only">
                {done ? "Fait : " : isNext ? "À faire maintenant : " : "En attente : "}
              </span>
              {step.label}
            </Badge>
          </li>
        )
      })}
    </ul>
  )
}

/**
 * A journée's heading, in both trees — the label, its age, and how many séances it holds.
 *
 * <p>One component rather than the two copies it replaced: the table's `colSpan` row and the card list's `<h3>`
 * carried byte-identical markup, and the age chip would have been the third thing to keep in step by hand.</p>
 *
 * <p>The chip appears only from two days back (`daysAgo` is `null` below that) and is the one amber thing on the
 * screen. It exists because the list opens on <i>today</i>: the séance nobody has closed since Monday is now at
 * the bottom of the page rather than at the top of it, and its age has to be said somewhere.</p>
 */
function DayHeading({ group }: { group: VisitClosureDayGroup }) {
  return (
    <span className="flex items-center justify-between gap-3">
      <span className="flex min-w-0 items-center gap-2">
        <span className="truncate text-foreground">{group.label}</span>
        {group.daysAgo !== null && (
          <span className="shrink-0 rounded-full bg-warning-wash px-1.5 text-warning-ink">
            il y a {group.daysAgo} jours
          </span>
        )}
      </span>
      <span className="shrink-0">
        {group.visits.length} séance{group.visits.length > 1 ? "s" : ""}
      </span>
    </span>
  )
}

/** The controls for the ONE question this visit is asking. */
function RowActions({
  visit,
  busy,
  onPresence,
  onFiche,
  onBilling,
  onNothingToBill,
  onDisregard,
  onRestore,
  disregardedView,
}: {
  visit: VisitToCloseDto
  busy: boolean
  onPresence: (visit: VisitToCloseDto, came: boolean) => void
  onFiche: (visit: VisitToCloseDto) => void
  onBilling: (visit: VisitToCloseDto) => void | Promise<void>
  onNothingToBill: (visit: VisitToCloseDto) => void
  onDisregard: (visit: VisitToCloseDto) => void
  onRestore: (visit: VisitToCloseDto) => void | Promise<void>
  disregardedView: boolean
}) {
  // A set-aside séance is not asking anything, so it is offered nothing but the way back. Showing « Venu »
  // here would be putting a question to a row the practice has already said is not one.
  if (disregardedView) {
    return (
      <Button
        size="sm"
        variant="outline"
        disabled={busy}
        onClick={() => void onRestore(visit)}
        aria-label={`Remettre la séance de ${visit.patientName} dans la liste`}
      >
        <Undo2 aria-hidden="true" className="me-1.5 size-4" />
        Remettre dans la liste
      </Button>
    )
  }

  // ⚠️ Offered on EVERY row, whatever the next question is — and deliberately last, after the answers. A row
  // that should not be on the list at all is not a stage of the cascade, so gating it behind « Presence » would
  // hide it from exactly the séances a bad import leaves in « Fiche » and « Billing ».
  const setAside = (
    <Button
      size="sm"
      variant="ghost"
      disabled={busy}
      onClick={() => onDisregard(visit)}
      aria-label={`Retirer la séance de ${visit.patientName} de la liste`}
      title="Retirer de la liste sans rien affirmer sur cette séance"
    >
      <EyeOff aria-hidden="true" className="me-1.5 size-4" />
      Retirer
    </Button>
  )

  if (visit.nextStep === "Presence") {
    // Each row's pair carries the patient's name: this list is many rows of identical actions, so « Venu » alone
    // announces the same thing a dozen times with no way to tell which séance is being answered.
    return (
      <>
        <Button
          size="sm"
          disabled={busy}
          onClick={() => onPresence(visit, true)}
          aria-label={`${visit.patientName} est venu`}
        >
          <UserCheck aria-hidden="true" className="me-1.5 size-4" />
          Venu
        </Button>
        <Button
          size="sm"
          variant="outline"
          disabled={busy}
          onClick={() => onPresence(visit, false)}
          aria-label={`${visit.patientName} est absent`}
        >
          <UserX aria-hidden="true" className="me-1.5 size-4" />
          Absent
        </Button>
        {setAside}
      </>
    )
  }

  if (visit.nextStep === "Fiche") {
    return (
      <>
        <Button size="sm" disabled={busy} onClick={() => onFiche(visit)}>
          <FileText aria-hidden="true" className="me-1.5 size-4" />
          Ajouter la fiche
        </Button>
        {setAside}
      </>
    )
  }

  return (
    <>
      <Button size="sm" disabled={busy} onClick={() => void onBilling(visit)}>
        <Receipt aria-hidden="true" className="me-1.5 size-4" />
        Encaisser
      </Button>
      <Button size="sm" variant="outline" disabled={busy} onClick={() => onNothingToBill(visit)}>
        Rien à facturer
      </Button>
      {setAside}
    </>
  )
}
