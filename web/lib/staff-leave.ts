// A secretary's congé, read in French. Days are `YYYY-MM-DD` clinic-local keys, compared as strings.
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import type { StatusTone } from "@/components/ui/status-tone"
import type { StaffLeaveStatus, StaffLeaveType } from "@/lib/api/team"
import { dayFromKey, leavePeriodLabel, leaveRangeRefusal } from "@/lib/doctor-leaves"

/** The server's own ceiling (`StaffLeaveRequest.MaxReasonLength`), said on the field before the round trip. */
export const MAX_STAFF_LEAVE_REASON = 500

/** The `/users` tab the bell and the request links open (`?onglet=conges`). */
export const USERS_LEAVE_TAB = "conges"
/** Raised by the bell when it sends an admin to the congés while `/users` is already on screen. */
export const USERS_LEAVE_TAB_EVENT = "clinic:users-leave-tab"

export const STAFF_LEAVE_TYPES: ReadonlyArray<{ value: StaffLeaveType; label: string }> = [
  { value: "PaidLeave", label: "Congé payé" },
  { value: "Sick", label: "Maladie" },
  { value: "Unpaid", label: "Sans solde" },
  { value: "Other", label: "Autre" },
]

export const STAFF_LEAVE_STATUS_TONE: Record<StaffLeaveStatus, StatusTone> = {
  Pending: "active",
  Approved: "positive",
  Rejected: "negative",
  Cancelled: "neutral",
}

const dayFormatter = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 1 })

/** « 19,5 », « 3 » — a day count without its unit, for a figure set large. */
export function formatStaffDays(days: number): string {
  return dayFormatter.format(days)
}

/** « 3 jours », « 1 jour », « 0,5 jour », « 1,5 jour ». */
export function staffDaysLabel(days: number): string {
  return `${dayFormatter.format(days)} ${days > 1 ? "jours" : "jour"}`
}

/** « 19,5 j » — a day count where the column already says « jours ». */
export function staffDaysShort(days: number): string {
  return `${dayFormatter.format(days)} j`
}

/** The server's ceiling for a day count (`StaffLeaveRequest.MaxDays`), said on the field before the round trip. */
export const MAX_STAFF_DAYS = 366

/** « 3 », « 2,5 » → a count in half days between 0 and {@link MAX_STAFF_DAYS}; null for anything else. */
export function parseStaffDays(text: string): number | null {
  const trimmed = text.trim()
  const value = Number(trimmed.replace(",", "."))
  if (!trimmed || !Number.isFinite(value) || value < 0 || value > MAX_STAFF_DAYS || value * 2 !== Math.trunc(value * 2)) {
    return null
  }
  return value
}

/** « Solde après », or « Solde 2027 après » when the congé starts in another year than the one on screen. */
export function balanceAfterLabel(startDate: string, thisYear = new Date().getFullYear()): string {
  const year = Number(startDate.slice(0, 4))
  return year === thisYear ? "Solde après" : `Solde ${year} après`
}

type LeaveDates = { startDate: string; endDate: string; halfDay?: boolean }

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

/**
 * The period as a row title: « Lundi 21 décembre », « 16 → 18 novembre », « 30 déc. → 2 janv. 2027 ». The year is
 * named only when it is not this one, or when the congé crosses it.
 */
export function staffLeaveShortPeriod(leave: LeaveDates, thisYear = new Date().getFullYear()): string {
  const start = dayFromKey(leave.startDate)
  const end = dayFromKey(leave.endDate)
  const half = leave.halfDay ? " · demi-journée" : ""
  if (leave.startDate === leave.endDate) {
    const year = start.getFullYear() === thisYear ? "" : " yyyy"
    return capitalize(format(start, `EEEE d MMMM${year}`, { locale: fr })) + half
  }
  if (start.getFullYear() !== end.getFullYear()) {
    return `${format(start, "d MMM", { locale: fr })} → ${format(end, "d MMM yyyy", { locale: fr })}`
  }
  const year = end.getFullYear() === thisYear ? "" : " yyyy"
  if (start.getMonth() === end.getMonth()) {
    return `${format(start, "d", { locale: fr })} → ${format(end, `d MMMM${year}`, { locale: fr })}`
  }
  return `${format(start, "d MMM", { locale: fr })} → ${format(end, `d MMM${year}`, { locale: fr })}`
}

/** The date tile beside a request: « NOV. » over « 16 ». */
export function staffLeaveDateTile(leave: LeaveDates): { month: string; day: string } {
  const start = dayFromKey(leave.startDate)
  return { month: format(start, "MMM", { locale: fr }).toUpperCase(), day: format(start, "d") }
}

/** Waiting, or accepted and not over: what a secretary still has ahead of her. */
export function isUpcomingLeave(leave: { status: StaffLeaveStatus; endDate: string }, todayKey: string): boolean {
  return leave.status === "Pending" || (leave.status === "Approved" && leave.endDate >= todayKey)
}

/** « 3 jours », or « 2,5 jours décomptés (3 jours demandés) » when the admin counted differently. */
export function staffLeaveDaysText(leave: { requestedDays: number; countedDays: number | null }): string {
  const counted = leave.countedDays
  return counted !== null && counted !== leave.requestedDays
    ? `${staffDaysLabel(counted)} décomptés (${staffDaysLabel(leave.requestedDays)} demandés)`
    : staffDaysLabel(counted ?? leave.requestedDays)
}

/** « du 10 novembre au 12 novembre 2026 », « le 10 novembre 2026 (demi-journée) ». */
export function staffLeavePeriodLabel(leave: { startDate: string; endDate: string; halfDay?: boolean }): string {
  return leavePeriodLabel(leave) + (leave.halfDay ? " (demi-journée)" : "")
}

/** The French refusal for a typed range, or null — the server's `StaffLeaveRequest.RefusalFor`, said on the field. */
export function staffLeaveRangeRefusal(startKey: string, endKey: string, halfDay: boolean): string | null {
  const refusal = leaveRangeRefusal(startKey, endKey)
  if (refusal) return refusal
  if (halfDay && startKey !== endKey) return "Une demi-journée ne peut porter que sur un seul jour."
  return null
}
