import type { WaitingRoomLane, WaitingRoomVisitDto } from "@/lib/api/types"
import { formatClock, formatDuration, minutesOfDay } from "@/lib/dashboard/day-summary"

/**
 * The salle d'attente's small rules (`salle-attente`), in one place so the board, the doctor's panel and the header
 * pill cannot word or colour the same wait two ways.
 */

/** The four lanes of the board, in the order a patient walks through them. `Left` is shown under « Terminé ». */
export const BOARD_LANES = ["Upcoming", "Waiting", "InChair", "Done"] as const
export type BoardLane = (typeof BOARD_LANES)[number]

export const LANE_LABEL: Record<Exclude<WaitingRoomLane, "None">, string> = {
  Upcoming: "À venir",
  Waiting: "En salle d'attente",
  InChair: "Au fauteuil",
  Done: "Terminé",
  Left: "Parti sans être vu",
}

/** D4 — neutral under 20 minutes, amber from 20, red from 40. */
export const WAIT_AMBER_MINUTES = 20
export const WAIT_RED_MINUTES = 40

export type WaitTone = "calm" | "long" | "too-long"

export function waitTone(minutes: number): WaitTone {
  if (minutes >= WAIT_RED_MINUTES) return "too-long"
  if (minutes >= WAIT_AMBER_MINUTES) return "long"
  return "calm"
}

/** Whole minutes between two instants, never negative (a drifting clock must not print « −2 min »). */
export function minutesBetween(fromIso: string | null | undefined, nowMs: number): number {
  if (!fromIso) return 0
  const from = Date.parse(fromIso)
  if (Number.isNaN(from)) return 0
  return Math.max(0, Math.floor((nowMs - from) / 60_000))
}

/** « 8 min », « 1 h 05 » — the dashboard's own wording, so a wait reads the same on every screen. */
export const formatWait = formatDuration

/**
 * « 10:30 », read in the workstation's zone like every time the agenda prints (`day-summary.ts`' convention), so a
 * card here and the same RDV in the agenda can never show two different hours.
 */
export function boardTime(iso: string | null | undefined): string {
  if (!iso) return ""
  return formatClock(minutesOfDay(new Date(iso)))
}

/** The lane a card is drawn in: « Parti » lives under « Terminé ». */
export function boardLaneOf(visit: Pick<WaitingRoomVisitDto, "lane">): BoardLane {
  return visit.lane === "Left" ? "Done" : (visit.lane as BoardLane)
}

/**
 * May this card go to that lane? Every lane reaches every other one (D6 — « all actions reversible »), with the one
 * refusal the server also makes: a walk-in has no slot to go back to.
 */
export function canMoveTo(visit: Pick<WaitingRoomVisitDto, "lane" | "isWalkIn">, target: BoardLane): boolean {
  if (boardLaneOf(visit) === target) return false
  return !(target === "Upcoming" && visit.isWalkIn)
}

/** The toast after a move: « Amira Ben Salah → Au fauteuil ». */
export function moveSentence(name: string | null | undefined, lane: WaitingRoomLane | null | undefined): string {
  const who = name?.trim() || "Le patient"
  return lane && lane !== "None" ? `${who} → ${LANE_LABEL[lane]}` : who
}
