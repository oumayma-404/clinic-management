"use client"

import { useCallback, useEffect, useState } from "react"
import { useQuery } from "@tanstack/react-query"
import { toast } from "sonner"

import { ApiError } from "@/lib/api/client"
import type { WaitingRoomChangeDto, WaitingRoomDto } from "@/lib/api/types"
import { waitingRoomApi } from "@/lib/api/waiting-room"
import { showErrorToast } from "@/lib/errors"
import { queryKeys, realtimeMeta } from "@/lib/query/keys"
import { moveSentence } from "@/lib/waiting-room"

/** How long « Annuler » stays on screen after a board action (D6). */
export const UNDO_MS = 8_000

/** The codes that mean « somebody moved first »: refresh quietly and say so, never an error to fix. */
const STALE_CODES = new Set(["waiting-room-already-called", "waiting-room-already-here"])

interface BoardRead {
  board: WaitingRoomDto
  /** Server clock − this PC's clock, measured at the read. */
  offsetMs: number
}

async function readBoard(doctorId: string | null): Promise<BoardRead> {
  const sentAt = Date.now()
  const board = await waitingRoomApi.get(doctorId)
  const receivedAt = Date.now()
  const server = Date.parse(board.serverNowUtc)
  return { board, offsetMs: Number.isNaN(server) ? 0 : server - (sentAt + receivedAt) / 2 }
}

/**
 * Today's « Salle d'attente » board (`salle-attente`): one shared read, refreshed by every broadcast that can move a
 * lane, and a clock offset from the server's own `serverNowUtc` — a cabinet PC's clock drifts, and a wait counted
 * from it lies.
 *
 * <p>`loading` (no board yet), `failed` (the read failed and there is nothing to show) and a board with empty lanes
 * are three different states and the page renders three different things for them (`frontend-web.md` § 13).</p>
 */
export function useWaitingRoom(doctorId: string | null = null, options: { enabled?: boolean } = {}) {
  const query = useQuery({
    queryKey: queryKeys.waitingRoom(doctorId),
    queryFn: () => readBoard(doctorId),
    enabled: options.enabled ?? true,
    // The wait timers tick on their own; a quiet minute is still re-read in case a broadcast was missed.
    staleTime: 60_000,
    refetchInterval: 120_000,
    meta: realtimeMeta.waitingRoom,
  })

  // The wait timers move every 30 s; the board itself is re-read only on a broadcast.
  const [tick, setTick] = useState(() => Date.now())
  useEffect(() => {
    const id = setInterval(() => setTick(Date.now()), 30_000)
    return () => clearInterval(id)
  }, [])

  const { refetch } = query
  const reload = useCallback(async () => {
    await refetch()
  }, [refetch])

  return {
    board: query.data?.board ?? null,
    loading: query.isPending && (options.enabled ?? true),
    failed: query.isError && !query.data,
    reload,
    nowMs: tick + (query.data?.offsetMs ?? 0),
  }
}

/**
 * Run one board action: the call, the « Patient → Lane » toast with its « Annuler », and a quiet refresh when somebody
 * else moved first. Every reception action and « Patient suivant » goes through here, so they all undo the same way.
 */
export async function runBoardAction(
  action: () => Promise<WaitingRoomChangeDto>,
  reload: () => Promise<void> | void,
  options: { message?: (change: WaitingRoomChangeDto) => string } = {},
): Promise<WaitingRoomChangeDto | null> {
  try {
    const change = await action()
    await reload()
    if (!change.changed) return change
    const text = options.message?.(change) ?? moveSentence(change.patientName, change.lane)
    toast.success(text, {
      duration: UNDO_MS,
      action: {
        label: "Annuler",
        onClick: () => {
          void waitingRoomApi
            .restore(change)
            .then(() => reload())
            .then(() => toast.info("Action annulée"))
            .catch((err) => showErrorToast(err, "L'action n'a pas pu être annulée."))
        },
      },
    })
    return change
  } catch (err) {
    if (err instanceof ApiError && err.code && STALE_CODES.has(err.code)) {
      toast.info(err.message)
      await reload()
      return null
    }
    showErrorToast(err, "L'action n'a pas pu être enregistrée.")
    await reload()
    return null
  }
}
