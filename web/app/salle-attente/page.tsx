"use client"

import { useCallback, useMemo, useState } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"

import { AppShell } from "@/components/app-shell"
import { ClinicGuard } from "@/components/clinic-guard"
import { ArrivalSearch } from "@/components/waiting-room/arrival-search"
import { CallInBanner, markCallSeen } from "@/components/waiting-room/call-in-banner"
import { DoctorNextPanel } from "@/components/waiting-room/doctor-next-panel"
import { WaitingRoomBoard, type MoveTarget } from "@/components/waiting-room/waiting-room-board"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import { PageHeader } from "@/components/ui/page-header"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Stat, StatStrip } from "@/components/ui/stat-strip"
import type { WaitingRoomVisitDto } from "@/lib/api/types"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { runBoardAction, useWaitingRoom } from "@/lib/hooks/use-waiting-room"
import { formatWait, minutesBetween, type BoardLane } from "@/lib/waiting-room"

const ALL = "all"

/**
 * « Salle d'attente » (`salle-attente`): reception's board and the dentist's « Patient suivant », on one screen
 * because they are one queue seen from two ends. Everything on it is reversible — every move answers with an
 * « Annuler » — so nothing here asks « êtes-vous sûr ? ».
 */
export default function SalleAttentePage() {
  const [dentistFilter, setDentistFilter] = useState<string>(ALL)
  const doctorId = dentistFilter === ALL ? null : dentistFilter
  const { board, loading, failed, reload, nowMs } = useWaitingRoom(doctorId)
  const [busyId, setBusyId] = useState<string | null>(null)

  const lanes = useMemo(
    () =>
      board
        ? ({ Upcoming: board.upcoming, Waiting: board.waiting, InChair: board.inChair, Done: board.done } satisfies Record<
            BoardLane,
            WaitingRoomVisitDto[]
          >)
        : null,
    [board],
  )

  const onMove = useCallback(
    async (visit: WaitingRoomVisitDto, target: MoveTarget) => {
      if (busyId) return
      setBusyId(visit.appointmentId)
      if (target === "InChair") markCallSeen(visit.appointmentId)
      await runBoardAction(() => waitingRoomApi.move(visit.appointmentId, target), reload)
      setBusyId(null)
    },
    [busyId, reload],
  )

  const stats = useMemo(() => {
    if (!board) return null
    const waits = board.waiting.map((visit) => minutesBetween(visit.arrivedAtUtc, nowMs))
    const average = waits.length ? Math.round(waits.reduce((sum, w) => sum + w, 0) / waits.length) : null
    const seen = board.done.filter((visit) => visit.lane === "Done").length
    return { waiting: board.waiting.length, average, seen, upcoming: board.upcoming.length }
  }, [board, nowMs])

  const today = format(new Date(nowMs), "EEEE d MMMM", { locale: fr })
  const groupPractice = (board?.dentists.length ?? 0) > 1

  return (
    <ClinicGuard>
      <AppShell>
        <PageHeader
          title="Salle d'attente"
          subtitle={today.charAt(0).toUpperCase() + today.slice(1)}
          actions={
            groupPractice && board ? (
              <Select value={dentistFilter} onValueChange={setDentistFilter}>
                <SelectTrigger className="w-full sm:w-56" aria-label="Praticien">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value={ALL}>Tous les praticiens</SelectItem>
                  {board.dentists.map((dentist) => (
                    <SelectItem key={dentist.doctorId} value={dentist.doctorId}>
                      Dr {dentist.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            ) : undefined
          }
        />

        {failed && !board ? (
          <LoadFailureNotice
            message="La salle d'attente n'a pas pu être chargée."
            detail="Vérifiez votre connexion."
            onRetry={() => void reload()}
          />
        ) : loading || !board || !lanes || !stats ? (
          <div className="grid gap-4" aria-busy="true" aria-label="Chargement de la salle d'attente">
            <div className="h-20 w-full rounded-xl bg-muted motion-safe:animate-pulse" />
            <div className="h-14 w-full rounded-xl bg-muted motion-safe:animate-pulse" />
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              {Array.from({ length: 4 }, (_, i) => (
                <div key={i} className="h-48 rounded-xl bg-muted motion-safe:animate-pulse" />
              ))}
            </div>
          </div>
        ) : (
          <div className="grid gap-4">
            <StatStrip>
              <Stat label="En attente" value={String(stats.waiting)} tone={stats.waiting > 0 ? "pending" : "neutral"} />
              <Stat
                label="Attente moyenne"
                value={stats.average === null ? "—" : formatWait(stats.average)}
                tone={stats.average !== null && stats.average >= 20 ? "negative" : "neutral"}
              />
              <Stat label="Vus aujourd'hui" value={String(stats.seen)} tone="positive" />
              <Stat label="À venir" value={String(stats.upcoming)} />
            </StatStrip>

            <CallInBanner board={board} nowMs={nowMs} />

            {board.canCallNext && <DoctorNextPanel board={board} nowMs={nowMs} reload={reload} />}

            <ArrivalSearch board={board} reload={reload} />

            <WaitingRoomBoard
              visits={lanes}
              nowMs={nowMs}
              showDentist={groupPractice && !doctorId}
              busyId={busyId}
              onMove={(visit, target) => void onMove(visit, target)}
            />
          </div>
        )}
      </AppShell>
    </ClinicGuard>
  )
}
