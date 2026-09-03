"use client"

import { useCallback, useEffect, useState } from "react"
import { useRouter } from "next/navigation"
import { CalendarCheck, CalendarPlus, Layers } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow, TableEmptyRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { TreatmentInProgressDto } from "@/lib/api/types"
import type { PagedResponse } from "@/lib/api/paging"
import { formatDateFr, quoteFr } from "@/lib/format"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { cn } from "@/lib/utils"

/** Days after which a stalled treatment is worth pointing out. Two clinic weeks. */
const STALE_DAYS = 14

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
export function TreatmentsInProgressList() {
  const router = useRouter()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [data, setData] = useState<PagedResponse<TreatmentInProgressDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [failed, setFailed] = useState(false)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await treatmentPlansApi.treatmentsInProgress({ page, pageSize })
      setData(result)
      setFailed(false)
    } catch {
      // ⚠️ Never `setData({ items: [] })`: « aucun traitement en cours » and « je n'ai pas pu lire » are the
      // same picture and opposite facts, and here the wrong one is actively reassuring (§ 13).
      setFailed(true)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize])

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

  const book = (row: TreatmentInProgressDto) =>
    router.push(`/treatment-plans/${row.planId}`)

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
              {loading && rows.length === 0 ? (
                <SkeletonRows />
              ) : isEmpty ? (
                <TableEmptyRow colSpan={5}>
                  <EmptyState
                    size="compact"
                    icon={Layers}
                    title="Aucun traitement en cours"
                    description="Un acte commencé et non terminé apparaîtra ici avec l'étape qui reste à planifier."
                  />
                </TableEmptyRow>
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
                    <TableCell>
                      <span className="font-medium">{row.patientName ?? "Patient supprimé"}</span>
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
                      <RowAction row={row} onBook={book} onOpen={openAppointment} />
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
          ) : isEmpty ? (
            <EmptyState
              size="compact"
              icon={Layers}
              title="Aucun traitement en cours"
              description="Un acte commencé et non terminé apparaîtra ici avec l'étape qui reste à planifier."
            />
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
                <RowAction row={row} onBook={book} onOpen={openAppointment} block />
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
      <span className="rounded-md bg-accent px-2 py-0.5 text-2xs text-accent-foreground">
        étape{" "}
        <span className="font-mono tabular-nums">
          {row.nextStepNumber ?? row.stepsDone + 1} / {row.stepsTotal}
        </span>
      </span>
    </div>
  )
}

/**
 * « il y a 26 jours ». Amber past {@link STALE_DAYS} — the whole point of the list is the treatment nobody has
 * come back for, and the tone is on the figure rather than on a tinted row (nothing has gone wrong).
 */
function LastSeance({ row }: { row: TreatmentInProgressDto }) {
  if (!row.lastStepDoneOn) return <span className="text-muted-foreground">—</span>

  const days = Math.max(
    0,
    Math.floor((Date.now() - new Date(row.lastStepDoneOn).getTime()) / 86_400_000),
  )
  const label =
    days === 0 ? "aujourd'hui" : days === 1 ? "hier" : `il y a ${days} jours`

  return (
    <span
      className={cn("text-xs", days >= STALE_DAYS ? "font-medium text-warning-ink" : "text-muted-foreground")}
      title={formatDateFr(row.lastStepDoneOn)}
    >
      {label}
    </span>
  )
}

function RowAction({
  row,
  onBook,
  onOpen,
  block = false,
}: {
  row: TreatmentInProgressDto
  onBook: (row: TreatmentInProgressDto) => void
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
        className={cn("h-8 gap-1 text-muted-foreground coarse:h-11", block && "w-full justify-center")}
        onClick={() => onOpen(row)}
        aria-label={`Voir la séance du ${formatDateFr(row.nextStepAppointmentAt)} pour ${quoteFr(who)}`}
      >
        <CalendarCheck className="h-4 w-4" />
        <span className="whitespace-nowrap">
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
      onClick={() => onBook(row)}
      aria-label={
        row.nextStepLabel
          ? `Planifier l'étape ${quoteFr(row.nextStepLabel)} pour ${quoteFr(who)}`
          : `Planifier la séance suivante pour ${quoteFr(who)}`
      }
    >
      <CalendarPlus className="h-4 w-4" />
      Planifier la séance
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
