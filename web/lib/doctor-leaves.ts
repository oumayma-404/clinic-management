// A practitioner's congé, read in French. Days are `YYYY-MM-DD` clinic-local keys and are compared as strings.
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import type { DoctorLeaveDto } from "@/lib/api/doctors"

/** The server's own ceiling (`DoctorLeave.MaxDays`) — checked here only to name the field before the round trip. */
export const MAX_LEAVE_DAYS = 366
export const MAX_LEAVE_NOTE_LENGTH = 200

/** Local midnight of a day key — never `new Date("YYYY-MM-DD")`, which is UTC midnight and the day before in Tunis. */
export function dayFromKey(key: string): Date {
  return new Date(`${key}T00:00:00`)
}

export function leaveCoversDay(leave: Pick<DoctorLeaveDto, "startDate" | "endDate">, dayKey: string): boolean {
  return leave.startDate <= dayKey && dayKey <= leave.endDate
}

/** « du 12 août au 20 août 2026 », « le 12 août 2026 ». */
export function leavePeriodLabel(leave: Pick<DoctorLeaveDto, "startDate" | "endDate">): string {
  const start = dayFromKey(leave.startDate)
  const end = dayFromKey(leave.endDate)
  if (leave.startDate === leave.endDate) return `le ${format(start, "d MMMM yyyy", { locale: fr })}`
  const sameYear = start.getFullYear() === end.getFullYear()
  return `du ${format(start, sameYear ? "d MMMM" : "d MMMM yyyy", { locale: fr })} au ${format(end, "d MMMM yyyy", { locale: fr })}`
}

export function dayCountLabel(count: number): string {
  return count <= 1 ? "1 jour" : `${count} jours`
}

/** Inclusive length of a typed range, or null while either end is missing or unreadable. */
export function rangeDayCount(startKey: string, endKey: string): number | null {
  if (!startKey || !endKey) return null
  const start = dayFromKey(startKey).getTime()
  const end = dayFromKey(endKey).getTime()
  if (Number.isNaN(start) || Number.isNaN(end)) return null
  return Math.round((end - start) / 86_400_000) + 1
}

/** The French refusal for a typed range, or null — the server's `DoctorLeave.RefusalFor`, said on the field. */
export function leaveRangeRefusal(startKey: string, endKey: string): string | null {
  if (!startKey) return "Indiquez le premier jour du congé."
  if (!endKey) return "Indiquez le dernier jour du congé."
  const days = rangeDayCount(startKey, endKey)
  if (days === null) return "Date invalide."
  if (days < 1) return "Le dernier jour doit être le même jour ou après le premier."
  if (days > MAX_LEAVE_DAYS) return `Un congé ne peut pas dépasser ${MAX_LEAVE_DAYS} jours — vérifiez l'année.`
  return null
}
