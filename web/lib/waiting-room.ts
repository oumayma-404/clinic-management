import type {
  AppointmentWaitingRoomDto,
  WaitingRoomDentistDto,
  WaitingRoomDoctorUnavailable,
  WaitingRoomDto,
  WaitingRoomLane,
  WaitingRoomVisitDto,
} from "@/lib/api/types"
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

/**
 * Round 2, E5 — the minutes a wait is COLOURED by. A booked patient who came 25 minutes early by choice has not been
 * kept waiting by the cabinet: his colour stays neutral until his RDV time, while the pill still shows the real time
 * since he arrived. A walk-in has no RDV time, so his wait counts from the door.
 */
export function waitToneMinutes(
  visit: Pick<WaitingRoomVisitDto, "arrivedAtUtc" | "appointmentDateTime" | "isWalkIn">,
  nowMs: number,
): number {
  if (visit.isWalkIn || !visit.arrivedAtUtc) return minutesBetween(visit.arrivedAtUtc, nowMs)
  const arrived = Date.parse(visit.arrivedAtUtc)
  const slot = Date.parse(visit.appointmentDateTime)
  const from = Number.isNaN(slot) || arrived >= slot ? visit.arrivedAtUtc : visit.appointmentDateTime
  return minutesBetween(from, nowMs)
}

/** Round 2, C10 — what a card says when its dentist cannot see him today. */
export const DOCTOR_UNAVAILABLE_LABEL: Record<WaitingRoomDoctorUnavailable, (name: string) => string> = {
  OnLeave: (name) => `Dr ${name} absent aujourd'hui`,
  NotInTeam: (name) => `Dr ${name} ne fait plus partie du cabinet`,
}

/** The dentists « Quel praticien ? » may offer today: the cabinet's, minus those on congé. */
export function availableDentists(board: Pick<WaitingRoomDto, "dentists">): WaitingRoomDentistDto[] {
  return board.dentists.filter((dentist) => !dentist.absentToday)
}

/**
 * Round 2, C7 — does seating this visit need « Quel praticien ? » first? Only in a cabinet de groupe, and only for a
 * visit that names no dentist: the server refuses to seat him in nobody's chair.
 */
export function needsDentistToSeat(
  board: Pick<WaitingRoomDto, "dentists">,
  visit: Pick<WaitingRoomVisitDto, "doctorId">,
): boolean {
  return board.dentists.length > 1 && !visit.doctorId
}

/** Digits only, so « 22 345 678 », « +216 22345678 » and « 22-345-678 » all match « 22345 ». */
export function phoneDigits(value: string | null | undefined): string {
  return (value ?? "").replace(/\D/g, "")
}

/**
 * Round 2, E3 — the agenda's word for a visit on today's board: « en salle », « au fauteuil », or nothing. The
 * agenda is where much of reception works, and it used to say nothing about who is actually here.
 */
export function boardPresenceWord(board: AppointmentWaitingRoomDto | null | undefined): string | null {
  if (board?.lane === "Waiting") return "en salle"
  if (board?.lane === "InChair") return "au fauteuil"
  return null
}

/**
 * Round 2, E4 — how many minutes an agenda block spans. A patient still « au fauteuil » past his booking grows the
 * block to now, so the desk sees the delay before the next patient asks. Every other visit spans its booking (or,
 * once finished on the board, its real chair time — the server has already moved the RDV there).
 */
export function liveBlockMinutes(
  appointment: { appointmentDateTime: string; waitingRoom?: AppointmentWaitingRoomDto | null },
  bookedMinutes: number,
  nowMs: number,
): number {
  if (appointment.waitingRoom?.lane !== "InChair") return bookedMinutes
  const start = Date.parse(appointment.appointmentDateTime)
  if (Number.isNaN(start)) return bookedMinutes
  return Math.max(bookedMinutes, Math.ceil((nowMs - start) / 60_000))
}

/**
 * The toast after « Pas là » (round 2, E1) — one sentence for the board's banner, its card menu, the dentist's panel
 * and the staff-chat call-in notice, so the desk reads the same thing wherever it pressed.
 */
export function notHereSentence(
  absentName: string | null | undefined,
  change: Pick<WaitingRoomVisitDto, "lane"> & { patientName?: string | null },
): string {
  const who = absentName?.trim() || "Le patient"
  return change.lane === "InChair" && change.patientName
    ? `${who} n'est pas là · ${change.patientName} est appelé·e`
    : `${who} n'est pas là · il garde sa place`
}
