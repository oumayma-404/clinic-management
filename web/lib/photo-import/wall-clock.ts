/**
 * Wall-clock time as a plain number line (`features/camera-photo-import`).
 *
 * A camera writes the time on its own face into EXIF (« 2026:11:12 09:14:03 », no zone), and the server sends the
 * day's visits as clinic wall-clock (« 2026-11-12T09:00:00 », no offset). Comparing the two needs **no timezone at
 * all**, so both are parsed here into milliseconds of the clock's face, laid out as if it were UTC.
 *
 * ⚠️ **Never hand either string to `new Date(string)`.** A string without an offset is read in the WORKSTATION's zone,
 * so the same card would match differently on a PC set to Paris time. Everything that formats one of these numbers
 * formats it with `timeZone: "UTC"` for the same reason — that is the face, not a UTC instant.
 */

/** Milliseconds of a wall clock's face, laid out on the UTC line. Never a real instant. */
export type WallClock = number

export const MINUTE_MS = 60_000

const ISO_WALL = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?$/
const EXIF_WALL = /^(\d{4}):(\d{2}):(\d{2})[ T](\d{2}):(\d{2}):(\d{2})/

function build(
  year: number,
  month: number,
  day: number,
  hour: number,
  minute: number,
  second: number,
  ms = 0,
): WallClock | null {
  if (year < 1900 || month < 1 || month > 12 || day < 1 || day > 31 || hour > 23 || minute > 59 || second > 60) {
    return null
  }
  const value = Date.UTC(year, month - 1, day, hour, minute, second, ms)
  // Date.UTC rolls 31 February into March; a camera that wrote it is not telling the time.
  const check = new Date(value)
  if (check.getUTCDate() !== day || check.getUTCMonth() !== month - 1) return null
  return value
}

/** « 2026-11-12T09:00:00 » (the server's wall-clock) → the number line. A trailing zone is refused, not ignored. */
export function parseWallClockIso(value: string | null | undefined): WallClock | null {
  if (!value) return null
  const match = ISO_WALL.exec(value.trim())
  if (!match) return null
  const [, y, mo, d, h, mi, s, frac] = match
  const ms = frac ? Math.round(Number(`0.${frac}`) * 1000) : 0
  return build(Number(y), Number(mo), Number(d), Number(h), Number(mi), Number(s ?? 0), ms)
}

/**
 * EXIF's « 2026:11:12 09:14:03 » plus its optional sub-second digits (« 45 » = 0,45 s) → the number line.
 * A blank or zeroed field (« 0000:00:00 00:00:00 », what a camera with no clock writes) is null.
 */
export function parseExifDateTime(value: string | null | undefined, subSec?: string | null): WallClock | null {
  if (!value) return null
  const match = EXIF_WALL.exec(value.trim())
  if (!match) return null
  const [, y, mo, d, h, mi, s] = match
  const digits = subSec?.trim().replace(/\D/g, "") ?? ""
  const ms = digits ? Math.round(Number(`0.${digits}`) * 1000) : 0
  return build(Number(y), Number(mo), Number(d), Number(h), Number(mi), Number(s), ms)
}

/** « +01:00 » / « -05:30 » (EXIF OffsetTimeOriginal) → minutes east of UTC, or null. */
export function parseExifOffset(value: string | null | undefined): number | null {
  if (!value) return null
  const match = /^([+-])(\d{2}):(\d{2})$/.exec(value.trim())
  if (!match) return null
  const minutes = Number(match[2]) * 60 + Number(match[3])
  if (minutes > 14 * 60) return null
  return match[1] === "-" ? -minutes : minutes
}

/** A file's own date (an instant) on the wall clock of the workstation that reads it — the « heure incertaine » path. */
export function wallClockOfInstant(instantMs: number): WallClock {
  return instantMs - new Date(instantMs).getTimezoneOffset() * MINUTE_MS
}

const pad = (value: number, width = 2) => String(value).padStart(width, "0")

/** The number line → « 2026-11-12T09:14:03 », the wall-clock shape the server reads back with `ClinicClock.ToUtc`. */
export function toWallClockIso(value: WallClock): string {
  const date = new Date(value)
  return (
    `${date.getUTCFullYear()}-${pad(date.getUTCMonth() + 1)}-${pad(date.getUTCDate())}` +
    `T${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}:${pad(date.getUTCSeconds())}`
  )
}

/** « 2026-11-12 » — the day a wall-clock time falls on. */
export function wallClockDay(value: WallClock): string {
  return toWallClockIso(value).slice(0, 10)
}

/** « 2026-11-12 » → the number line at that day's midnight. */
export function wallClockOfDay(day: string): WallClock | null {
  return parseWallClockIso(`${day}T00:00:00`)
}

const TIME = new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "UTC" })
const DAY = new Intl.DateTimeFormat("fr-FR", { weekday: "long", day: "numeric", month: "long", timeZone: "UTC" })
const SHORT_DAY = new Intl.DateTimeFormat("fr-FR", { day: "numeric", month: "short", timeZone: "UTC" })

/** « 09:14 » — 24-hour, whatever the browser's locale. */
export function formatWallClockTime(value: WallClock): string {
  return TIME.format(value)
}

/** « jeudi 12 novembre » */
export function formatWallClockDay(value: WallClock): string {
  return DAY.format(value)
}

/** « 12 nov. » */
export function formatWallClockShortDay(value: WallClock): string {
  return SHORT_DAY.format(value)
}

/** « 1 h 02 » / « 4 min » — a duration for the clock notice. */
export function formatOffset(minutes: number): string {
  const total = Math.abs(Math.round(minutes))
  const hours = Math.floor(total / 60)
  const rest = total % 60
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${pad(rest)}`
}
