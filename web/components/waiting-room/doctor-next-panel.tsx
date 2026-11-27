"use client"

import { useId, useMemo, useState } from "react"
import { Armchair, BellRing, Check, Coffee, Loader2, UserX } from "lucide-react"
import { toast } from "sonner"

import { PatientNameLink } from "@/components/patient-name-link"
import { WaitPill } from "@/components/waiting-room/waiting-room-board"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Switch } from "@/components/ui/switch"
import type { WaitingRoomDto, WaitingRoomVisitDto } from "@/lib/api/types"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { showErrorToast } from "@/lib/errors"
import { runBoardAction } from "@/lib/hooks/use-waiting-room"
import { cn } from "@/lib/utils"
import { boardTime, formatWait, minutesBetween, waitToneMinutes } from "@/lib/waiting-room"

/**
 * The dentist's half of the salle d'attente (`salle-attente` S5): who is in my chair, who waits for me, and ONE
 * button — « Patient suivant » — that ends the visit in the chair, calls the first of the queue and rings reception.
 *
 * <p>The queue is the server's order, never re-sorted here: a visit with no dentist joins every dentist's queue in a
 * cabinet de groupe, and the first to call him takes him (the server refuses the second press by name).</p>
 *
 * <p>Round 2: <b>« 2 fauteuils »</b> (Q3) — a dentist who leaves a patient in one chair while the anaesthesia works
 * sets it here, and then « Patient suivant » ends nobody: each seated patient has his own « Terminer ».
 * <b>« Pas là »</b> (E1) sits on a patient just called, for when nobody walks in.</p>
 */

/** How long after a call « Pas là » is offered on the dentist's own chair — past that he plainly came in. */
const NOT_HERE_MINUTES = 10

export function myQueue(board: WaitingRoomDto, dentistId: string | null | undefined): WaitingRoomVisitDto[] {
  if (!dentistId) return []
  return board.waiting.filter((visit) => !visit.chairDoctorId || visit.chairDoctorId === dentistId)
}

/** Everyone in this dentist's chairs — one, or two with « 2 fauteuils ». */
export function myChairs(board: WaitingRoomDto, dentistId: string | null | undefined): WaitingRoomVisitDto[] {
  if (!dentistId) return []
  return board.inChair.filter((visit) => visit.chairDoctorId === dentistId)
}

export function myChair(board: WaitingRoomDto, dentistId: string | null | undefined): WaitingRoomVisitDto | null {
  return myChairs(board, dentistId)[0] ?? null
}

/** « 2 fauteuils » and both taken: « Patient suivant » would be refused, so it is not offered. */
export function chairsFull(board: WaitingRoomDto, dentistId: string | null | undefined): boolean {
  const twoChairs = board.dentists.find((dentist) => dentist.doctorId === dentistId)?.worksTwoChairs ?? false
  return twoChairs && myChairs(board, dentistId).length >= 2
}

interface DoctorNextPanelProps {
  board: WaitingRoomDto
  nowMs: number
  reload: () => Promise<void>
  onNotHere: (visit: WaitingRoomVisitDto) => void
}

export function DoctorNextPanel({ board, nowMs, reload, onNotHere }: DoctorNextPanelProps) {
  const me = board.callerDoctorId
  const switchId = useId()
  const queue = useMemo(() => myQueue(board, me), [board, me])
  const chairs = useMemo(() => myChairs(board, me), [board, me])
  const twoChairs = board.dentists.find((dentist) => dentist.doctorId === me)?.worksTwoChairs ?? false
  const full = twoChairs && chairs.length >= 2
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

  const endChair = async (visit: WaitingRoomVisitDto) => {
    if (busy) return
    setBusy(visit.appointmentId)
    await runBoardAction(() => waitingRoomApi.move(visit.appointmentId, "Done"), reload)
    setBusy(null)
  }

  const setChairs = async (value: boolean) => {
    if (busy) return
    setBusy("chairs")
    try {
      await waitingRoomApi.setTwoChairs(value)
      await reload()
      toast.success(value ? "2 fauteuils : « Patient suivant » ne termine plus personne" : "1 fauteuil")
    } catch (err) {
      showErrorToast(err, "Le réglage n'a pas pu être enregistré.")
    } finally {
      setBusy(null)
    }
  }

  return (
    <section
      aria-label="Mon fauteuil"
      className="grid gap-4 rounded-xl border bg-card p-4 shadow-sm lg:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]"
    >
      <div className="grid content-start gap-3">
        <div className="grid gap-2 rounded-lg border bg-success-wash p-3">
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            <span className="flex size-9 shrink-0 items-center justify-center rounded-md bg-success text-white">
              <Armchair className="size-5" aria-hidden="true" />
            </span>
            <span className="min-w-0 flex-1 text-xs font-semibold text-muted-foreground">
              {twoChairs ? "Mes 2 fauteuils" : "Au fauteuil"}
            </span>
            <span className="flex items-center gap-2">
              <Switch
                id={switchId}
                checked={twoChairs}
                disabled={busy !== null}
                onCheckedChange={(value) => void setChairs(value)}
              />
              <Label htmlFor={switchId} className="text-xs font-medium">
                2 fauteuils
              </Label>
            </span>
          </div>
          {chairs.length === 0 ? (
            <span className="text-sm font-semibold">Fauteuil libre</span>
          ) : (
            <ul className="grid gap-2">
              {chairs.map((visit) => {
                const since = minutesBetween(visit.calledInAtUtc, nowMs)
                return (
                  <li key={visit.appointmentId} className="flex min-w-0 flex-wrap items-center gap-2">
                    <span className="grid min-w-0 flex-1">
                      <PatientNameLink patientId={visit.patientId} name={visit.patientName} className="text-base font-semibold" />
                      <span className="text-xs text-muted-foreground">depuis {formatWait(since)}</span>
                    </span>
                    {since < NOT_HERE_MINUTES && (
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        className="gap-1 coarse:min-h-11"
                        disabled={busy !== null}
                        onClick={() => onNotHere(visit)}
                        aria-label={`${visit.patientName} n'est pas là`}
                      >
                        <UserX className="size-4" aria-hidden="true" />
                        Pas là
                      </Button>
                    )}
                    {twoChairs && (
                      <Button
                        type="button"
                        size="sm"
                        variant="outline"
                        className="gap-1 bg-card coarse:min-h-11"
                        disabled={busy !== null}
                        onClick={() => void endChair(visit)}
                        aria-label={`Terminer la séance de ${visit.patientName}`}
                      >
                        {busy === visit.appointmentId ? (
                          <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />
                        ) : (
                          <Check className="size-4" aria-hidden="true" />
                        )}
                        Terminer
                      </Button>
                    )}
                  </li>
                )
              })}
            </ul>
          )}
        </div>

        {next && !full ? (
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
        ) : next && full ? (
          <Button type="button" variant="outline" className="min-h-14 w-full gap-2 whitespace-normal text-base" disabled>
            <Armchair className="size-5" aria-hidden="true" />
            Les deux fauteuils sont occupés
          </Button>
        ) : chairs.length > 0 && !twoChairs ? (
          <Button
            type="button"
            variant="outline"
            className="min-h-14 w-full gap-2 text-base font-semibold"
            disabled={busy !== null}
            onClick={() => void endChair(chairs[0])}
          >
            {busy === chairs[0].appointmentId ? (
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
                    {visit.urgentAtUtc ? "Urgent · " : ""}
                    {visit.isWalkIn ? "Sans RDV" : `RDV ${boardTime(visit.appointmentDateTime)}`}
                    {visit.callSkippedAtUtc ? ` · pas là à ${boardTime(visit.callSkippedAtUtc)}` : ""}
                    {visit.acts ? ` · ${visit.acts}` : ""}
                  </span>
                </span>
                <WaitPill
                  minutes={minutesBetween(visit.arrivedAtUtc, nowMs)}
                  toneMinutes={waitToneMinutes(visit, nowMs)}
                />
                {index > 0 && !full && (
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
