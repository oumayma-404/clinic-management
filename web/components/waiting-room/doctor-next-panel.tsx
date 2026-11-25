"use client"

import { useMemo, useState } from "react"
import { Armchair, BellRing, Check, Coffee, Loader2 } from "lucide-react"

import { PatientNameLink } from "@/components/patient-name-link"
import { WaitPill } from "@/components/waiting-room/waiting-room-board"
import { Button } from "@/components/ui/button"
import type { WaitingRoomDto, WaitingRoomVisitDto } from "@/lib/api/types"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { runBoardAction } from "@/lib/hooks/use-waiting-room"
import { cn } from "@/lib/utils"
import { boardTime, formatWait, minutesBetween } from "@/lib/waiting-room"

/**
 * The dentist's half of the salle d'attente (`salle-attente` S5): who is in my chair, who waits for me, and ONE
 * button — « Patient suivant » — that ends the visit in the chair, calls the first of the queue and rings reception.
 *
 * <p>The queue is the server's order, never re-sorted here: a visit with no dentist joins every dentist's queue in a
 * cabinet de groupe, and the first to call him takes him (the server refuses the second press by name).</p>
 */

export function myQueue(board: WaitingRoomDto, dentistId: string | null | undefined): WaitingRoomVisitDto[] {
  if (!dentistId) return []
  return board.waiting.filter((visit) => !visit.chairDoctorId || visit.chairDoctorId === dentistId)
}

export function myChair(board: WaitingRoomDto, dentistId: string | null | undefined): WaitingRoomVisitDto | null {
  if (!dentistId) return null
  return board.inChair.find((visit) => visit.chairDoctorId === dentistId) ?? null
}

interface DoctorNextPanelProps {
  board: WaitingRoomDto
  nowMs: number
  reload: () => Promise<void>
}

export function DoctorNextPanel({ board, nowMs, reload }: DoctorNextPanelProps) {
  const me = board.callerDoctorId
  const queue = useMemo(() => myQueue(board, me), [board, me])
  const chair = useMemo(() => myChair(board, me), [board, me])
  const next = queue[0] ?? null
  const [busy, setBusy] = useState<string | null>(null)

  const call = async (target: WaitingRoomVisitDto | null) => {
    if (busy) return
    setBusy(target?.appointmentId ?? "next")
    await runBoardAction(
      () =>
        target && target !== next
          ? waitingRoomApi.callNext({ appointmentId: target.appointmentId })
          : waitingRoomApi.callNext({ expectedNextId: next?.appointmentId ?? null }),
      reload,
      { message: (change) => `${change.patientName ?? "Le patient"} est appelé — la réception est prévenue` },
    )
    setBusy(null)
  }

  const endChair = async () => {
    if (!chair || busy) return
    setBusy(chair.appointmentId)
    await runBoardAction(() => waitingRoomApi.move(chair.appointmentId, "Done"), reload)
    setBusy(null)
  }

  return (
    <section
      aria-label="Mon fauteuil"
      className="grid gap-4 rounded-xl border bg-card p-4 shadow-sm lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]"
    >
      <div className="grid content-start gap-3">
        <div className="flex min-w-0 items-center gap-3 rounded-lg border bg-success-wash p-3">
          <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-success text-white">
            <Armchair className="size-5" aria-hidden="true" />
          </span>
          <div className="grid min-w-0 flex-1">
            <span className="text-xs font-semibold text-muted-foreground">Au fauteuil</span>
            {chair ? (
              <>
                <PatientNameLink patientId={chair.patientId} name={chair.patientName} className="text-base font-semibold" />
                <span className="text-xs text-muted-foreground">
                  depuis {formatWait(minutesBetween(chair.calledInAtUtc, nowMs))}
                </span>
              </>
            ) : (
              <span className="text-sm font-semibold">Fauteuil libre</span>
            )}
          </div>
        </div>

        {next ? (
          <Button
            type="button"
            className="h-auto min-h-16 w-full min-w-0 justify-between gap-3 whitespace-normal px-4 py-3 text-start"
            disabled={busy !== null}
            onClick={() => void call(null)}
            aria-label={`Patient suivant : faire entrer ${next.patientName}`}
          >
            <span className="grid min-w-0">
              <span className="flex items-center gap-2 text-lg font-bold">
                {busy === "next" ? (
                  <Loader2 className="size-5 motion-safe:animate-spin" aria-hidden="true" />
                ) : (
                  <BellRing className="size-5" aria-hidden="true" />
                )}
                Patient suivant
              </span>
              <span className="truncate text-sm font-normal opacity-90">
                {next.patientName} · attend depuis {formatWait(minutesBetween(next.arrivedAtUtc, nowMs))}
              </span>
            </span>
          </Button>
        ) : chair ? (
          <Button
            type="button"
            variant="outline"
            className="min-h-14 w-full gap-2 text-base font-semibold"
            disabled={busy !== null}
            onClick={() => void endChair()}
          >
            {busy === chair.appointmentId ? (
              <Loader2 className="size-5 motion-safe:animate-spin" aria-hidden="true" />
            ) : (
              <Check className="size-5" aria-hidden="true" />
            )}
            Terminer la séance
          </Button>
        ) : (
          <Button type="button" variant="outline" className="min-h-14 w-full gap-2 text-base" disabled>
            <Coffee className="size-5" aria-hidden="true" />
            Personne n&apos;attend
          </Button>
        )}
      </div>

      <div className="grid content-start gap-2">
        <h2 className="flex items-center gap-2 text-sm font-semibold">
          En salle d&apos;attente pour moi
          <span className="rounded-full border bg-muted px-2 text-xs tabular-nums text-muted-foreground">{queue.length}</span>
        </h2>
        {queue.length === 0 ? (
          <p className="rounded-lg border border-dashed px-3 py-4 text-center text-sm text-muted-foreground" role="status">
            Personne n&apos;attend
          </p>
        ) : (
          <ol className="grid gap-1.5">
            {queue.map((visit, index) => (
              <li
                key={visit.appointmentId}
                className={cn(
                  "flex min-w-0 flex-wrap items-center gap-2 rounded-lg border px-3 py-2",
                  index === 0 && "border-primary/40 bg-primary/5",
                )}
              >
                <span
                  className={cn(
                    "flex size-6 shrink-0 items-center justify-center rounded text-xs font-bold tabular-nums",
                    index === 0 ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground",
                  )}
                  aria-hidden="true"
                >
                  {index + 1}
                </span>
                <span className="grid min-w-0 flex-1">
                  <PatientNameLink patientId={visit.patientId} name={visit.patientName} className="text-sm" />
                  <span className="text-xs text-muted-foreground">
                    {visit.isWalkIn ? "Sans RDV" : `RDV ${boardTime(visit.appointmentDateTime)}`}
                    {visit.acts ? ` · ${visit.acts}` : ""}
                  </span>
                </span>
                <WaitPill minutes={minutesBetween(visit.arrivedAtUtc, nowMs)} />
                {index > 0 && (
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    className="coarse:min-h-11"
                    disabled={busy !== null}
                    onClick={() => void call(visit)}
                    aria-label={`Appeler ${visit.patientName} avant les autres`}
                  >
                    {busy === visit.appointmentId && <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />}
                    Appeler
                  </Button>
                )}
              </li>
            ))}
          </ol>
        )}
      </div>
    </section>
  )
}
