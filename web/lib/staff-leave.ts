// A secretary's congé, read in French. Days are `YYYY-MM-DD` clinic-local keys, compared as strings.
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import type { StatusTone } from "@/components/ui/status-tone"
import type {
  StaffLeaveAccrual,
  StaffLeaveAllowanceEntryDto,
  StaffLeaveCreditDto,
  StaffLeaveHalfDayPart,
  StaffLeaveMovementDto,
  StaffLeaveRuleDto,
  StaffLeaveStatus,
  StaffLeaveType,
} from "@/lib/api/team"
import { dayFromKey, leaveRangeRefusal } from "@/lib/doctor-leaves"

/** The server's own ceiling (`StaffLeaveRequest.MaxReasonLength`), said on the field before the round trip. */
export const MAX_STAFF_LEAVE_REASON = 500

/** The `/users` tab the bell and the request links open (`?onglet=conges`). */
export const USERS_LEAVE_TAB = "conges"
/** The `/users` tab « Équipe aujourd'hui » opens (`?onglet=presences`). */
export const USERS_ATTENDANCE_TAB = "presences"
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

/** « matin » / « après-midi » — which half of a demi-journée. */
export const STAFF_LEAVE_HALF_DAY_LABEL: Record<StaffLeaveHalfDayPart, string> = {
  Morning: "matin",
  Afternoon: "après-midi",
}

/** Who reads the line: her own screen says « vous », everybody else's names her. */
export type StaffLeaveReader = "self" | "other"

const dayFormatter = new Intl.NumberFormat("fr-FR", { maximumFractionDigits: 1 })
const MINUS = "−"

/** « 19,5 », « 3 », « −3 » — a day count without its unit, signed with a real minus. */
export function formatStaffDays(days: number): string {
  return days < 0 ? `${MINUS}${dayFormatter.format(-days)}` : dayFormatter.format(days === 0 ? 0 : days)
}

/** « 3 jours », « 1 jour », « 0,5 jour », « −3 jours » — plural from |days| > 1. */
export function staffDaysLabel(days: number): string {
  return `${formatStaffDays(days)} ${Math.abs(days) > 1 ? "jours" : "jour"}`
}

/** « 19,5 j » — a day count where the column already says « jours ». */
export function staffDaysShort(days: number): string {
  return `${formatStaffDays(days)} j`
}

/** A balance is overdrawn below zero — the one test every « Reste » / « Il restera » reads. */
export function isOverdrawn(days: number | null | undefined): boolean {
  return typeof days === "number" && days < 0
}

/** « 9 j », or « 3 j de dépassement » below zero — never a bare « −3 ». Render it with `isOverdrawn`'s red. */
export function staffBalanceShort(days: number): string {
  return days < 0 ? `${staffDaysShort(-days)} de dépassement` : staffDaysShort(days)
}

/** « 9 jours », or « 3 jours de dépassement » below zero. */
export function staffBalanceLabel(days: number): string {
  return days < 0 ? `${staffDaysLabel(-days)} de dépassement` : staffDaysLabel(days)
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

/** « +2 j », « −1,5 j » — a signed movement of the relevé. */
export function staffDaysSigned(days: number): string {
  if (days === 0) return "0 j"
  return `${days > 0 ? "+" : MINUS}${dayFormatter.format(Math.abs(days))} j`
}

export const STAFF_LEAVE_ACCRUAL_LABEL: Record<StaffLeaveAccrual, string> = {
  Year: "par an",
  Month: "par mois",
}

/** « 20 j par an · non pris perdus au 31/12 » — a rule in one line. */
export function leaveRuleSentence(rule: Pick<StaffLeaveRuleDto, "days" | "accrual" | "carryOver">): string {
  return `${staffDaysShort(rule.days)} ${STAFF_LEAVE_ACCRUAL_LABEL[rule.accrual]} · ${
    rule.carryOver ? "non pris reportés" : "non pris perdus au 31/12"
  }`
}

/**
 * The rule in force and the one that follows it: « 24 j par an · non pris perdus au 31/12 → 2 j par mois à partir du
 * 1er décembre ». A rule not in force yet says « à partir du »; null when no rule was ever set.
 */
export function leaveRuleLine(
  rule: StaffLeaveRuleDto | null,
  nextRule: StaffLeaveRuleDto | null,
  todayKey: string,
): string | null {
  const first = rule ?? nextRule
  if (!first) return null
  let text = leaveRuleSentence(first)
  if (first.since > todayKey) text += ` à partir du ${firstOfLabel(first.since)}`
  if (rule && nextRule && nextRule.since !== rule.since) {
    const sameCarry = nextRule.carryOver === rule.carryOver
    const next = sameCarry
      ? `${staffDaysShort(nextRule.days)} ${STAFF_LEAVE_ACCRUAL_LABEL[nextRule.accrual]}`
      : `${leaveRuleSentence(nextRule)},`
    text += ` → ${next} à partir du ${firstOfLabel(nextRule.since)}`
  }
  return text
}

/** « 1er janvier 2027 », « 6 juillet » — a day in a sentence, the year named when not this one. */
export function firstOfLabel(dayKey: string, thisYear = new Date().getFullYear()): string {
  const day = dayFromKey(dayKey)
  const text = format(day, day.getFullYear() === thisYear ? "d MMMM" : "d MMMM yyyy", { locale: fr })
  return text.replace(/^1 /, "1er ")
}

/** « +2 j le 1er décembre ». */
export function nextCreditLabel(credit: StaffLeaveCreditDto): string {
  return `${staffDaysSigned(credit.days)} le ${firstOfLabel(credit.day)}`
}

/** The year whose days a `LostLastYear` line reports — the line opens the next year's relevé. */
function lostYearOf(movement: StaffLeaveMovementDto): number {
  const year = Number(movement.day.slice(0, 4))
  return movement.day.slice(5, 7) === "12" ? year : year - 1
}

/** What a relevé line is, in French — the dates and amounts are rendered beside it. */
export function leaveMovementLabel(movement: StaffLeaveMovementDto): string {
  const year = Number(movement.day.slice(0, 4))
  switch (movement.kind) {
    case "CarriedIn":
      return movement.days >= 0 ? `Report de ${year - 1}` : `Dépassement reporté de ${year - 1}`
    case "Opening":
      return "Solde de départ"
    case "Credit":
      return movement.accrual === "Month"
        ? `Acquis — ${format(dayFromKey(movement.day), "MMMM", { locale: fr })}`
        : `Droit ${year}`
    case "Adjustment": {
      const what = movement.days > 0 ? "Jours ajoutés" : "Jours retirés"
      return movement.reason ? `${what} · ${movement.reason}` : what
    }
    case "Taken":
    case "Planned": {
      const period =
        movement.startDate && movement.endDate
          ? staffLeaveShortPeriod({ startDate: movement.startDate, endDate: movement.endDate })
          : ""
      return `${movement.kind === "Planned" ? "Congé prévu" : "Congé"}${period ? ` · ${period}` : ""}`
    }
    case "Expired":
      return `Non pris en ${year} : perdus`
    case "LostLastYear":
      return `${staffDaysShort(Math.abs(movement.days))} non pris en ${lostYearOf(movement)} : perdus`
  }
}

/** A « Droit à congé » line as the admin's list states it. */
export function allowanceEntryLabel(entry: StaffLeaveAllowanceEntryDto): string {
  switch (entry.kind) {
    case "Rule":
      return entry.accrual
        ? `Règle : ${leaveRuleSentence({ days: entry.days, accrual: entry.accrual, carryOver: entry.carryOver })}, à partir du ${firstOfLabel(entry.onDay)}`
        : "Règle"
    case "Opening":
      return `Solde de départ : ${staffDaysShort(entry.days)} au ${format(dayFromKey(entry.onDay), "d MMM yyyy", { locale: fr })}`
    case "Adjustment":
      return `${entry.days > 0 ? "Ajout" : "Retrait"} de ${staffDaysShort(Math.abs(entry.days))}${entry.reason ? ` · ${entry.reason}` : ""}`
  }
}

/** « Solde après », or « Solde 2027 après » when the congé starts in another year than the one on screen. */
export function balanceAfterLabel(startDate: string, thisYear = new Date().getFullYear()): string {
  const year = Number(startDate.slice(0, 4))
  return year === thisYear ? "Solde après" : `Solde ${year} après`
}

type LeaveDates = {
  startDate: string
  endDate: string
  halfDay?: boolean
  halfDayPart?: StaffLeaveHalfDayPart | null
}

const capitalize = (text: string) => text.charAt(0).toUpperCase() + text.slice(1)

/** « 1er », « 16 » — the day of the month as French writes it. */
const dayNumber = (date: Date) => (date.getDate() === 1 ? "1er" : String(date.getDate()))

/** « matin », « après-midi », or « demi-journée » for an older half day nobody qualified. */
export function staffHalfDayLabel(leave: Pick<LeaveDates, "halfDayPart">): string {
  return leave.halfDayPart ? STAFF_LEAVE_HALF_DAY_LABEL[leave.halfDayPart] : "demi-journée"
}

/**
 * The period as a row title: « Lundi 21 décembre · matin », « 16 → 18 novembre », « 30 déc. → 2 janv. 2027 ». The
 * year is named only when it is not this one, or when the congé crosses it. Cards and rows only — a sentence uses
 * {@link staffLeavePeriodLabel}.
 */
export function staffLeaveShortPeriod(leave: LeaveDates, thisYear = new Date().getFullYear()): string {
  const start = dayFromKey(leave.startDate)
  const end = dayFromKey(leave.endDate)
  if (leave.startDate === leave.endDate) {
    const year = start.getFullYear() === thisYear ? "" : " yyyy"
    const half = leave.halfDay ? ` · ${staffHalfDayLabel(leave)}` : ""
    return `${capitalize(format(start, "EEEE", { locale: fr }))} ${dayNumber(start)} ${format(start, `MMMM${year}`, { locale: fr })}${half}`
  }
  if (start.getFullYear() !== end.getFullYear()) {
    return `${dayNumber(start)} ${format(start, "MMM", { locale: fr })} → ${dayNumber(end)} ${format(end, "MMM yyyy", { locale: fr })}`
  }
  const year = end.getFullYear() === thisYear ? "" : " yyyy"
  if (start.getMonth() === end.getMonth()) {
    return `${dayNumber(start)} → ${dayNumber(end)} ${format(end, `MMMM${year}`, { locale: fr })}`
  }
  return `${dayNumber(start)} ${format(start, "MMM", { locale: fr })} → ${dayNumber(end)} ${format(end, `MMM${year}`, { locale: fr })}`
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

/**
 * The days as a row states them: « 3 jours »; an accepted congé payé says what came off the balance — « 2,5 j
 * retirés de votre solde » — since every date is granted and only the counted days are withdrawn.
 */
export function staffLeaveDaysText(
  leave: { requestedDays: number; countedDays: number | null; type?: StaffLeaveType; status?: StaffLeaveStatus },
  reader: StaffLeaveReader = "other",
): string {
  const counted = leave.countedDays ?? leave.requestedDays
  if (leave.status === "Approved" && leave.type === "PaidLeave") {
    return `${staffDaysShort(counted)} retiré${counted > 1 ? "s" : ""} ${reader === "self" ? "de votre solde" : "du solde"}`
  }
  return staffDaysLabel(leave.status === "Approved" ? counted : leave.requestedDays)
}

/**
 * The period inside a sentence: « du 16 au 18 novembre », « le 16 novembre (matin) », « du 30 décembre 2026 au
 * 2 janvier 2027 ». The year is named only when it is not this one, or when the congé crosses it.
 */
export function staffLeavePeriodLabel(leave: LeaveDates, thisYear = new Date().getFullYear()): string {
  const start = dayFromKey(leave.startDate)
  const end = dayFromKey(leave.endDate)
  const month = (date: Date, withYear: boolean) => format(date, withYear ? "MMMM yyyy" : "MMMM", { locale: fr })
  if (leave.startDate === leave.endDate) {
    const half = leave.halfDay ? ` (${staffHalfDayLabel(leave)})` : ""
    return `le ${dayNumber(start)} ${month(start, start.getFullYear() !== thisYear)}${half}`
  }
  if (start.getFullYear() !== end.getFullYear()) {
    return `du ${dayNumber(start)} ${month(start, true)} au ${dayNumber(end)} ${month(end, true)}`
  }
  const withYear = end.getFullYear() !== thisYear
  if (start.getMonth() === end.getMonth()) return `du ${dayNumber(start)} au ${dayNumber(end)} ${month(end, withYear)}`
  return `du ${dayNumber(start)} ${month(start, false)} au ${dayNumber(end)} ${month(end, withYear)}`
}

/** A waiting request is withdrawn, an accepted congé is cancelled — the action and the toast, for each. */
export function staffLeaveCancelWording(status: StaffLeaveStatus): { action: string; done: string } {
  return status === "Pending"
    ? { action: "Retirer la demande", done: "Demande retirée" }
    : { action: "Annuler le congé", done: "Congé annulé" }
}

/** The French refusal for a typed range, or null — the server's `StaffLeaveRequest.RefusalFor`, said on the field. */
export function staffLeaveRangeRefusal(startKey: string, endKey: string, halfDay: boolean): string | null {
  const refusal = leaveRangeRefusal(startKey, endKey)
  if (refusal) return refusal
  if (halfDay && startKey !== endKey) return "Une demi-journée ne peut porter que sur un seul jour."
  return null
}
