"use client"

import { useCallback, useEffect, useState } from "react"
import { CalendarPlus, CircleDashed } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { CardList, CARDS_ONLY_LG, TABLE_ONLY_LG } from "@/components/ui/card-list"
import { DataTablePagination } from "@/components/ui/data-table-pagination"
import { EmptyState } from "@/components/ui/empty-state"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PatientNameLink } from "@/components/patient-name-link"
import { CreateAppointmentDialog } from "@/components/create-appointment-dialog"
import {
  ContinueSessionDialog,
  type ContinuationChoice,
} from "@/components/treatment-plans/continue-session-dialog"
import { treatmentPlansApi } from "@/lib/api/treatment-plans"
import type { UnfinishedActDto } from "@/lib/api/types"
import type { PagedResponse } from "@/lib/api/paging"
import { DEFAULT_PAGE_SIZE } from "@/lib/api/paging"
import { formatDT, formatDateFr } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { RealtimeResource } from "@/lib/realtime/clinic-hub"
import { useClinicRealtime } from "@/lib/realtime/use-clinic-realtime"
import { cn } from "@/lib/utils"

/**
 * « Suites à planifier » — the acts a dentist marked « non terminé » that nothing has picked up yet.
 *
 * <p>⚠️ <b>It exists because « Traitements en cours » structurally cannot see these acts.</b> That list reads
 * treatment plans, so it shows an unfinished act the moment the act is on a devis — and an act done as a
 * one-off and left unfinished has no devis by definition. That is exactly the case the whole continuation
 * feature is about, and it was the one case no worklist in this product could surface: the dentist had to
 * remember, while booking, that the séance of three weeks ago was left half done.</p>
 *
 * <p>⚠️ <b>It is NOT a fourth question in « À clôturer », and that was a deliberate call.</b> The closure
 * cascade asks three things about a <i>visit</i> — est-il venu · qu'a-t-on fait · combien a-t-il payé — and each
 * is open because a record is <b>absent</b>. « L'acte est-il terminé ? » is not of that shape: its default
 * answer is « oui », so an untouched fiche can never raise it, and a séance holding an unfinished act is
 * <i>completely closed as a visit</i> — the patient came, the fiche is recorded, the money is settled. What is
 * open is the <b>treatment</b>. Folding it into the cascade would mean a row that answering the visit's own
 * questions can never clear, and would make the page's count and the dashboard chip behind it start counting a
 * different thing. So it is a sibling tab on the same page: the practice opens one screen and sees everything
 * still owed, which is what was actually asked for.</p>
 *
 * <p>⚠️ <b>Its action is a PRE-FILLER, never a second writer.</b> « Planifier la suite » opens the ordinary
 * booking dialog with the continuation already chosen, and the devis is minted by `materialiseTreatments` when
 * that booking is saved — exactly as the in-dialog door does it. One materialiser, not two.</p>
 *
 * <p>⚠️ <b>Empty on the day this ships, and that is correct.</b> The flag is not derivable, so nothing was
 * backfilled; the list fills as séances are charted. Do not repair it by inference.</p>
 */
export function UnfinishedActsList({
  onTotalChange,
}: {
  /**
   * The **server's** total after every read, so the tab around this list can put a figure on its own trigger —
   * and `null` when the read failed, which is « je ne sais pas » and never « aucune ».
   */
  onTotalChange?: (total: number | null) => void
}) {
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [data, setData] = useState<PagedResponse<UnfinishedActDto> | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  /**
   * The row whose « Planifier la suite » was pressed — the act `ContinueSessionDialog` opens on.
   *
   * ⚠️ **Two steps, not one, and the first version skipped the first.** What the remaining work is worth is a
   * decision only the dentist can make, and it decides everything the booking card then says: with no amount
   * the séance is named after the act already DONE, its price field is withheld (a devis-carried act holds no
   * fee on the séance) and the card states both « n'ajoute aucun honoraire » and « Encaissement sur la note ».
   * That question has one home and this is the door to it.
   */
  const [continuing, setContinuing] = useState<UnfinishedActDto | null>(null)

  /** The choice, once made — what the booking dialog opens on. */
  const [booking, setBooking] = useState<{ patientId: string; patientName: string | null; choice: ContinuationChoice } | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      const result = await treatmentPlansApi.unfinishedActs({ page, pageSize })

      // Booking the last act on page 2 leaves that page empty while the list still has rows: `PageRequest`
      // clamps the page *size* and deliberately does not clamp a page past the end. Rendering it would print
      // « Aucune suite à planifier » — a false statement — under a pager reading « 26–26 sur 26 ».
      if (result.items.length === 0 && result.totalCount > 0 && page > 1) {
        setPage(Math.min(page - 1, Math.max(1, result.totalPages)))
        return
      }

      setData(result)
      setError(null)
      onTotalChange?.(result.totalCount)
    } catch (err) {
      // § 13 — a failed read must NEVER render as an empty list. « Aucune suite à planifier » and « je n'ai pas
      // pu lire » are the same picture and opposite facts, and here the wrong one is actively reassuring.
      setError(getErrorMessage(err))
      setData(null)
      onTotalChange?.(null)
    } finally {
      setLoading(false)
    }
  }, [page, pageSize, onTotalChange])

  useEffect(() => {
    void load()
  }, [load])

  // Every key whose mutation can add a row or take one away: charting a fiche (patients), accepting the devis
  // that continues it (treatmentplans), and booking the séance that answers it (appointments).
  useClinicRealtime(
    [RealtimeResource.Patients, RealtimeResource.TreatmentPlans, RealtimeResource.Appointments],
    load,
  )

  const items = data?.items ?? []

  /** Open the continuation dialog on this act — it asks the price, then hands back the choice. */
  const planNext = (row: UnfinishedActDto) => setContinuing(row)

  if (error) {
    return (
      <LoadFailureNotice
        message={error}
        detail="Aucune séance n'a été modifiée."
        onRetry={() => void load()}
      />
    )
  }

  return (
    <>
      <div className="rounded-md border bg-card">
        {loading && items.length === 0 ? (
          <ListSkeleton />
        ) : items.length === 0 ? (
          <div className="p-4">
            <EmptyState
              size="compact"
              icon={CircleDashed}
              title="Aucune suite à planifier"
              description="Un acte coché « non terminé » sur une fiche de soins apparaît ici jusqu'à ce que sa suite soit planifiée."
            />
          </div>
        ) : (
          <>
            <div className={TABLE_ONLY_LG}>
              <Table>
                <TableHeader className="sticky top-0 z-10 bg-card">
                  <TableRow>
                    <TableHead>Patient</TableHead>
                    <TableHead>Acte</TableHead>
                    <TableHead className="whitespace-nowrap">Séance</TableHead>
                    <TableHead className="text-end">Honoraires</TableHead>
                    <TableHead>Encaissement</TableHead>
                    <TableHead className="text-end">Action</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {items.map((row) => (
                    <TableRow key={row.actId}>
                      <TableCell>
                        <PatientNameLink patientId={row.patientId} name={row.patientName ?? "Patient inconnu"} />
                        {row.nextAppointmentAt && (
                          <p className="mt-0.5 text-2xs text-muted-foreground">{bookedLine(row)}</p>
                        )}
                      </TableCell>
                      <TableCell>
                        <span className="font-medium">{row.procedureName}</span>
                        {row.toothNumbers.length > 0 && (
                          <span className="ms-1.5 font-mono text-2xs text-muted-foreground">
                            {row.toothNumbers.join(", ")}
                          </span>
                        )}
                      </TableCell>
                      <TableCell className="whitespace-nowrap">
                        <SeanceAge row={row} />
                      </TableCell>
                      <TableCell numeric>{formatDT(row.cost)}</TableCell>
                      <TableCell className="text-xs">
                        <MoneyLine row={row} />
                      </TableCell>
                      <TableCell className="text-end">
                        <Button size="sm" onClick={() => planNext(row)}>
                          <CalendarPlus className="h-4 w-4" aria-hidden="true" />
                          Planifier la suite
                        </Button>
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>

            <div className={cn(CARDS_ONLY_LG, "p-3")}>
              <CardList
                items={items}
                ariaLabel="Suites à planifier"
                getKey={(row) => row.actId}
                title={(row) => row.procedureName}
                underTitle={(row) => (
                  <div className="space-y-0.5">
                    <PatientNameLink patientId={row.patientId} name={row.patientName ?? "Patient inconnu"} />
                    {row.nextAppointmentAt && (
                      <p className="text-2xs text-muted-foreground">{bookedLine(row)}</p>
                    )}
                  </div>
                )}
                fields={(row) => [
                  { label: "Séance", value: <SeanceAge row={row} /> },
                  ...(row.toothNumbers.length > 0
                    ? [{ label: "Dents", value: <span className="font-mono">{row.toothNumbers.join(", ")}</span> }]
                    : []),
                  { label: "Honoraires", value: formatDT(row.cost) },
                  { label: "Encaissement", value: <MoneyLine row={row} /> },
                ]}
                /*
                  ⚠️ `primaryAction`, not `actions` — the sanctioned exception this list is exactly the case for:
                  planning the suite IS why the screen is open, and a `⋯` menu holding one item hides the only
                  verb behind a tap. Full-width below the fields, so it gets a real 44 px target without
                  crushing the act's name in the header row.
                */
                primaryAction={(row) => (
                  <Button size="sm" className="w-full" onClick={() => planNext(row)}>
                    <CalendarPlus className="h-4 w-4" aria-hidden="true" />
                    Planifier la suite
                  </Button>
                )}
              />
            </div>
          </>
        )}

        {data && data.totalCount > 0 && (
          <DataTablePagination
            page={data}
            loading={loading}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
            label={["suite à planifier", "suites à planifier"]}
          />
        )}
      </div>

      {/*
        Both dialogs are SIBLINGS of the list, never children of a row — Radix unmounts a closed dialog's
        content, and every row is re-rendered by the realtime refresh under it.
      */}

      {/*
        Step 1 — what is this séance, and what is the remaining work worth? `ContinueSessionDialog` is the one
        place that question is asked, and it writes nothing: it hands back a `ContinuationChoice`.
      */}
      {continuing && (
        <ContinueSessionDialog
          open
          onOpenChange={(next) => !next && setContinuing(null)}
          patientId={continuing.patientId}
          preselectActId={continuing.actId}
          onChosen={(choice) => {
            setBooking({
              patientId: continuing.patientId,
              patientName: continuing.patientName,
              choice,
            })
            setContinuing(null)
          }}
        />
      )}

      {/*
        Step 2 — the booking itself, with the continuation already chosen. The devis is still minted by
        `materialiseTreatments` when THIS is saved, so abandoning it writes nothing.

        ⚠️ `presetPatientId` travels with the continuation: the pending row is keyed on one patient's fiche, so
        a patient changed mid-dialog either drops it or mints the FIRST patient's devis and is then refused.
      */}
      {booking && (
        <CreateAppointmentDialog
          open
          onOpenChange={(next) => !next && setBooking(null)}
          presetPatientId={booking.patientId}
          presetPatientName={booking.patientName ?? undefined}
          presetContinuation={booking.choice}
          onSuccess={() => {
            setBooking(null)
            void load()
          }}
        />
      )}
    </>
  )
}

/**
 * « il y a 18 jours », amber past a fortnight.
 *
 * ⚠️ **A count phrased as a count, and the date beside it** — never a bare figure. N31: a lone number next to a
 * clinical row is read as a claim about the work rather than about the wait.
 */
function SeanceAge({ row }: { row: UnfinishedActDto }) {
  const days = Math.max(
    0,
    Math.floor((Date.now() - new Date(row.interventionDate).getTime()) / 86_400_000),
  )
  const stale = days >= 14
  return (
    <span className={cn("text-xs", stale && "font-medium text-warning-ink")}>
      {formatDateFr(row.interventionDate)}
      <span className="ms-1.5 text-2xs text-muted-foreground">
        {days === 0 ? "aujourd'hui" : days === 1 ? "il y a 1 jour" : `il y a ${days} jours`}
      </span>
    </span>
  )
}

/**
 * Which document collects this act, and what is still owed on it.
 *
 * ⚠️ **It states the money and moves none.** The two cases lead to opposite actions at the next séance and
 * neither is guessable from the act's name, which is the whole reason the row carries a sentence rather than a
 * figure. ⚠️ Branches on `invoiceId`, **never** on the number: a DRAFT note collects and has no number yet, and
 * reading the number puts a billed séance on the « non facturée » branch — the exact inversion the continuation
 * dialog already paid for.
 */
function MoneyLine({ row }: { row: UnfinishedActDto }) {
  if (!row.invoiceId) {
    return (
      <span className="text-muted-foreground">
        Non facturée — le devis portera {formatDT(row.cost)}.
      </span>
    )
  }
  return (
    <span className="text-muted-foreground">
      Facturée sur {row.invoiceNumber ? "la note " : "un brouillon de note"}
      {row.invoiceNumber && <span className="font-mono">{row.invoiceNumber}</span>}
      {row.invoiceOutstanding > 0 ? (
        <span className="font-medium text-warning-ink">
          {" "}
          · reste {formatDT(row.invoiceOutstanding)} sur cette note
        </span>
      ) : (
        <span> · entièrement réglée</span>
      )}
    </span>
  )
}

/**
 * ⚠️ « un rendez-vous est prévu », never « cet acte est planifié ». Nothing links a booking to an act with no
 * treatment behind it — which is exactly what these acts are — so the row may only say the patient is coming
 * back. Claiming the stronger of the two is how a worklist starts lying.
 */
function bookedLine(row: UnfinishedActDto): string {
  return `Un rendez-vous est déjà prévu le ${formatDateFr(row.nextAppointmentAt!)}`
}

/** Loading is its own state — a card list has no header row, so empty and loading are otherwise one blank box. */
function ListSkeleton() {
  return (
    <div className="space-y-2 p-3">
      {[0, 1, 2].map((i) => (
        <div key={i} className="h-14 animate-pulse rounded-md bg-muted" />
      ))}
    </div>
  )
}
