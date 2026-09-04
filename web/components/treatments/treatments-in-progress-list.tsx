"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { CalendarCheck, CalendarPlus, Layers } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { TreatmentInProgressDto, TreatmentPlanDto } from "@/lib/api/types"
import type { PagedResponse } from "@/lib/api/paging"
import { formatDateFr, quoteFr } from "@/lib/format"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { cn } from "@/lib/utils"
import { PatientNameLink } from "@/components/patient-name-link"
import { planItemToPreset } from "@/components/treatment-plans/plan-next-action"
import { CreateAppointmentDialog, type PresetPlanAct } from "@/components/create-appointment-dialog"

/** Days after which a stalled treatment is worth pointing out. Two clinic weeks. */
const STALE_DAYS = 14

interface TreatmentsInProgressListProps {
  /**
   * Reports the **server's** total after every read, so the section around this list can state it — and `null`
   * when the read failed, which is « je ne sais pas », never « aucun ».
   *
   * <p>Must be referentially stable (a `setState` setter, or a `useCallback`): it is a dependency of the fetch.</p>
   */
  onTotalChange?: (total: number | null) => void
  /**
   * Free text over the patient's name and the devis number, applied **server-side** across the whole clinic.
   *
   * <p>Owned by the page rather than by this list, because one box has to narrow both halves of
   * « Traitements » — a search that reached only the devis table answered « qu'a-t-on convenu ? » while
   * leaving « où en est son traitement ? » showing every other patient in the cabinet.</p>
   */
  searchTerm?: string
}

/**
 * « Traitements en cours » — the acts this cabinet has started and not finished, with the next step and whether
 * a séance is booked for it.
 *
 * <p>The answer to the dentist's « je ne sais pas quoi planifier ». Everything needed to write it was already
 * stored — the devis, the steps, the appointments — and nothing put the three on one screen.</p>
 *
 * <p>⚠️ <b>Cards below `lg:`, not `md:`.</b> Five columns, and measured inside a page card that box is ~451 px
 * at 820 px — so at the `md:` hinge an iPad portrait would get the desktop grid and lose the last column, which
 * here is the « Planifier » button: the one control the screen exists for (§ 0/§ 1).</p>
 *
 * <p>⚠️ <b>Rows already booked stay on the list.</b> A treatment under way belongs here either way, and
 * filtering them out after the page was cut would make the pager's total describe a different set than the rows
 * — the trap the repository's own note names. A booked row states its date instead of offering a button.</p>
 *
 * <p>⚠️ <b>No money figure anywhere</b>, which is what lets this screen be open to the whole team: booking the
 * next séance is reception's job. A « reste à payer » here would move it behind the practitioner policy.</p>
 */
export function TreatmentsInProgressList({ onTotalChange, searchTerm }: TreatmentsInProgressListProps = {}) {
  const router = useRouter()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [data, setData] = useState<PagedResponse<TreatmentInProgressDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)
  const query = (searchTerm ?? "").trim()

  /*
   * Back to page 1 whenever the term changes. Without it, narrowing from a clinic-wide list while sitting on
   * page 3 requests an offset past the end of the filtered set and renders `PageRequest`'s deliberate empty —
   * « aucun traitement en cours » about a patient who has one.
   */
  useEffect(() => {
    setPage(1)
  }, [query])

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await treatmentPlansApi.treatmentsInProgress({ page, pageSize, search: query || undefined })
      setData(result)
      setFailed(false)
      // The SERVER's total, not `result.items.length` — the read is paged, so the rows in hand are at most one
      // page of it and a heading built from them would read « 25 » about a clinic with sixty.
      onTotalChange?.(result.totalCount)
    } catch {
      // The count is unknown now, not zero — same distinction as the list below, one level up: a heading
      // reading « rien en attente » over « je n'ai pas pu lire » is the reassuring half of a wrong pair.
      onTotalChange?.(null)
      // ⚠️ Never `setData({ items: [] })`: « aucun traitement en cours » and « je n'ai pas pu lire » are the
      // same picture and opposite facts, and here the wrong one is actively reassuring (§ 13).
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, query, onTotalChange])

  useEffect(() => {
    void load()
  }, [load])

  // A séance booked or cancelled elsewhere changes what this list offers, and a fiche saved elsewhere advances a
  // step — so both keys matter.
  useClinicRealtime([RealtimeResource.TreatmentPlans, RealtimeResource.Appointments], load)

  const rows = data?.items ?? []

  // Landing on a page past the end (the last treatment of page 2 was finished) steps back rather than rendering
  // the empty-state invite under a pager reading « 26–26 sur 25 ».
  useEffect(() => {
    if (!loading && data && rows.length === 0 && data.totalCount > 0 && page > 1) {
      setPage((p) => Math.max(1, p - 1))
    }
  }, [loading, data, rows.length, page])

  /** Opens the devis this act belongs to — the row's own destination. */
  const openPlan = (row: TreatmentInProgressDto) => router.push(`/treatment-plans/${row.planId}`)

  /**
   * « Planifier la séance » — the booking dialog, pre-filled, from here.
   *
   * <p>⚠️ It navigated to the devis workspace, where the dentist then had to find the act's row and press *its*
   * « Planifier l'étape ». The most frequent action in the feature cost an extra screen and a hunt, under a
   * label that promises otherwise — and the workspace's row action already opens exactly this dialog with
   * exactly this preset, so the fix is to reuse it rather than to reword the button.</p>
   *
   * <p>A read is needed first because the row is a projection and the dialog needs the plan aggregate (the act,
   * its steps, its fee, and the note that may hold its money). A failure falls back to the workspace, which is
   * where the dentist was going anyway.</p>
   */
  const [booking, setBooking] = useState<{
    plan: TreatmentPlanDto
    presets: PresetPlanAct[]
    defaultDay?: Date
  } | null>(null)
  const [preparingBooking, setPreparingBooking] = useState<string | null>(null)

  const book = async (row: TreatmentInProgressDto) => {
    setPreparingBooking(row.itemId)
    try {
      const plan = await treatmentPlansApi.get(row.planId)
      const item = plan.items.find((i) => i.id === row.itemId)
      if (!item) {
        router.push(`/treatment-plans/${plan.id}`)
        return
      }
      // The protocol's own interval, as the day the sheet opens on — « le 3 octobre » rather than today for a
      // step that cannot happen for eight weeks. Never a past date: an elapsed interval means « now ».
      //
      // `defaultDay`, never `defaultDate`: the due date is midnight (`DueFrom` is `previous.Date.AddDays(n)`),
      // and `defaultDate` reads an hour off the value — so the sheet opened on 00:00 and « Créer » answered
      // « Heure dans le passé ». The hour belongs to the form's own default.
      const due = row.nextStepDueFrom ? new Date(row.nextStepDueFrom) : null
      setBooking({
        plan,
        presets: [planItemToPreset(plan, item, (i) => i.procedureTypeId ?? undefined)],
        defaultDay: due && due.getTime() > Date.now() ? due : undefined,
      })
    } catch {
      router.push(`/treatment-plans/${row.planId}`)
    } finally {
      setPreparingBooking(null)
    }
  }

  const openAppointment = (row: TreatmentInProgressDto) =>
    router.push(`/appointments?appointmentId=${row.nextStepAppointmentId}`)

  if (failed) {
    return (
      <LoadFailureNotice
        message="Les traitements en cours n'ont pas pu être chargés."
        onRetry={() => void load()}
      />
    )
  }

  const isEmpty = !loading && rows.length === 0

  /*
   * Nothing under way — one quiet bordered line, and no pager.
   *
   * ⚠️ The full treatment (a table shell wrapping an `EmptyState`, then « 0 traitement en cours » beside a
   * page-size selector) was ~180 px of furniture stating that there is nothing to furnish. Harmless while this
   * list owned a route; on the merged page it sits ABOVE the devis list, so on every quiet day it pushed the
   * half that does have rows below the fold. A pager over zero rows also offers a control — « 25 par page » —
   * that cannot change what is shown.
   */
  if (isEmpty) {
    return (
      <div className="rounded-md border bg-card p-3">
        {/*
          Two empty KINDS, never one (§ 13): « this cabinet has no treatment under way » and « no treatment
          under way matches what you typed » are opposite facts, and the first is a claim about the records that
          a search must not be allowed to make. No « Ajouter » on the filtered branch either — the treatment may
          well exist under a name spelt differently.
        */}
        {query ? (
          <EmptyState
            size="compact"
            icon={Layers}
            title={`Aucun traitement en cours pour ${quoteFr(query)}`}
            description="Ce patient a peut-être un devis sans séance commencée — regardez « Devis et échéanciers » ci-dessous."
          />
        ) : (
          <EmptyState
            size="compact"
            icon={Layers}
            title="Aucun traitement en cours"
            description="Un acte commencé et non terminé apparaîtra ici avec l'étape qui reste à planifier."
          />
        )}
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="overflow-hidden rounded-md border bg-card">
        <div className={TABLE_ONLY_LG}>
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Patient</TableHead>
                <TableHead>Acte</TableHead>
                <TableHead>Prochaine étape</TableHead>
                <TableHead className="whitespace-nowrap">Dernière séance</TableHead>
                <TableHead className="text-right" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {/* No empty branch here or in the card tree: the early return above owns that state, so an
                  `isEmpty` arm in each of the two trees would be two more copies of one sentence, unreachable. */}
              {loading && rows.length === 0 ? (
                <SkeletonRows />
              ) : (
                rows.map((row) => (
                  /*
                   * The row opens the devis — that is where « et ensuite ? » is answered, and where the étapes,
                   * the money and the history live. `treatment-plans-table`'s pattern verbatim: `cursor-pointer`
                   * plus a row `onClick`, and the action cell stops the event so « Planifier la séance » does
                   * not also navigate away from the dialog it just opened.
                   */
                  <TableRow
                    key={row.itemId}
                    className="cursor-pointer"
                    onClick={() => openPlan(row)}
                  >
                    <TableCell onClick={(e) => e.stopPropagation()}>
                      {/* The name is the door to the fiche, as it is on every other list — `PatientNameLink`
                          also carries the two details a hand-written link drops: underlined at rest (a touch
                          screen has no hover to reveal it) and the 44 px coarse target. The row's own click
                          still opens the devis, so the cell stops the event. */}
                      {row.patientName ? (
                        <PatientNameLink patientId={row.patientId} name={row.patientName} />
                      ) : (
                        <span className="font-medium">Patient supprimé</span>
                      )}
                      {row.planNumber && (
                        <p className="font-mono text-2xs text-muted-foreground">{row.planNumber}</p>
                      )}
                    </TableCell>
                    <TableCell clamp title={row.designationFr}>
                      {row.designationFr}
                    </TableCell>
                    <TableCell>
                      <NextStepCell row={row} />
                    </TableCell>
                    <TableCell className="whitespace-nowrap">
                      <LastSeance row={row} />
                    </TableCell>
                    <TableCell className="text-right" onClick={(e) => e.stopPropagation()}>
                      <RowAction row={row} onBook={book} onOpen={openAppointment} busy={preparingBooking === row.itemId} />
                    </TableCell>
                  </TableRow>
                ))
              )}
            </TableBody>
          </Table>
        </div>

        <div className={cn(CARDS_ONLY_LG, "p-3")}>
          {loading && rows.length === 0 ? (
            <div className="space-y-2">
              {[0, 1, 2].map((i) => (
                <div key={i} className="h-28 animate-pulse rounded-md bg-muted" />
              ))}
            </div>
          ) : (
            <CardList
              ariaLabel="Traitements en cours"
              items={rows}
              getKey={(row) => row.itemId}
              title={(row) => row.patientName ?? "Patient supprimé"}
              // The card's title IS the link, stretched over the whole card by CardList's pseudo-element.
              href={(row) => `/treatment-plans/${row.planId}`}
              subtitle={(row) => row.designationFr}
              fields={(row) => [
                // Card order per § 6: identity → status → date. There is no money field on this surface.
                { label: "Prochaine étape", value: <NextStepCell row={row} /> },
                { label: "Dernière séance", value: <LastSeance row={row} /> },
              ]}
              // Its own full-width row, never the card header — see `RowAction`'s `block` note.
              primaryAction={(row) => (
                <RowAction row={row} onBook={book} onOpen={openAppointment} busy={preparingBooking === row.itemId} block />
              )}
            />
          )}
        </div>

        {data && (
          <DataTablePagination
            page={data}
            loading={loading}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            label={["traitement en cours", "traitements en cours"]}
          />
        )}
      </div>

      {/* The workspace's own « Planifier l'étape » dialog, reused verbatim — see `book`. */}
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
            void load()
          }}
        />
      )}
    </div>
  )
}

/** The pips + « 3 / 3 » + the step's own name. The same three readings the devis row's strip uses. */
function NextStepCell({ row }: { row: TreatmentInProgressDto }) {
  return (
    <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1">
      {/*
        The step's NAME leads. The column asks « prochaine étape » and that is the answer; the pips and the rank
        qualify it. Trailing, it landed last and least emphasised in both trees — and in the card, where the
        value wraps under its own label, it ended up the third of three lines.
      */}
      {row.nextStepLabel && <span className="text-sm">{row.nextStepLabel}</span>}
      <span className="flex shrink-0 items-center gap-1" aria-hidden="true">
        {Array.from({ length: row.stepsTotal }).map((_, i) => (
          <span
            key={i}
            className={cn(
              "size-2.5 flex-none rounded-full border-[1.5px]",
              i < row.stepsDone
                ? "border-success bg-success"
                : i === row.stepsDone
                  ? "border-dashed border-primary"
                  : "border-border",
            )}
          />
        ))}
      </span>
      {/*
        ⚠️ « étape » is VISIBLE, not `sr-only`, and that word is the whole difference between two readings of
        one shape. This is the next step's RANK — « la 3e des 3 » — while `PlanStepStrip`'s identical-looking
        counter on the devis is « done / total ». Unlabelled, a bridge with two of three steps carried out read
        « 3 / 3 » here and « 2 / 3 » one screen over, and the first of those says « terminé » about the act this
        list exists to say is *not*. The screen-reader label was already right; the sighted reader had nothing.
      */}
      {/*
        ⚠️ « à faire » is what the word « étape » alone could not carry. The defence for the bare label was
        recorded and is real — without it a bridge two of three done would read « 3 / 3 » here and « 2 / 3 » on
        the workspace — but three reviewers read « étape 2 / 2 » cold on a treatment with one of two séances
        done and all three took it for finished. The label tells you *which* counter it is only if you already
        know there are two kinds; « étape 2 / 2 à faire » says what it means to somebody who does not.
      */}
      <span className="rounded-md bg-accent px-2 py-0.5 text-2xs text-accent-foreground">
        étape{" "}
        <span className="font-mono tabular-nums">
          {row.nextStepNumber ?? row.stepsDone + 1} / {row.stepsTotal}
        </span>{" "}
        à faire
      </span>
    </div>
  )
}

/**
 * « il y a 26 jours », graded against **what the protocol actually asks for**.
 *
 * <p>⚠️ It was a flat {@link STALE_DAYS}-day amber from a constant with no reference to what the step was, and
 * that is a list that cries wolf: the shipped implant protocol waits eight to twelve weeks for osseointegration
 * between « Pose de l'implant » and « Désenfouissement », so a correctly-progressing implant was amber for ten
 * of its twelve weeks — and a list that flags correct clinical waiting as overdue is a list a dentist stops
 * reading, which is when it also stops catching the bridge that really was abandoned.</p>
 *
 * <p>Three readings now, not two. <b>« pas encore due »</b> while the interval has not elapsed — the state the
 * screen had no way to express at all; neutral once it has; amber only when the séance is genuinely late, which
 * for a protocol that states no interval is still the flat fortnight, because that is the best available guess
 * and it was never wrong for a one-week act.</p>
 */
function LastSeance({ row }: { row: TreatmentInProgressDto }) {
  if (!row.lastStepDoneOn) return <span className="text-muted-foreground">—</span>

  const days = Math.max(
    0,
    Math.floor((Date.now() - new Date(row.lastStepDoneOn).getTime()) / 86_400_000),
  )
  const label =
    days === 0 ? "aujourd'hui" : days === 1 ? "hier" : `il y a ${days} jours`

  const dueFrom = row.nextStepDueFrom ? new Date(row.nextStepDueFrom) : null
  const notDueYet = dueFrom != null && dueFrom.getTime() > Date.now()
  // Past the protocol's own interval where there is one, else past the fortnight.
  const late = dueFrom ? !notDueYet : days >= STALE_DAYS

  return (
    <span className="inline-flex flex-wrap items-baseline gap-x-1.5 text-xs">
      <span
        className={cn(late ? "font-medium text-warning-ink" : "text-muted-foreground")}
        title={formatDateFr(row.lastStepDoneOn)}
      >
        {label}
      </span>
      {notDueYet && dueFrom && (
        <span className="text-2xs text-muted-foreground">
          · pas encore due (à partir du {formatDateFr(row.nextStepDueFrom!)})
        </span>
      )}
    </span>
  )
}

function RowAction({
  row,
  onBook,
  busy,
  onOpen,
  block = false,
}: {
  row: TreatmentInProgressDto
  onBook: (row: TreatmentInProgressDto) => void | Promise<void>
  /** The plan read is in flight — a devis is a round trip away, and a second press would open two dialogs. */
  busy?: boolean
  onOpen: (row: TreatmentInProgressDto) => void
  /**
   * Full width on its own row — the card tree's `primaryAction` slot.
   *
   * ⚠️ In the card header (`actions`) this control is `shrink-0` beside the title, and « Planifier la séance »
   * is ~150 px of a ~288 px card: measured at 320 px, « Emna Belhadj » broke mid-word across three lines
   * (« Emna / Belha / dj ») and the act truncated to « Bridge 4… ». That is `CardList`'s own documented reason
   * for having this slot, and this list is precisely what it describes — the action a user opens the page to
   * perform.
   */
  block?: boolean
}) {
  const who = row.patientName ?? "ce patient"

  if (row.nextStepAppointmentId && row.nextStepAppointmentAt) {
    return (
      <Button
        variant="ghost"
        size="sm"
        className={cn(
          "gap-1 text-muted-foreground coarse:min-h-11",
          // `h-auto` + vertical padding in the card, because the label WRAPS there (see below) and a fixed
          // `h-8` would let two lines paint straight through the button's own edge.
          block ? "h-auto w-full justify-center py-2" : "h-8",
        )}
        onClick={() => onOpen(row)}
        aria-label={`Voir la séance du ${formatDateFr(row.nextStepAppointmentAt)} pour ${quoteFr(who)}`}
      >
        <CalendarCheck className="h-4 w-4 shrink-0" />
        {/*
          ⚠️ `whitespace-nowrap` ONLY in the table, where the column has the room. In the card it is a
          ~250 px label on a full-width control inside a ~248 px card at 320 px, so it overflowed on both
          sides — the icon clipped at the left edge and the year at the right (« … le 20 sept. 202 »), which
          reads as a rendering fault rather than as a date that did not fit. `whitespace-normal` and not merely
          the absence of `nowrap`: `ui/button.tsx`'s base class sets `whitespace-nowrap` on every button, so
          only an explicit override on this span gives the label a wrap opportunity at all.
        */}
        <span className={cn("min-w-0", block ? "whitespace-normal" : "whitespace-nowrap")}>
          prochaine séance le {formatDateFr(row.nextStepAppointmentAt)}
        </span>
      </Button>
    )
  }

  return (
    <Button
      variant="outline"
      size="sm"
      className={cn("h-8 gap-1 coarse:h-11", block && "w-full justify-center")}
      disabled={busy}
      onClick={() => void onBook(row)}
      aria-label={
        row.nextStepLabel
          ? `Planifier l'étape ${quoteFr(row.nextStepLabel)} pour ${quoteFr(who)}`
          : `Planifier la séance suivante pour ${quoteFr(who)}`
      }
    >
      <CalendarPlus className="h-4 w-4" />
      {busy ? "Ouverture…" : "Planifier la séance"}
    </Button>
  )
}

function SkeletonRows() {
  return (
    <>
      {[0, 1, 2].map((i) => (
        <TableRow key={i}>
          {[0, 1, 2, 3, 4].map((c) => (
            <TableCell key={c}>
              <span className="block h-4 animate-pulse rounded bg-muted" />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  )
}
