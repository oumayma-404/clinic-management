"use client"

import { useCallback, useMemo, useState } from "react"
import { BellRing } from "lucide-react"

import { Button } from "@/components/ui/button"
import type { WaitingRoomDto, WaitingRoomVisitDto } from "@/lib/api/types"
import { minutesBetween } from "@/lib/waiting-room"

/** How long a fresh call stays on the board's banner — reception has seen it by then, or the notice still rings. */
const FRESH_MINUTES = 5
const SEEN_KEY = "salle-attente:call-seen"

function readSeen(): Set<string> {
  try {
    const raw = window.localStorage.getItem(SEEN_KEY)
    return new Set(raw ? (JSON.parse(raw) as string[]) : [])
  } catch {
    return new Set()
  }
}

function writeSeen(ids: Set<string>) {
  try {
    // Only the last few matter: a call older than FRESH_MINUTES is never shown again anyway.
    window.localStorage.setItem(SEEN_KEY, JSON.stringify([...ids].slice(-30)))
  } catch {
    // Private window or blocked storage: the banner simply comes back on the next read.
  }
}

/**
 * Remember a call this browser made itself (« Faire entrer », a drag to « Au fauteuil »): the banner is for a call
 * somebody ELSE made, and announcing your own press back to you is noise.
 */
export function markCallSeen(appointmentId: string) {
  const seen = readSeen()
  seen.add(appointmentId)
  writeSeen(seen)
}

/**
 * « Dr Trabelsi : faites entrer Amira Ben Salah » across the top of the board (`salle-attente` S5) — the same call the
 * staff-chat notice rings, kept on the screen reception is looking at, for whoever is at the desk (an admin covering
 * reception receives no desk notice). Not for the dentist who called: he knows.
 *
 * <p>Round 2, E1: « Pas là » beside « OK » — the patient called is not in the salle d'attente. He keeps his place and
 * the next one of that chair is called instead, so the dentist is not left waiting for nobody.</p>
 */
export function CallInBanner({
  board,
  nowMs,
  onNotHere,
}: {
  board: WaitingRoomDto
  nowMs: number
  onNotHere: (visit: WaitingRoomVisitDto) => void
}) {
  const [seen, setSeen] = useState<Set<string>>(() => (typeof window === "undefined" ? new Set() : readSeen()))

  const calls = useMemo(
    () =>
      board.inChair.filter(
        (visit) =>
          visit.calledInAtUtc &&
          minutesBetween(visit.calledInAtUtc, nowMs) < FRESH_MINUTES &&
          visit.chairDoctorId !== board.callerDoctorId &&
          !seen.has(visit.appointmentId),
      ),
    [board.inChair, board.callerDoctorId, nowMs, seen],
  )

  const dismiss = useCallback((id: string) => {
    setSeen((current) => {
      const next = new Set(current)
      next.add(id)
      writeSeen(next)
      return next
    })
  }, [])

  if (calls.length === 0) return null

  return (
    <div className="grid gap-2" role="status">
      {calls.map((visit) => (
        <div
          key={visit.appointmentId}
          className="flex min-w-0 flex-wrap items-center gap-3 rounded-xl border border-primary/40 bg-primary/10 px-4 py-3"
        >
          <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-primary text-primary-foreground">
            <BellRing className="size-5" aria-hidden="true" />
          </span>
          {/* Not `flex-1`: the call-in notice and the « Annuler » toasts are pinned top-right, so the buttons sit
              right after the sentence, where neither covers them (round 2 QA, F1). */}
          <p className="min-w-0 text-sm">
            {visit.doctorName ? <span className="font-semibold">Dr {visit.doctorName} : </span> : null}
            faites entrer <span className="text-base font-bold">{visit.patientName}</span>
          </p>
          <Button
            size="sm"
            variant="outline"
            className="bg-card coarse:min-h-11"
            onClick={() => {
              dismiss(visit.appointmentId)
              onNotHere(visit)
            }}
            aria-label={`${visit.patientName} n'est pas là`}
          >
            Pas là
          </Button>
          <Button size="sm" className="min-w-16 coarse:min-h-11" onClick={() => dismiss(visit.appointmentId)}>
            OK
          </Button>
        </div>
      ))}
    </div>
  )
}
