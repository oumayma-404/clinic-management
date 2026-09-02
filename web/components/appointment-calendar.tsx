"use client"

import { Card } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog"
import { AppointmentQuickActions } from "@/components/appointment-quick-actions"
import { Switch } from "@/components/ui/switch"
import { Label } from "@/components/ui/label"
import { ExportButton } from "@/components/ui/export-button"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import {
  ChevronLeft,
  ChevronRight,
  ChevronDown,
  Calendar,
  CalendarCheck,
  CalendarPlus,
  Filter,
  CloudOff,
  UploadCloud,
  Plus,
  UserX,
  MoreHorizontal,
  Unlink,
} from "lucide-react"
import { format, addDays, startOfWeek, endOfWeek, addWeeks, subWeeks, subDays, startOfDay, endOfDay, isToday, startOfMonth, endOfMonth, addMonths, subMonths, isSameMonth, isSameDay } from "date-fns"
import { AgendaPhoneHeader } from "@/components/agenda-phone-header"
import { fr } from "date-fns/locale"
import { useCallback, useMemo, useRef, useEffect, useState, type CSSProperties } from "react"
import { toast } from "sonner"
import { useAppointments } from "@/lib/hooks/use-appointments"
import { googleCalendarApi } from "@/lib/api/google-calendar"
import { appointmentsApi } from "@/lib/api/appointments"
import { ApiError, ApiErrorCode } from "@/lib/api/client"
import { showErrorToast } from "@/lib/errors"
import { useConnectivity } from "@/lib/connectivity/connectivity"
import { useMediaQuery, COARSE_POINTER_QUERY } from "@/lib/hooks/use-media-query"
import {
  AGENDA_CELL_ATTR,
  AGENDA_NO_DRAG_ATTR,
  AGENDA_SNAP_MINUTES,
  useAgendaGridDrag,
  type AgendaCellTarget,
} from "@/components/agenda-grid-drag"
import { useSession } from "@/lib/auth/session"
import type { AppointmentDto } from "@/lib/api/types"
import { cn, parseDurationToMinutes } from "@/lib/utils"
import { clinicsApi, type DoctorDto } from "@/lib/api/clinics"
import { doctorsApi } from "@/lib/api/doctors"
import { WEEKDAYS, type WorkingDay } from "@/lib/working-hours"
import {
  APPOINTMENT_STATUSES,
  APPOINTMENT_STATUS_TONE,
  appointmentActsCount,
  appointmentActsSummary,
  appointmentStatusLabel,
  isBusySlot,
  normalizeStatus,
} from "@/components/appointment-labels"
import type { StatusTone } from "@/components/ui/status-tone"

/** `13` → `"13:00"`. One place, because the hour label, the `data-time-slot` key and the scroll target must agree. */
const hourSlot = (hour: number): string => `${String(hour).padStart(2, "0")}:00`

/** `585` → `"09:45"`. The grid gestures speak in minutes past midnight; the user reads a clock. */
const clockOfMinutes = (minutes: number): string =>
  `${String(Math.floor(minutes / 60) % 24).padStart(2, "0")}:${String(minutes % 60).padStart(2, "0")}`

/**
 * The hours the time grid renders when the clinic has configured no working hours at all.
 *
 * Not 0–24. An unconfigured clinic used to get the full twenty-four rows, of which a dental practice uses about
 * eleven — so two thirds of the scroll length was hours nobody has ever booked, and the morning and the afternoon
 * could not be on screen together at any window height. The window still **grows over anything actually booked**
 * (see `gridWindow`), so this is a starting frame, never a filter.
 */
const DEFAULT_GRID_FROM_HOUR = 8
const DEFAULT_GRID_TO_HOUR = 19

/**
 * The open window for one calendar day, from the clinic's saved hours. Returns null when nothing is configured
 * or the day is closed — the caller shades accordingly.
 */
type OpenWindow = { fromHour: number; toHour: number; breakFromHour: number | null; breakToHour: number | null }

function openWindowFor(day: Date, hours: WorkingDay[] | null): OpenWindow | null {
  if (!hours || hours.length === 0) return null
  const name = WEEKDAYS[(day.getDay() + 6) % 7] // WEEKDAYS starts Monday; Date.getDay() starts Sunday
  const match = hours.find((h) => h.day?.trim().toLowerCase() === name.toLowerCase())
  if (!match || !match.enabled) return null
  const from = Number.parseInt(match.from?.slice(0, 2) ?? "", 10)
  const to = Number.parseInt(match.to?.slice(0, 2) ?? "", 10)
  if (!Number.isFinite(from) || !Number.isFinite(to) || from >= to) return null

  // A break is drawn only on the hour rows it covers ENTIRELY. 12:00-14:00 hatches 12 and 13; 12:30-13:30
  // hatches nothing, because half of each of those rows is genuinely open and shading them would say the
  // cabinet is shut when it is not. The booking guard is exact; the grid is an hour tall.
  const breakFrom = Number.parseInt(match.breakFrom?.slice(0, 2) ?? "", 10)
  const breakTo = Number.parseInt(match.breakTo?.slice(0, 2) ?? "", 10)
  const wholeHourBreak =
    Number.isFinite(breakFrom) &&
    Number.isFinite(breakTo) &&
    match.breakFrom?.slice(3) === "00" &&
    match.breakTo?.slice(3) === "00" &&
    breakFrom < breakTo
  return {
    fromHour: from,
    toHour: to,
    breakFromHour: wholeHourBreak ? breakFrom : null,
    breakToHour: wholeHourBreak ? breakTo : null,
  }
}

/** Is one hour row inside the open window and outside the mid-day closure? One test, used at all three sites. */
function isHourOpen(hour: number, window: OpenWindow | null): boolean {
  if (window === null) return true
  if (hour < window.fromHour || hour >= window.toHour) return false
  if (window.breakFromHour === null || window.breakToHour === null) return true
  return hour < window.breakFromHour || hour >= window.breakToHour
}

/**
 * Fixed pixel height of one hour row; appointment blocks are positioned/sized against it.
 *
 * 48, not 35. At 35 px/hour the *default* 30-minute appointment is 17.5 px tall — below `MIN_APPT_HEIGHT`,
 * so it was clamped taller than its own slot AND fell under the `isVerySmall` threshold below, which strips
 * the duration badge, the procedure name and the sync badge. The standard appointment rendered in the
 * degraded mode meant for a 5-minute one. Worse, two back-to-back 15-minute appointments sit 8.75 px apart
 * at 35 px/hour while both are clamped to 20 px, so they *visually overlapped* — and `computeOverlapLanes`
 * cannot lane them apart, because they do not overlap in time. At 48 a 30-minute block is 24 px (legible,
 * unclamped) and a 15-minute block is 12 px, so the clamp only ever bites on genuinely tiny durations.
 */
const HOUR_HEIGHT = 48
/**
 * The phone's hour row, taller than the desktop's — Google Calendar's own phone day view is ~64dp/hour, and the
 * reason is not taste. A phone shows ONE day, so the vertical axis is the only axis there is to spend: at 48 a
 * 30-minute visit is 24 px, which after padding fits one clipped line and nothing else, so the patient's name is
 * all a block can ever say. At 64 it is 32 px — name plus its start time — and a 15-minute block still clears
 * `MIN_APPT_HEIGHT` on its own rather than being clamped into its neighbour.
 */
const HOUR_HEIGHT_PHONE = 64
/**
 * Minimum rendered height so a very short appointment still shows the patient name legibly (AC-4).
 *
 * **Two values, because the two devices fail differently.** On a desk machine 18 px is the floor at which a
 * 15-minute block (12 px at `HOUR_HEIGHT`) stays readable. On a phone the appointment block is the most-tapped
 * control in the whole app and 18 px is barely a third of a fingertip, so its floor is 28.
 *
 * ⚠️ A floor is **not** a licence to overlap, and raising it is what makes that visible. Two consecutive
 * quarter-hour appointments sit `15/60 × 64 = 16 px` apart on a phone (12 px on a desktop), so clamping blindly
 * would paint the 09:00 block 12 px over the 09:15 one — an overlap `computeOverlapLanes` **cannot** lane apart,
 * because the two do not overlap *in time* and so are never in the same cluster. `renderAppointmentBlock`
 * therefore clamps to `min(floor, distance to the next appointment's start)`: a short visit with room around it
 * grows to the floor, a short visit with a neighbour keeps its true height. That is also why
 * `computeOverlapLanes` now reports `minutesToNextStart` — the geometry needs it, and the lane packer is the one
 * place that has already sorted the day.
 */
const MIN_APPT_HEIGHT = 18
const MIN_APPT_HEIGHT_PHONE = 28

/**
 * The week grid's columns — **one definition, used by the header, the hour grid and the loading skeleton**,
 * because three copies of a column template is three chances for the dates to sit over the wrong columns.
 *
 * ⚠️ The `120px` is not a taste choice, it is an **arithmetic contract with `weekBandWidthExpr`** (AC-30).
 * While the grid is wider than the viewport it scrolls, so the wrapper is `w-max`: its width is
 * `60 + 7 × 120 = 900px`, and the overlay's `(100% - 60px) / 7` therefore resolves to exactly `120px`. Change
 * one number without the other and every appointment block drifts sideways by a few pixels per column — the
 * kind of wrong that looks like a rendering glitch rather than a maths error. `check:responsive`'s
 * `agenda-scroll` reads both numbers out of this file for that reason.
 *
 * ⚠️ **The fluid override is `lg:`, not `md:`, and that one prefix is the entire 768–1023 px tablet band.**
 * At `md:` the columns became `1fr` while the rail is still 256 px wide, so at 820 px the seven day columns
 * shared ~514 px — about 65 px each, i.e. a ~61 px appointment block, of which the padding, the gap and the
 * duration badge leave roughly 11 px for the patient's name. A tablet is not a small desktop; below `lg:` it
 * gets the same honest scrolling grid a phone would, one size up.
 *
 * ⚠️ `minmax(120px,1fr)` rather than a bare `120px`, and that is the contract again rather than taste: the
 * wrapper carries `min-w-full`, so whenever the container happens to be wider than 900 px (a collapsed rail at
 * ~1000 px) a fixed track leaves dead space at the end while `100%` still means the *wrapper* — and every block
 * drifts. A flexible track absorbs exactly that surplus, so `(100% - 60px) / 7` stays true at every width.
 * Under `w-max` (indefinite available space) a `minmax(120px,1fr)` track resolves to its 120 px base, so the
 * 900 px intrinsic width is unchanged.
 */
const WEEK_COLS = "grid-cols-[60px_repeat(7,minmax(120px,1fr))] lg:grid-cols-[60px_repeat(7,minmax(0,1fr))]"
// Month view: max appointment chips shown per day cell before collapsing the rest into "+N more" (AC-2).
const MONTH_CELL_MAX_CHIPS = 3

/**
 * ── What an hour cell is painted with ───────────────────────────────────────────────────────────────────────
 *
 * Both are `background-image`, and today's band is a `background-color` under them, so the three compose in a
 * defined order on one element. That is the point: `isToday(day) && "bg-primary/[0.04]"` and
 * `!dayOpen && "bg-muted/40"` were two Tailwind `background-color` utilities on the same cell, and which one won
 * was decided by **stylesheet order**, not by intent — a closed hour of today was a coin toss. Longhands cannot
 * collide.
 *
 * `HALF_HOUR_GUIDE` is new: a 30-minute visit is this product's default booking length, so half the appointments
 * in the book start on a line the ruler did not draw. One hairline at 50 % of each row is what lets the eye read
 * « 14:30 » off the grid instead of estimating it.
 *
 * `CLOSED_HATCH` replaces `bg-muted/40 opacity-70`. The `opacity` was the defect: it faded the cell's **own
 * gridlines** along with the fill, so a closed column lost its structure and read as a rendering fault rather
 * than as a decision. A diagonal hatch is the standing "unavailable" convention, keeps the borders crisp, and —
 * being an image over a colour — lets today's band show through underneath on a day that is both.
 */
const HALF_HOUR_GUIDE =
  "linear-gradient(to bottom, transparent calc(50% - 1px), var(--agenda-guide) calc(50% - 1px), var(--agenda-guide) 50%, transparent 50%)"
const CLOSED_HATCH =
  "repeating-linear-gradient(135deg, var(--agenda-closed) 0, var(--agenda-closed) 1px, transparent 1px, transparent 7px)"

/**
 * One hour cell's `background-image`, guide first so it paints above the hatch.
 *
 * ⚠️ Today's band is deliberately **not** folded in here as an inline `background-color`. An inline style beats
 * a class in every state, including `:hover` — so painting the band inline would silently delete
 * `hover:bg-accent/30` on today's column, i.e. remove the click affordance from the one column most likely to
 * be clicked. It stays a class (`bg-[var(--agenda-today)]`), and it no longer collides with anything because
 * "closed" is now an image rather than a competing background colour.
 */
const hourCellBackground = (isOpenHour: boolean): string =>
  isOpenHour ? HALF_HOUR_GUIDE : `${HALF_HOUR_GUIDE}, ${CLOSED_HATCH}`

/**
 * Mois on a phone is a **continuous scroll into the following months**, and these three numbers bound it.
 *
 * A month that stops at its own last row is a dead end on a phone: the desktop grid can afford ‹ › arrows in a
 * toolbar, but the phone gesture for "what about after that" is to keep scrolling — so the weeks simply carry on,
 * with a month heading where each new month starts, exactly like Google Calendar's month view.
 *
 * ⚠️ The span is **not** fixed at the maximum, because the rendered weeks decide the fetch window (see `endDate`):
 * asking for a year of appointments up front to draw dots nobody scrolled to would make Mois the most expensive
 * read in the app. It starts at three months and grows only as the user actually reaches the bottom.
 */
const PHONE_MONTH_AHEAD_INITIAL = 2
const PHONE_MONTH_AHEAD_STEP = 3
const PHONE_MONTH_AHEAD_MAX = 11
// Distance from the bottom at which the next batch of months is appended.
const PHONE_MONTH_GROW_THRESHOLD_PX = 240
// Dots per day cell in the phone month view before the rest go unshown (the cell is ~52px wide).
const PHONE_MONTH_MAX_DOTS = 3
/**
 * Weekday initials for the phone month header, Monday-first like every other grid here. Hardcoded rather than
 * `format(day, "EEEEE")`, because French gives L M M J V S D — two indistinguishable « M »s — so the header needs
 * a stable key per column anyway, and the cells below carry the full weekday name in their `sr-only` label.
 */
const PHONE_WEEKDAY_INITIALS = ["L", "M", "M", "J", "V", "S", "D"] as const


/**
 * A procedure colour, validated. Returns `#rrggbb` or `null`.
 *
 * ⚠️ The validation is not defensive tidiness, it is the reason this value may be **interpolated into a CSS
 * expression** at all (`color-mix(… ${hex} …)`). `procedureColorHex` is clinic-editable data arriving over the
 * wire; a strict shape test is what stops an unexpected string from becoming a CSS declaration of its own.
 * Anything that is not three or six hex digits falls back to the themed default rather than painting garbage.
 */
function parseProcedureHex(value: string | null | undefined): string | null {
  if (!value) return null
  const hex = value.trim().replace(/^#/, "")
  if (/^[0-9a-fA-F]{3}$/.test(hex)) {
    return `#${hex[0]}${hex[0]}${hex[1]}${hex[1]}${hex[2]}${hex[2]}`
  }
  return /^[0-9a-fA-F]{6}$/.test(hex) ? `#${hex}` : null
}

/** The status tone an appointment paints with — the same table the status badges read. */
function appointmentTone(appointment: { status: string }): StatusTone | undefined {
  return APPOINTMENT_STATUS_TONE[normalizeStatus(appointment.status)]
}

/**
 * ── The two axes, and why they are on opposite edges ────────────────────────────────────────────────────────
 *
 * A block answers two questions that must never be confused: **what is it** (the act) and **where has it got
 * to** (the statut). The act owns the left rail and the surface; the statut owns a 3 px strip on the **right**
 * edge, painted from this table.
 *
 * ⚠️ **The right edge is the only place that survives a 12 px block.** A block is sized by duration, so a
 * quarter-hour visit is 12 px tall at `HOUR_HEIGHT` — there is no room there for a pip, an icon or a word, and
 * a treatment that disappears at the density the grid is busiest is not a status indicator. A vertical strip is
 * full height whatever the height is.
 *
 * ⚠️ **`pending` and `accepted` were previously painted IDENTICALLY**, because `appointmentAppearance`'s switch
 * had no case for either and both fell through to `default`. « Planifié » versus « Confirmé » — did the patient
 * say yes, or did we merely write them in the book — is the single distinction a desk reads an agenda for, and
 * `appointment-labels.ts` says so in its own docstring while nothing on screen carried it. So `pending` is a
 * **dashed** strip (provisional, and legible without colour) and `accepted` a solid primary one.
 *
 * ⚠️ Colour is never the sole carrier: the form treatments in `appointmentAppearance` — strikethrough, a dashed
 * rail, an inset ring — remain, and « Absent » keeps its `UserX` glyph. This strip is the axis that reads at a
 * glance across a whole week; those are what make it survive a colour-blind reader and a greyscale printout.
 */
const STATUS_EDGE_PAINT: Record<StatusTone, string> = {
  pending:
    "repeating-linear-gradient(to bottom, color-mix(in oklab, var(--muted-foreground) 60%, transparent) 0 4px, transparent 4px 8px)",
  accepted: "var(--primary)",
  active: "var(--warning)",
  positive: "var(--success)",
  negative: "var(--destructive)",
  neutral: "var(--muted-foreground)",
}

/**
 * The strip's paint, or `null` when the row has no statut worth showing.
 *
 * ⚠️ A **« créneau occupé » gets none**, deliberately. `AppointmentProgressJob` moves any row whose slot has
 * begun to `InProgress`, so a blocked hour acquired « En cours » — and with it the amber inset ring — which is
 * how a slot the practitioner simply reserved came to render as the loudest thing on the week. Nobody is at the
 * fauteuil; there is no statut to report. Same line as `f441d1d`, one surface over.
 */
function statusEdgePaint(appointment: AppointmentDto): string | null {
  if (isBusySlot(appointment)) return null
  const tone = appointmentTone(appointment)
  return tone ? STATUS_EDGE_PAINT[tone] : null
}

/**
 * The strip itself. A child rather than an `inset` box-shadow, and that is load-bearing: `box-shadow` is already
 * spoken for on these blocks by `hover:shadow-md` and by `ring-2 ring-inset` — and an **inline** `boxShadow`
 * beats a class at every state, so composing the strip that way would silently delete the hover elevation and
 * the « En cours » ring. The parent is absolutely positioned (or made `relative`), so this needs no extra class
 * on the ancestor.
 */
function renderStatusEdge(appointment: AppointmentDto) {
  const paint = statusEdgePaint(appointment)
  if (!paint) return null
  return (
    <span
      aria-hidden="true"
      className="pointer-events-none absolute inset-y-0 end-0 w-[3px]"
      style={{ background: paint }}
    />
  )
}

/**
 * How one appointment paints: **the procedure's hue is its identity, the status is a treatment on top of it.**
 * Composed, never one *instead of* the other — which is exactly the defect this replaces.
 *
 * ⚠️ The old function `return`ed from inside `if (appointment.procedureColorHex)`, so the status branch below it
 * was unreachable for any appointment carrying an act colour. Every seeded `ProcedureType` has one, so that was
 * not an edge case, it was the normal case: a **cancelled** visit rendered pixel-identical to a live one, and
 * the same value feeds `renderMonthChip`, the phone-month dots and the week strip — four surfaces, one blind
 * spot. The status branch was *also* missing `noshow` and `inprogress` entirely; both fell through to a default
 * identical to `scheduled`, so « Absent » — the status the desk most needs to spot — could not be shown at all.
 *
 * ⚠️ Two further things the old branch got wrong, both theme-blind. It painted `color: <the raw act hex>` on a
 * 15 %-alpha wash of that same hue over white: a light act colour lands near 2:1, and there was no `dark:`
 * counterpart at all, so on a dark ground the wash sat on nothing. Here the hue is spent where a hue reads best
 * and contrast does not apply — the 4 px left border — the surface is `color-mix(… var(--card))` so it tracks
 * the theme's own card, and the text comes from the theme (`text-card-foreground`). One expression, correct in
 * both themes.
 *
 * The status → treatment mapping is keyed on `APPOINTMENT_STATUS_TONE`, the table `appointmentStatusBadgeClass`
 * already reads, so the agenda and a status badge cannot describe the same appointment differently.
 */
function appointmentAppearance(appointment: AppointmentDto): { className: string; style: CSSProperties } {
  const tone = appointmentTone(appointment)
  const hex = parseProcedureHex(appointment.procedureColorHex)

  const classes = ["border-l-4"]
  const style: CSSProperties = {}

  if (isBusySlot(appointment)) {
    /*
     * A blocked slot is not a visit — amber, through the shared warning tokens rather than the `amber-100`
     * literals this carried, which no theme could follow.
     *
     * ⚠️ It **returns early**, and that is the fix for the loudest thing on the screen this redesign started
     * from. `AppointmentProgressJob` moves any row whose slot has begun to `InProgress`, so a reserved hour
     * reached the `active` case below and painted a 2 px amber ring on an amber wash — a « créneau occupé » on a
     * closed Sunday rendering as the most urgent thing in the week. Nobody is at the fauteuil, so there is no
     * statut to report: no ring here, and no edge strip from `statusEdgePaint`. Same line as `f441d1d`, one
     * surface over.
     */
    classes.push("bg-warning-wash font-semibold text-warning-ink")
    /*
     * ⚠️ `--warning-ink`, not `--warning`, because this 4 px edge is drawn **on the wash it belongs to** — the
     * one situation `ui/status-tone.ts` keeps a separate ink step for. Since the amber was warmed and
     * brightened, `--warning` reads 2.8:1 against its own wash (under the 3:1 non-text floor) while the ink
     * reads 4.6:1; and the edge now matches the `text-warning-ink` words inside the block instead of being a
     * second, paler amber beside them.
     */
    style.borderLeftColor = "var(--warning-ink)"
    return { className: cn(...classes), style }
  }

  if (hex) {
    /*
     * ⚠️ **`--act-tint`, not a percentage typed here.** This was a hardcoded 12 % (6 % when terminé) mixed
     * toward the card — *half* the strength the rest of the product paints an act colour at. `globals.css`
     * declares `--act-tint: 22%` light / `36%` dark and `lib/dashboard/act-colour.ts` is the dashboard's
     * consumer. At 12 % of a mid-chroma hue into pure white a block is a rectangle that is very slightly not
     * white, which is why two different acts were indistinguishable at arm's length — the exact opposite of what
     * a per-act colour exists for.
     *
     * The token also gets dark mode right for free: a pale wash on a `0.305` card disappears, which is why the
     * dark value is *stronger* rather than inverted. A literal could never track that.
     *
     * `actTintStyle` itself is deliberately not reused: it pairs the fill with an all-round inset hairline, and
     * the `box-shadow` slot on these blocks already carries hover elevation and the « En cours » ring. What is
     * shared is the decision — the tokens — not the helper.
     *
     * « Terminé » keeps the act's hue at 45 % of it, so a finished column recedes behind the live one instead of
     * shouting at the same volume. It is still that act, so it is still that hue.
     */
    const tint = tone === "positive" ? "calc(var(--act-tint) * 0.45)" : "var(--act-tint)"
    classes.push("text-card-foreground shadow-sm")
    style.backgroundColor = `color-mix(in oklab, ${hex} ${tint}, var(--card))`
    style.borderLeftColor = hex
  } else {
    classes.push("bg-accent text-accent-foreground")
    style.borderLeftColor = "var(--primary)"
  }

  /*
   * ⚠️ **`negative` and `positive` no longer overwrite the rail, and that is the point of splitting the axes.**
   * They used to repaint `borderLeftColor` with `--destructive` / `--success`, so the two statuses a desk most
   * needs to spot were also the two that **erased the act** — « who didn't turn up, and for what? » lost its
   * second half, and a whole finished morning went uniformly green. The statut has its own edge now, so every
   * treatment left here is one that *composes* with the hue rather than replacing it.
   */
  switch (tone) {
    case "neutral":
      // Annulé. Struck through and faded, but still legible: « Annulés affichés » exists precisely so the desk
      // can read what was cancelled. The rail does drop to a neutral — a cancelled visit is the one case where
      // the act genuinely stops identifying anything, because it is not going to happen.
      classes.push("opacity-60 line-through")
      style.borderLeftColor = "var(--muted-foreground)"
      break
    case "negative":
      // Absent. `border-dashed` reads as "did not happen" and — with the `UserX` glyph the block renders and the
      // red edge strip — states the status through **form as well as colour**, so it survives a colour-blind
      // reader and a greyscale printout.
      classes.push("border-dashed")
      break
    case "positive":
      classes.push("shadow-none")
      break
    case "active":
      // En cours. A ring rather than a border, so it composes with the act's hue instead of overwriting it.
      // `--warning` and not `--accent`: `active` is the amber step of the shared tone scale (the tone that asks
      // for attention), and this theme's `--accent` is a near-white tint of the accent hue — invisible as a ring.
      classes.push("ring-2 ring-inset ring-warning")
      break
    default:
      // `pending` and `accepted` need no *surface* treatment: nothing has happened to the visit yet, so the
      // act's colour is the whole of the block's identity. What tells the two apart is the **edge strip** —
      // dashed grey for Planifié, solid primary for Confirmé. Until that existed this branch was the entire
      // answer for both, and « le patient a confirmé » was unrepresentable on the agenda.
      break
  }

  return { className: cn(...classes), style }
}

/**
 * The **statut** half of the legend, as data — because it is rendered twice (the phone disclosure and the
 * desktop popover) and the two copies had already drifted into hardcoding three of the six statuses between
 * them.
 *
 * ⚠️ **It used to be actively wrong, not merely incomplete.** Its swatches were flat fills — « Planifié » was a
 * solid `bg-primary` square — while a Planifié block is painted in its **act's** colour and has never once been
 * blue. A legend that names a colour the grid does not use is worse than no legend: it teaches a mapping, and
 * then every block contradicts it. The swatches are now the *actual* right-edge paint (`STATUS_EDGE_PAINT`), so
 * the key and the grid are one expression, and « Confirmé » — which was missing entirely — is in the list
 * because it is finally distinguishable on screen.
 *
 * The **act** half is not a list at all: it is derived from the appointments in the visible window (see
 * `actLegend`), because a hand-written table of act colours would be a second answer to a question
 * `ProcedureType.ColorHex` already owns. `appointmentAppearance`'s own note called that legend the deferred
 * half; this is it.
 */
const LEGEND_ITEMS: { label: string; tone: StatusTone }[] = (() => {
  /*
   * ⚠️ **Derived from the status table, never listed here** — one row per *paint*, not per status.
   *
   * The old hardcoded list is what let this legend go stale twice over: it named five of six statuses, omitted
   * « Confirmé » entirely, and gave « Planifié » a solid blue swatch the grid has never once drawn. Listing them
   * again — even correctly — would only reset that clock.
   *
   * There are six tones and the status set is open, so two statuses can legitimately share a strip; when they do
   * the row names **both** (« Planifié / Séance passée ») rather than teaching one of them as the meaning of a
   * colour the other also wears. A `Map` keyed on tone preserves insertion order, so the rows come out in
   * `APPOINTMENT_STATUSES`' own lifecycle order for free.
   */
  const byTone = new Map<StatusTone, string[]>()
  for (const status of APPOINTMENT_STATUSES) {
    const tone = APPOINTMENT_STATUS_TONE[status]
    if (!tone) continue
    const label = appointmentStatusLabel(status)
    const bucket = byTone.get(tone)
    if (bucket) bucket.push(label)
    else byTone.set(tone, [label])
  }
  return Array.from(byTone, ([tone, labels]) => ({ tone, label: labels.join(" / ") }))
})()

/** How many acts the legend names before collapsing the tail into « +N autres ». */
const LEGEND_MAX_ACTS = 10

/**
 * Today's date pill, in one place because it is painted by **four** surfaces — the week header, the day header,
 * the week strip and the phone month.
 *
 * ⚠️ `text-primary-foreground`, never `text-white`, which is what three of the four carried. In light mode the
 * two happen to agree; in dark `--primary` is a **bright** azure (L 0.72) and `--primary-foreground` is
 * near-black, so white-on-primary measured ≈2.2:1 — today's date, the single mark every reader of a calendar
 * looks for first, illegible on every dark-mode agenda. `globals.css` spells out why the token flipped when the
 * dark card was lifted off near-black; these three call sites had hardcoded past it.
 */
const TODAY_PILL = "bg-primary text-primary-foreground shadow-md"

/**
 * The three views as the **desktop/tablet** bar renders them, with the initial it falls back to below `lg:`.
 *
 * ⚠️ There are deliberately two lists of views in this feature, not one: `AgendaPhoneHeader` owns the phone's own
 * (full words, three 44 px targets in a row of their own — it has the width because it has nothing else on that
 * row). Sharing one list would mean sharing `short`, which is meaningless there. What must not drift is the
 * *labels*, and both read « Jour / Semaine / Mois » verbatim.
 */
const AGENDA_VIEW_OPTIONS: { value: "day" | "week" | "month"; label: string; short: string }[] = [
  { value: "day", label: "Jour", short: "J" },
  { value: "week", label: "Semaine", short: "S" },
  { value: "month", label: "Mois", short: "M" },
]

interface AppointmentCalendarProps {
  view: "day" | "week" | "month"
  selectedDate: Date
  onDateChange: (date: Date) => void
  /**
   * Open the create dialog on a slot.
   *
   * ⚠️ `durationMinutes` arrives **only from a drag across hours** — a plain click still calls this with two
   * arguments and the dialog keeps its own default length. One prop rather than a second « onTimeSpanSelect »,
   * because the two are the same request with one extra fact, and a caller that wired only one of them would have
   * a grid where clicking books and dragging does nothing.
   */
  onTimeSlotClick?: (date: Date, time: string, durationMinutes?: number) => void
  onAppointmentClick?: (appointment: AppointmentDto) => void
  /** Month view only: clicking a day cell's empty area / "+N more" focuses that date in Day view (AC-4). */
  onSelectDay?: (date: Date) => void
  showCancelled?: boolean
  showCompleted?: boolean
  /**
   * Lets the phone header switch view (< md:). Optional: when omitted the phone header is not rendered at all,
   * so an embedding that has no view switcher keeps its current behaviour. Pass the page's `selectView`, never a
   * bare `setView` — the page marks the view DECIDED so its narrow-screen Jour default stops re-asserting and
   * discarding the view the user just chose.
   */
  onViewChange?: (view: "day" | "week" | "month") => void
  onShowCancelledChange?: (show: boolean) => void
  onShowCompletedChange?: (show: boolean) => void
  /** Called after a per-card "Push to Google" succeeds so the parent can refetch (clears the badge). */
  onChanged?: () => void
  /** Optional per-practitioner filter (AC-3.2) — only appointments assigned to this doctor. */
  doctorId?: string
  /**
   * Opens the create dialog from the agenda bar. Optional: when omitted the bar simply has no primary action —
   * an embedding that provides its own must not get two.
   */
  onNewAppointment?: () => void
  /**
   * The praticien filter's data and setter. `doctorId` above is the *applied* value the fetch uses; this is what
   * the control needs to render. Passed as one object rather than three props so that a caller cannot supply the
   * list without the setter (a Select that cannot be changed) or the setter without the list (an empty Select) —
   * the two failure modes of the three-prop version.
   */
  doctorFilter?: {
    doctors: DoctorDto[]
    value: string
    onChange: (value: string) => void
  }
  /**
   * The clinic's Google Agenda connection, as the « ⋯ » menu needs it. **Only pass it for an admin** — the
   * connect/disconnect endpoints are `AdminOnly`, and rendering the actions for anyone else buys a 403 and a
   * generic « Échec » toast (finding #9). Omitted entirely, the menu holds Exporter alone.
   *
   * ⚠️ There is no import action: « Importer depuis Google » was retired, so this pushes only. The sync is
   * one-way by design now — see `features/calendar-import-revert/notes.md`.
   */
  googleControls?: {
    authorized: boolean
    onConnect: () => void
    onDisconnect: () => void
  }
  /**
   * Bump to refetch the current window **in place**. Replaces the old `key={refreshKey}` remount, which threw
   * away scroll position and flashed the grid empty every time anyone in the clinic touched an appointment.
   */
  reloadToken?: unknown
}

export function AppointmentCalendar({ view, selectedDate, onDateChange, onTimeSlotClick, onAppointmentClick, onSelectDay, showCancelled = false, showCompleted = false, onShowCancelledChange, onShowCompletedChange, onChanged, doctorId, reloadToken, onViewChange, onNewAppointment, doctorFilter, googleControls }: AppointmentCalendarProps) {
  const { internetReachable } = useConnectivity()
  // `md:` — the same boundary the rest of this feature splits devices at. Declared here rather than beside the
  // scroll refs because the fetch window below reads it: Mois on a phone spans several months, not one grid.
  const isNarrow = useMediaQuery("(max-width: 767px)")
  /**
   * A finger, not a mouse — which decides whether the two grid gestures arm on a long press or on movement.
   *
   * ⚠️ The **pointer**, never `isNarrow`: a chairside tablet is 1180 px and finger-operated, so a width test would
   * start a drag on contact there and take the scroll away on the one device this product is used on most. The
   * hook reads `false` until `matchMedia` answers, which errs toward the mouse behaviour for one frame — harmless,
   * because nothing is armed until a pointer actually goes down.
   */
  const coarsePointer = useMediaQuery(COARSE_POINTER_QUERY)

  /**
   * Has `matchMedia` answered yet?
   *
   * ⚠️ `useMediaQuery` returns `false` during SSR **and on the first client render** — its own docstring says so,
   * and says callers must read that `false` as "not yet known", never as "definitely a mouse". Four things here
   * key off `isNarrow`, and all four are *geometry*: `hourHeight`, `gutterPx`, `dayGridCols`, and the
   * `view === "week" && isNarrow` branch that swaps the whole time grid for the week strip. So the first frame a
   * phone painted was 48 px hour rows, a 60 px gutter and a 732 px-wide week grid inside a 358 px viewport — the
   * desktop agenda, for one frame, on the smallest screen.
   *
   * A CSS-variable fix (declaring the two heights as custom properties with a `@media` override) would cover the
   * first three and **cannot** cover the fourth: week-strip-versus-time-grid is a different component tree, not a
   * different value, and this component may not touch `globals.css`. So the gate is a mount flag and a neutral
   * skeleton — nothing is painted at a geometry that might be wrong, and the skeleton is one the app already
   * shows on every first load anyway.
   */
  const [mounted, setMounted] = useState(false)
  useEffect(() => {
    setMounted(true)
  }, [])

  /**
   * How many months past the selected one the phone's Mois view currently renders — and therefore fetches.
   * Reset whenever the selected month changes, so navigating months never inherits a previous scroll's span.
   */
  const [monthsAhead, setMonthsAhead] = useState(PHONE_MONTH_AHEAD_INITIAL)
  const selectedMonthKey = format(selectedDate, "yyyy-MM")
  useEffect(() => {
    setMonthsAhead(PHONE_MONTH_AHEAD_INITIAL)
  }, [selectedMonthKey])

  /**
   * The clinic's saved working hours, used to shade closed periods (AC-P1.33).
   *
   * The shading was `hour >= 8 && hour < 18` — hardcoded, and blind to the day of week, so Sunday shaded
   * identically to Monday and a clinic open 07:00–20:00 saw its real hours greyed out. Now read from the
   * clinic's own configuration; null/absent means nothing is configured, which per AC-P1.30 is *unrestricted*,
   * so nothing is shaded rather than everything.
   *
   * NOTE: the grid still renders 0..23 rows. Trimming it to the open hours only would require re-basing the
   * appointment overlay, which positions blocks from midnight against the fixed `HOUR_HEIGHT` (see the
   * load-bearing comment below) — deliberately left for a follow-up rather than risked here.
   */
  const [clinicHours, setClinicHours] = useState<WorkingDay[] | null>(null)
  useEffect(() => {
    let cancelled = false
    clinicsApi
      .getUserStatus()
      .then((status) => {
        if (!cancelled) setClinicHours(status.clinic?.workingHours ?? null)
      })
      // Best-effort: a failure means no shading, never a broken calendar.
      .catch(() => {
        if (!cancelled) setClinicHours(null)
      })
    return () => {
      cancelled = true
    }
  }, [])
  /**
   * The practitioner's own hours when the agenda is filtered on one, else the clinic's (AC-P1.30's order).
   *
   * ⚠️ The grid read `clinicHours` alone, so a per-practitioner override was **enforced but never drawn**: give a
   * dentist Tuesday-only hours and her Monday column still looked open, with the refusal arriving at save time
   * after the slot had been chosen and the patient told. Enforcement the screen does not show is a trap, not a
   * rule.
   */
  const [doctorHours, setDoctorHours] = useState<WorkingDay[] | null>(null)
  useEffect(() => {
    const filtered = doctorId
    if (!filtered) {
      setDoctorHours(null)
      return
    }
    let cancelled = false
    doctorsApi
      .getWorkingHours(filtered)
      // An empty list means « pas d'horaires spécifiques » — the clinic's hours apply, so null, not [].
      .then((days) => {
        if (!cancelled) setDoctorHours(days.length > 0 ? days : null)
      })
      // Best-effort, exactly like the clinic read: a failure falls back to the clinic hours, never to a blank grid.
      .catch(() => {
        if (!cancelled) setDoctorHours(null)
      })
    return () => {
      cancelled = true
    }
  }, [doctorId])
  const effectiveHours = doctorHours ?? clinicHours
  const isDayOpenOnAgenda = useCallback(
    (day: Date) => effectiveHours === null || openWindowFor(day, effectiveHours) !== null,
    [effectiveHours],
  )

  // The "Push to Google" endpoint is AdminOnly — only admins get the action (finding #9); everyone still
  // sees the "non synchronisé" status badge.
  const { user } = useSession()
  const isAdmin = user?.role === "admin"
  const [pushingId, setPushingId] = useState<string | null>(null)

  const handlePushToGoogle = async (appointment: AppointmentDto) => {
    if (!internetReachable || pushingId) return
    setPushingId(appointment.id)
    try {
      await googleCalendarApi.syncAppointment(appointment.id)
      toast.success("Rendez-vous synchronisé avec Google Agenda")
      onChanged?.()
    } catch (error) {
      // Mid-request connectivity loss ⇒ ApiError(status === 0) (google client now routes via client.ts).
      if (error instanceof ApiError && error.status === 0) {
        toast.error("Connexion perdue", {
          description: "La connexion a été interrompue. Réessayez une fois la connexion rétablie.",
        })
      } else {
        toast.error("Échec de la synchronisation", {
          description: error instanceof Error ? error.message : undefined,
        })
      }
    } finally {
      setPushingId(null)
    }
  }

  // "non synchronisé" badge + manual "Push to Google" for real appointments not yet in Google Calendar
  // (AC-6.6, FR-D4). Skips busy slots (no patient — never synced) and already-synced appointments.
  const renderSyncControls = (appointment: AppointmentDto) => {
    if (appointment.isSyncedToGoogle) return null
    if (isBusySlot(appointment)) return null
    // Cancelled/completed appointments intentionally carry no Google event (the sync service deletes
    // it); don't advertise "push to Google" for them — the badge is for appointments not yet synced
    // (e.g. created offline), per AC-6.6.
    const status = appointment.status.toLowerCase()
    if (status === "cancelled" || status === "completed") return null

    return (
      <div
        className="mt-0.5 flex items-center gap-1 flex-shrink-0"
        onClick={(e) => e.stopPropagation()}
        {...{ [AGENDA_NO_DRAG_ATTR]: "" }}
      >
        <Badge
          variant="secondary"
          className="border-0 bg-warning-wash text-warning-ink h-4 gap-0.5 px-1 text-2xs leading-none"
        >
          <CloudOff className="h-2.5 w-2.5" />
          non synchronisé
        </Badge>
        {isAdmin && (
          <button
            type="button"
            onClick={() => handlePushToGoogle(appointment)}
            disabled={!internetReachable || pushingId === appointment.id}
            title={internetReachable ? "Envoyer vers Google Agenda" : "Connexion internet requise"}
            aria-label={
              internetReachable
                ? `Envoyer le rendez-vous de ${appointment.patientName} vers Google Agenda`
                : "Connexion internet requise pour envoyer vers Google Agenda"
            }
            className="inline-flex h-4 items-center gap-0.5 rounded bg-white/60 px-1 text-2xs leading-none hover:bg-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50 dark:bg-background/60"
          >
            <UploadCloud className="h-2.5 w-2.5" />
            {/* AC-P3.49 — « Push » was the audit's own example. « Envoyer » at this size, with the full
                sentence in the title/aria-label. */}
            {pushingId === appointment.id ? "…" : "Envoyer"}
          </button>
        )}
      </div>
    )
  }
  // Memoized date range for API calls. Month view fetches the full visible grid (first visible day →
  // last visible day = start of the first week through the end of the fixed 6-row grid) so leading/
  // trailing days from adjacent months are populated too (AC-3).
  const startDate = useMemo(() => {
    if (view === "day") return startOfDay(selectedDate)
    if (view === "month") return startOfDay(startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 }))
    return startOfDay(startOfWeek(selectedDate, { weekStartsOn: 1 }))
  }, [view, selectedDate])

  const endDate = useMemo(() => {
    if (view === "day") return endOfDay(selectedDate)
    if (view === "month") {
      // The phone's Mois scrolls on into the following months, so the window is whatever it currently renders —
      // the dots and the weeks have to come from one range or a scrolled-to month reads as an empty one.
      if (isNarrow) {
        return endOfDay(
          endOfWeek(endOfMonth(addMonths(startOfMonth(selectedDate), monthsAhead)), { weekStartsOn: 1 }),
        )
      }
      return endOfDay(addDays(startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 }), 41)) // 6 rows × 7 - 1
    }
    return endOfDay(addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), 6)) // 7 days (0-6)
  }, [view, selectedDate, isNarrow, monthsAhead])

  /*
   * `error` used to be dropped on the floor here. A failed fetch therefore rendered a perfectly normal, empty
   * calendar — indistinguishable from a genuinely free day. In a clinic that is the one wrong answer that
   * matters: « rien cet après-midi » when in fact the server never answered. It is surfaced as a banner over
   * the grid (which keeps whatever rows were already loaded) with a Réessayer, following the same pattern
   * `dashboard-section.tsx` uses and the same lesson the procedure-type list in the create dialog documents.
   */
  const { appointments: allAppointments, loading, refetching, error, refetch } = useAppointments(
    startDate,
    endDate,
    undefined,
    undefined,
    doctorId,
    reloadToken,
  )

  // Filter appointments based on status filters
  const appointments = useMemo(() => {
    return allAppointments.filter(apt => {
      // A « créneau occupé » is exempt from both switches. `AppointmentProgressJob` closes a blocked hour once
      // its slot has ended, so honouring « Terminés » here would make every past block disappear from the
      // agenda — and a blocked hour is nobody's appointment, so an appointment-lifecycle filter is not about it
      // (`f441d1d`'s rule: figures about PEOPLE count appointments, figures about TIME count every slot).
      if (isBusySlot(apt)) {
        return true
      }
      const status = apt.status.toLowerCase()
      if (status === 'cancelled') {
        return showCancelled
      }
      if (status === 'completed') {
        return showCompleted
      }
      // By default, show scheduled, confirmed, inprogress, awaitingclosure, noshow
      return true
    })
  }, [allAppointments, showCancelled, showCompleted])

  /**
   * The visible appointments indexed by their day, earliest first within a day.
   *
   * ⚠️ Not a convenience. The phone's Mois renders up to twelve months of day cells, and `getAppointmentsForDay`
   * is a full scan of the window per cell — 84 cells over a quarter's appointments is a scan per row of a table
   * nobody paged. One pass builds the index the cells then read in O(1).
   */
  const appointmentsByDay = useMemo(() => {
    const byDay = new Map<string, AppointmentDto[]>()
    for (const appt of appointments) {
      const key = format(new Date(appt.appointmentDateTime), "yyyy-MM-dd")
      const bucket = byDay.get(key)
      if (bucket) bucket.push(appt)
      else byDay.set(key, [appt])
    }
    for (const bucket of byDay.values()) {
      bucket.sort(
        (a, b) => new Date(a.appointmentDateTime).getTime() - new Date(b.appointmentDateTime).getTime(),
      )
    }
    return byDay
  }, [appointments])

  /**
   * The weeks the phone's Mois renders: from the week containing the 1st of the selected month, on through the
   * last week of the `monthsAhead`-th month after it. **Weeks, not month pages** — a boundary week belongs to two
   * months and rendering it once (with a heading where the new month starts) is what makes the scroll continuous
   * instead of a stack of grids that each repeat their neighbours' days.
   */
  const phoneMonthWeeks = useMemo(() => {
    const first = startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 })
    const last = endOfWeek(endOfMonth(addMonths(startOfMonth(selectedDate), monthsAhead)), { weekStartsOn: 1 })
    const weeks: Date[][] = []
    for (let cursor = first; cursor <= last; cursor = addDays(cursor, 7)) {
      weeks.push(Array.from({ length: 7 }, (_, i) => addDays(cursor, i)))
    }
    return weeks
  }, [selectedDate, monthsAhead])

  // The 42 day cells (fixed 6 rows × 7 columns, weeks start Monday) covering the selected month plus
  // the leading/trailing days that fill the first/last weeks. Fixed length keeps the grid height stable
  // across months (Edge Cases: variable week count).
  const monthGridDays = useMemo(() => {
    const gridStart = startOfWeek(startOfMonth(selectedDate), { weekStartsOn: 1 })
    return Array.from({ length: 42 }, (_, i) => addDays(gridStart, i))
  }, [selectedDate])

  const scrollContainerRef = useRef<HTMLDivElement>(null)
  const [currentTime, setCurrentTime] = useState(new Date())
  const [currentTimePosition, setCurrentTimePosition] = useState<number | null>(null)

  // Where the hour grid starts inside the positioned wrapper — i.e. the sticky day header's height. Blocks are
  // placed by arithmetic from that wrapper, so without it every one paints a header too high (15:00 at ~13:30).
  const hourGridRef = useRef<HTMLDivElement>(null)
  const [hourGridOffsetTop, setHourGridOffsetTop] = useState(0)

  /**
   * Everything the day grid's geometry is measured against, in one place, because the phone and the desktop no
   * longer agree on any of it — and three of these numbers used to be literals scattered across the JSX.
   *
   * ⚠️ `gutterPx` is an arithmetic contract with `dayBandLeftExpr`/`dayBandWidthExpr` AND with the current-time
   * overlay's `left`, which are computed in three different places. Changing one alone drifts the blocks or the
   * red line sideways — the same failure the `WEEK_COLS` note describes for the week grid.
   */
  // The floor, not the rendered height: on a wide screen `hourHeight` below stretches it to fill the scrollport.
  const baseHourHeight = isNarrow ? HOUR_HEIGHT_PHONE : HOUR_HEIGHT
  const gutterPx = isNarrow ? 48 : 60
  const dayGridCols = isNarrow ? "grid-cols-[48px_1fr]" : "grid-cols-[60px_1fr]"

  /**
   * The days the time grid is currently showing — one in Jour, seven in Semaine. Shared by the window below and
   * by `initialScrollHour`, which used to build this array itself.
   */
  const gridDays = useMemo(
    () =>
      view === "week"
        ? Array.from({ length: 7 }, (_, i) => addDays(startOfWeek(selectedDate, { weekStartsOn: 1 }), i))
        : [selectedDate],
    [view, selectedDate],
  )

  /** Has the user asked for the whole twenty-four hours? A per-session escape hatch, never persisted. */
  const [showFullDay, setShowFullDay] = useState(false)

  /**
   * The wall clock, reduced to the two facts the grid window needs — see the `⚠️ nowHour` note in `gridWindow`.
   * Primitives on purpose: `currentTime` is a new `Date` every minute and would rebuild the window (and re-run the
   * scroll effect) with it.
   */
  const nowHour = currentTime.getHours()
  const todayIsInGrid = gridDays.some((day) => isSameDay(day, currentTime))

  /**
   * **The hours the grid actually renders**, and the fix for the audit's second finding.
   *
   * The grid used to render `0..23` unconditionally and merely *shade* the closed hours — so a clinic open
   * 08:00–18:00 still paid for thirteen rows it never books, the scroll bar was two-thirds dead travel, and
   * because the initial scroll centres on "now", the morning was permanently above the fold. The comment that
   * used to sit on `clinicHours` said trimming was "deliberately left for a follow-up rather than risked here",
   * naming the risk exactly: the overlay positions blocks by arithmetic from midnight. That is what
   * `gridWindow.fromHour` now subtracts, in the two places that do the arithmetic
   * (`renderAppointmentBlock`'s `top`, and the scroll effect's fallback) — the current-time line needs no change
   * because it asks the DOM where its hour row is.
   *
   * ⚠️ **The window is a union, not a filter, and that is § 0 of the device contract**: it covers the clinic's
   * configured hours *and every appointment actually booked in the visible days*, so a 06:30 emergency or a visit
   * running past closing **extends** the grid rather than being hidden by it. No layout decision may remove a
   * capability — and an appointment the agenda declines to draw is the worst version of that, because the screen
   * answers « rien » about a patient who is coming.
   *
   * ⚠️ Whole hours, both ends. A visit ending at 18:20 pushes `toHour` to 19, because a row is an hour: stopping
   * at 18 would clip the last twenty minutes of a block that is drawn against the row height.
   */
  const gridWindow = useMemo(() => {
    if (showFullDay) return { fromHour: 0, toHour: 24 }

    let from = 24
    let to = 0
    for (const day of gridDays) {
      const open = openWindowFor(day, effectiveHours)
      if (!open) continue
      from = Math.min(from, open.fromHour)
      to = Math.max(to, open.toHour)
    }
    // Nothing configured (or every visible day closed): an unrestricted clinic gets the default frame, which the
    // booked appointments below then widen if they need to.
    if (from >= to) {
      from = DEFAULT_GRID_FROM_HOUR
      to = DEFAULT_GRID_TO_HOUR
    }

    for (const day of gridDays) {
      for (const appointment of appointmentsByDay.get(format(day, "yyyy-MM-dd")) ?? []) {
        const start = new Date(appointment.appointmentDateTime)
        const startMinutes = start.getHours() * 60 + start.getMinutes()
        const endMinutes = startMinutes + Math.max(parseDurationToMinutes(appointment.duration), 1)
        from = Math.min(from, start.getHours())
        to = Math.max(to, Math.ceil(endMinutes / 60))
      }
    }

    /*
     * ⚠️ **The current hour is part of the union too, and leaving it out silently deleted the « maintenant » line.**
     *
     * The line is positioned by asking the DOM for the row of the current hour (`[data-time-slot="18:00"]`), which
     * is what lets it re-base itself for free when the window moves. Trimming the grid made that row *conditional*:
     * this clinic opens 09:00–17:00, so from 17:00 (or before 09:00) the query found nothing, and the honest
     * `setCurrentTimePosition(null)` that a trimmed grid requires then removed the line altogether — every evening,
     * on the one mark that says where "now" is.
     *
     * So « maintenant » widens the window exactly the way a booked appointment does. `nowHour + 1` because a row is
     * an hour and the line sits *inside* it: at 18:39 the grid must render the 18:00 row for the line to have
     * somewhere to be.
     *
     * Only when today is actually on screen — extending a week in March because the wall clock says 18:39 would
     * add a row nothing can occupy.
     */
    if (todayIsInGrid) {
      from = Math.min(from, nowHour)
      to = Math.max(to, nowHour + 1)
    }

    return { fromHour: Math.max(0, from), toHour: Math.min(24, Math.max(to, from + 1)) }
    /*
     * ⚠️ `nowHour`, not `currentTime`. The clock state is a fresh `Date` every minute, so depending on it would
     * rebuild this object sixty times an hour — and two effects key off the window, one of them the initial scroll.
     * A once-a-minute `scrollTo` would drag the agenda back to the opening hour under the reader's eye. An hour
     * number changes twenty-four times a day, which is the real rate at which this answer changes.
     */
  }, [showFullDay, gridDays, effectiveHours, appointmentsByDay, todayIsInGrid, nowHour])

  /** The rendered hour rows, as their `HH:00` labels. */
  const visibleHours = useMemo(
    () =>
      Array.from({ length: gridWindow.toHour - gridWindow.fromHour }, (_, i) =>
        hourSlot(gridWindow.fromHour + i),
      ),
    [gridWindow],
  )

  /** Is the grid currently showing less than the whole day? Drives the « afficher les 24 heures » disclosure. */
  const isTrimmed = gridWindow.fromHour > 0 || gridWindow.toHour < 24

  /** The scrollport's own height, measured — the grid has no other way to know how much room it was given. */
  const [gridViewportHeight, setGridViewportHeight] = useState(0)

  /*
   * A trimmed window is usually SHORTER than the screen, and the leftover was painted as blank card: a clinic
   * open 09:00–17:00 is 8 rows = 384 px at `HOUR_HEIGHT` inside a ~640 px scrollport, so a third of the agenda
   * was white below the last hour — which reads as a rendering fault rather than as "the day is over". Growing
   * this one number is enough, because rows, blocks, the « maintenant » line and the drag hook's own cell rects
   * are all derived from it.
   *
   * ⚠️ **It only ever grows.** Shrinking to fit would clamp a 24-hour window to ~25 px/hour, at which a
   * 30-minute visit is 12 px — the degraded rendering `HOUR_HEIGHT`'s own docstring exists to prevent. Past the
   * point where the rows no longer fit, the scrollport goes back to scrolling.
   *
   * ⚠️ **Desktop only.** A phone shows one day and the vertical axis IS its scroll axis, so `HOUR_HEIGHT_PHONE`
   * is tuned for a fingertip rather than for a screenful — stretching a three-hour window over 600 px there
   * would put one appointment on a screen.
   */
  const hourHeight = useMemo(() => {
    if (isNarrow || gridViewportHeight <= 0 || visibleHours.length === 0) return baseHourHeight
    const room = gridViewportHeight - hourGridOffsetTop
    return Math.max(baseHourHeight, Math.floor(room / visibleHours.length))
  }, [isNarrow, gridViewportHeight, hourGridOffsetTop, visibleHours.length, baseHourHeight])

  /* Re-measured on resize, not once on mount: the subscription banner and the filter-chips row both appear above
     this scrollport, and the deps are the four things that decide whether the ref points at anything at all. */
  useEffect(() => {
    const scroller = scrollContainerRef.current
    if (!scroller) return
    const measure = () => setGridViewportHeight(scroller.clientHeight)
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(scroller)
    return () => observer.disconnect()
  }, [view, isNarrow, mounted, loading])

  // Update current time every minute
  useEffect(() => {
    const interval = setInterval(() => {
      setCurrentTime(new Date())
    }, 60000) // Update every minute

    return () => clearInterval(interval)
  }, [])

  // Re-measured on everything that changes the header: the view (Jour and Semaine draw different ones), the
  // pointer class (a phone renders none) and the mount gate that swaps the skeleton for the real grid.
  useEffect(() => {
    setHourGridOffsetTop(hourGridRef.current?.offsetTop ?? 0)
  }, [view, isNarrow, mounted, loading, hourHeight])

  // Calculate current time position based on actual DOM elements (day/week only — month has no time grid)
  useEffect(() => {
    if (!loading && view !== "month" && scrollContainerRef.current) {
      const now = currentTime
      const hours = now.getHours()
      const minutes = now.getMinutes()
      const currentHourSlot = hourSlot(hours)

      // Find the actual DOM element for the current hour slot
      const timeSlotElement = scrollContainerRef.current.querySelector(`[data-time-slot="${currentHourSlot}"]`) as HTMLElement

      if (timeSlotElement) {
        const slotTop = timeSlotElement.offsetTop
        // Must be `hourHeight`, not a second copy of the number: this used to be a hardcoded `35`, so any
        // change to the row height silently drifted the current-time line away from the appointment blocks.
        const minuteOffset = (minutes / 60) * hourHeight
        const totalPosition = slotTop + minuteOffset
        setCurrentTimePosition(totalPosition)
      } else {
        // ⚠️ The `else` is required now that the grid is trimmed. The current hour is not always a rendered row
        // — 22:30 on a grid closing at 18:00 — and leaving the last computed value in place would paint the
        // « maintenant » line at whatever hour it last measured, i.e. a red line asserting a time that is hours
        // wrong. `isCurrentTimeVisible` cannot cover this: it answers about the *day*, not the hour.
        setCurrentTimePosition(null)
      }
    }
    // `mounted`: the hour rows this measures do not exist until `matchMedia` has answered and the real grid
    // replaces the neutral skeleton, so without it the line would wait up to a minute for the next tick.
    // The window's two **bounds**, not the object: the rows this measures are the trimmed ones, so opening
    // « les 24 heures » (or a late appointment, or the hour turning) moves them and the line must re-measure
    // rather than wait a tick. `toHour` is in here for the case this effect's `else` exists for — crossing 17:00
    // in a 09:00–17:00 clinic makes the current hour's row *appear*, and the line has to notice.
  }, [currentTime, loading, view, selectedDate, mounted, hourHeight, gridWindow.fromHour, gridWindow.toHour])

  // Check if current time is in the visible date range
  const isCurrentTimeVisible = useMemo(() => {
    const now = currentTime
    if (view === "day") {
      return format(now, "yyyy-MM-dd") === format(selectedDate, "yyyy-MM-dd")
    } else {
      const weekStart = startOfWeek(selectedDate, { weekStartsOn: 1 })
      const weekEnd = addDays(weekStart, 6)
      const nowDateOnly = format(now, "yyyy-MM-dd")
      const weekStartDateOnly = format(weekStart, "yyyy-MM-dd")
      const weekEndDateOnly = format(weekEnd, "yyyy-MM-dd")
      return nowDateOnly >= weekStartDateOnly && nowDateOnly <= weekEndDateOnly
    }
  }, [view, selectedDate, currentTime])


  /**
   * The hour the grid opens on when "now" is not in view: **the earlier of the clinic's opening hour and the
   * first appointment actually booked** in the visible range.
   *
   * ⚠️ It was a hardcoded `08:00`, in a component that already has both halves of the real answer in scope
   * (`openWindowFor` and `clinicHours`, added for the closed-hours shading). A clinic opening at 07:00 therefore
   * opened its agenda with the first hour of the day already scrolled off the top, and a 06:30 emergency slot
   * was invisible until someone scrolled up looking for something they had no reason to believe was there —
   * which is the same class of defect as the empty-state overlay below: the screen answering « rien » when it
   * simply has not shown you.
   */
  const initialScrollHour = useMemo(() => {
    let hour = DEFAULT_GRID_FROM_HOUR
    const openHours = gridDays
      .map((day) => openWindowFor(day, effectiveHours)?.fromHour)
      .filter((h): h is number => h !== undefined)
    if (openHours.length > 0) hour = Math.min(...openHours)

    for (const day of gridDays) {
      for (const appointment of appointmentsByDay.get(format(day, "yyyy-MM-dd")) ?? []) {
        hour = Math.min(hour, new Date(appointment.appointmentDateTime).getHours())
      }
    }
    // Clamped into the rendered window: with the grid trimmed this is normally its very first row, but under
    // « afficher les 24 heures » the window is 0..24 while the interesting hour is still the opening one — and a
    // scroll target that is not a rendered row would fall through to the arithmetic fallback below.
    return Math.max(gridWindow.fromHour, Math.min(gridWindow.toHour - 1, hour))
  }, [gridDays, effectiveHours, appointmentsByDay, gridWindow])

  // Initial scroll position (AC-1): when today falls in the visible range, center the current-time
  // line in the viewport (Google-Calendar-like); otherwise open on `initialScrollHour`.
  useEffect(() => {
    if (loading || view === "month") return

    /*
     * ⚠️ `behavior: "smooth"` is an explicit option, and the CSS `scroll-behavior: auto !important` that
     * `globals.css` sets under `prefers-reduced-motion` does **not** override it — per spec that rule only
     * decides what `behavior: "auto"` means. So reduced motion is honoured here, in JS, or this would be the
     * one animation in the app that ignores the setting.
     */
    const reducedMotion =
      typeof window !== "undefined" && window.matchMedia?.("(prefers-reduced-motion: reduce)").matches
    const behavior: ScrollBehavior = reducedMotion ? "auto" : "smooth"

    const positionScroll = () => {
      const container = scrollContainerRef.current
      if (!container || !container.querySelector('[data-time-slot]')) return false

      if (isCurrentTimeVisible) {
        const now = new Date()
        const currentHourSlot = `${String(now.getHours()).padStart(2, '0')}:00`
        const slotElement = container.querySelector(`[data-time-slot="${currentHourSlot}"]`) as HTMLElement | null
        if (slotElement) {
          const nowPosition = slotElement.offsetTop + (now.getMinutes() / 60) * hourHeight
          container.scrollTo({ top: Math.max(0, nowPosition - container.clientHeight / 2), behavior })
          return true
        }
      }

      /*
       * The opening hour at the top — read from the DOM, not computed as `hour * HOUR_HEIGHT`.
       *
       * ⚠️ That arithmetic silently assumed the hour grid began at the scroll container's top. Since AC-30
       * moved the day header INSIDE this scroller (it has to be, or it would not scroll sideways with its
       * columns), the grid now starts one header below — so the old expression landed ~8 AM *minus the
       * header* and the morning was cut off. Asking the row where it actually is cannot drift again,
       * whatever else is stacked above it. `offsetTop` is measured from the `relative` wrapper, which is the
       * scroller's content origin, so it is already in `scrollTop` coordinates.
       */
      const opening = container.querySelector(`[data-time-slot="${hourSlot(initialScrollHour)}"]`) as HTMLElement | null
      // ⚠️ The fallback is measured from the grid's FIRST RENDERED HOUR, not from midnight: since the grid is
      // trimmed to the clinic's window, `initialScrollHour * hourHeight` would scroll a whole opening-hour's worth
      // of rows too far — eight hours down an eleven-hour grid, i.e. straight to the bottom.
      container.scrollTo({
        top: opening ? opening.offsetTop : Math.max(0, initialScrollHour - gridWindow.fromHour) * hourHeight,
        behavior,
      })
      return true
    }

    if (!positionScroll()) {
      const timer = setTimeout(positionScroll, 150)
      return () => clearTimeout(timer)
    }
    // `isNarrow` is a dependency because the week time grid is not rendered at all below `md:` — crossing the
    // breakpoint mounts it for the first time, and without a re-run it would sit at midnight instead of the
    // opening hour. `mounted` is one for the same reason: nothing but a neutral skeleton exists until
    // `matchMedia` has answered, so this must run again once the real grid is in the DOM to measure.
    // `gridWindow.fromHour` keeps the *same hours* under the eye when rows are added above the ones being read
    // (switching to « les 24 heures »), which `scrollTop` alone would render as a jump into the middle of the night.
    //
    // ⚠️ **`fromHour`, never the `gridWindow` object.** The window is rebuilt every time the wall-clock hour turns
    // (« maintenant » is part of its union), so depending on the object would re-run this — a `scrollTo` on the
    // hour, every hour, dragging the agenda away from whatever the user had scrolled to. `toHour` is deliberately
    // absent for the same reason: rows appended at the bottom move nothing that is already on screen.
  }, [view, selectedDate, loading, isCurrentTimeVisible, isNarrow, mounted, hourHeight, initialScrollHour, gridWindow.fromHour])

  /**
   * All appointments starting on the given day (already status-filtered), **earliest first**. Each is rendered
   * exactly once by the overlay below (AC-4), replacing the old per-hour-slot duplication.
   *
   * ⚠️ The returned array is the index's own bucket, so callers must not `.sort()` it — that mutates the memo.
   * They no longer need to: the index sorts every bucket once, which is what the three callers each did by hand.
   */
  const getAppointmentsForDay = (date: Date): AppointmentDto[] =>
    appointmentsByDay.get(format(date, "yyyy-MM-dd")) ?? []

  // Horizontal band (as CSS calc expressions, without the wrapping `calc()`) for a day's appointment
  // column. Day view uses the full width right of the 60px time gutter; week view uses one of 7 equal
  // day columns. `laneStyle` splits this band into side-by-side lanes when appointments overlap.
  // Derived from `gutterPx`, never a second copy of it: a phone's gutter is 48px, and a hardcoded 64/70 there
  // left every block overhanging its own column by the difference.
  const dayBandLeftExpr = `${gutterPx + 4}px`
  const dayBandWidthExpr = `100% - ${gutterPx + 10}px`
  const weekBandLeftExpr = (dayIndex: number) => `60px + ((100% - 60px) / 7) * ${dayIndex} + 2px`
  const weekBandWidthExpr = `(100% - 60px) / 7 - 4px`

  // Split a day's column band into `colCount` equal side-by-side lanes; `colCount === 1` fills the band.
  const laneStyle = (leftExpr: string, widthExpr: string, colIndex: number, colCount: number): CSSProperties => {
    if (colCount <= 1) {
      return { left: `calc(${leftExpr})`, width: `calc(${widthExpr})` }
    }
    return {
      left: `calc((${leftExpr}) + (${widthExpr}) * ${colIndex} / ${colCount})`,
      width: `calc((${widthExpr}) / ${colCount} - 2px)`,
    }
  }

  /* ── The two grid gestures: drag empty hours to book a span, drag a block to move it ───────────────────── */

  /** `yyyy-MM-dd` → the `Date` of that grid column, so a gesture's day key resolves without re-parsing a string. */
  const gridDayByKey = useMemo(() => {
    const byKey = new Map<string, Date>()
    for (const day of gridDays) byKey.set(format(day, "yyyy-MM-dd"), day)
    return byKey
  }, [gridDays])

  /**
   * Where a provisional band sits — **the same arithmetic `renderAppointmentBlock` uses**, and reusing it rather
   * than re-deriving it is the point: a selection that painted a pixel off the block it is about to become would
   * read as the grid being imprecise about time.
   */
  const gridBandStyle = (dayKey: string, fromMinutes: number, spanMinutes: number): CSSProperties | null => {
    const dayIndex = gridDays.findIndex((day) => format(day, "yyyy-MM-dd") === dayKey)
    if (dayIndex < 0) return null
    const position =
      view === "week"
        ? laneStyle(weekBandLeftExpr(dayIndex), weekBandWidthExpr, 0, 1)
        : laneStyle(dayBandLeftExpr, dayBandWidthExpr, 0, 1)
    return {
      ...position,
      top: `${hourGridOffsetTop + ((fromMinutes - gridWindow.fromHour * 60) / 60) * hourHeight}px`,
      // A floor of 4 px so a zero-length selection (the drag has not left its first unit yet) is still a visible
      // hairline rather than nothing at all — the user is holding the pointer down and must see that it took.
      height: `${Math.max((spanMinutes / 60) * hourHeight, 4)}px`,
    }
  }

  /**
   * What an hour cell must carry for a drag to identify it — spread, never retyped per branch.
   *
   * `data-time-slot` rides along because the two things that read the grid from the DOM (the « maintenant » line
   * and the initial scroll) key on it, and Jour and Semaine render the cell from two different branches: writing
   * the set out twice is how one of them ends up with a cell the drag can see and the scroll cannot.
   */
  const cellDataProps = (day: Date, hour: number) => ({
    [AGENDA_CELL_ATTR]: "",
    "data-agenda-day": format(day, "yyyy-MM-dd"),
    "data-agenda-hour": hour,
    "data-time-slot": hourSlot(hour),
  })

  /** The appointment whose move is in flight — the block dims rather than pretending the new time is saved. */
  const [movingId, setMovingId] = useState<string | null>(null)
  /**
   * The refusal a drop met, if any: the server's own French sentence for the two advisory codes, ours for the
   * past-time case — which no backend refuses, so both booking dialogs already ask it client-side and this is the
   * third caller of the same question rather than a new rule.
   */
  const [movePrompt, setMovePrompt] = useState<{ kind: "overlap" | "hours" | "past"; message: string } | null>(null)
  const pendingMoveRef = useRef<{ appointment: AppointmentDto; start: Date } | null>(null)
  /**
   * ⚠️ **Merged, and a ref.** A single drop can trip all three — an emergency squeezed into an occupied slot on a
   * Saturday morning — and each confirmation re-sends, so granting one as a fresh boolean would un-grant the last
   * and loop. It is a ref because the confirmation re-submits in the same tick a state update would miss, which is
   * the trap `create-appointment-dialog.tsx` documents for the identical shape.
   */
  const moveGrantsRef = useRef({ hours: false, overlap: false, past: false })

  const submitMove = useCallback(async () => {
    const pending = pendingMoveRef.current
    if (!pending) return
    const { appointment, start } = pending

    if (!moveGrantsRef.current.past) {
      const nowFloored = new Date()
      nowFloored.setSeconds(0, 0)
      if (start.getTime() < nowFloored.getTime()) {
        setMovePrompt({
          kind: "past",
          message: "L'heure choisie est déjà passée. Voulez-vous quand même déplacer ce rendez-vous ?",
        })
        return
      }
    }

    setMovingId(appointment.id)
    try {
      /*
       * ⚠️ **Only the instant, the version and the granted overrides.** Every other key on this payload is
       * tri-state, and `durationMinutes`, `procedures` and `status` are each omitted on purpose: sending the acts
       * would replace them, sending the statut would re-assert whatever this render happens to hold (the L2a
       * defect `edit-appointment-dialog.tsx` names), and sending the duration would let a move quietly relengthen
       * a visit — AC-5 says a moved appointment keeps its own length.
       */
      /*
       * The version is re-read, not taken from the calendar's own list.
       *
       * A calendar can sit open for hours, and the version on this card is as old as the last refetch — so a
       * move would 409 over the clinic's own earlier writes (the post-commit Google Calendar push stamps
       * `GoogleCalendarEventId` after the response has gone out, which alone is enough to age every card).
       * Nothing is lost by refreshing it: the payload below carries only the new instant, so a colleague's
       * edit to any other field survives the move untouched.
       */
      const current = await appointmentsApi.get(appointment.id).catch(() => appointment)
      await appointmentsApi.update(appointment.id, {
        appointmentDateTime: start.toISOString(),
        version: current.version,
        allowOutsideWorkingHours: moveGrantsRef.current.hours || undefined,
        allowOverlap: moveGrantsRef.current.overlap || undefined,
      })
      pendingMoveRef.current = null
      toast.success("Rendez-vous déplacé", {
        description: `${appointment.patientName} · ${format(start, "EEEE d MMMM à HH:mm", { locale: fr })}`,
      })
      onChanged?.()
      void refetch()
    } catch (err) {
      if (err instanceof ApiError) {
        // Advisory as on create and on edit — and guarded on the grant, so a re-send that still refuses
        // surfaces as a real error instead of re-asking for ever.
        if (err.code === ApiErrorCode.SlotTaken && !moveGrantsRef.current.overlap) {
          setMovePrompt({ kind: "overlap", message: err.message })
          return
        }
        if (err.code === ApiErrorCode.OutsideWorkingHours && !moveGrantsRef.current.hours) {
          setMovePrompt({ kind: "hours", message: err.message })
          return
        }
        // A peer moved the same appointment first. The server's sentence says so; the refetch is what stops the
        // grid disagreeing with the database about a visit's time.
        if (err.status === 409) void refetch()
      }
      pendingMoveRef.current = null
      showErrorToast(err)
    } finally {
      setMovingId(null)
    }
  }, [onChanged, refetch])

  const handleMoveDrop = useCallback(
    (appointment: AppointmentDto, target: AgendaCellTarget) => {
      const day = gridDayByKey.get(target.dayKey)
      if (!day) return
      const start = new Date(day)
      start.setHours(0, 0, 0, 0)
      start.setMinutes(target.minutes)

      // Dropped back where it already was: no request at all (AC-7). Compared on the minute, because a
      // Google-synced appointment keeps its stored seconds and would otherwise never look equal to a snapped slot.
      const current = new Date(appointment.appointmentDateTime)
      current.setSeconds(0, 0)
      if (current.getTime() === start.getTime()) return

      pendingMoveRef.current = { appointment, start }
      moveGrantsRef.current = { hours: false, overlap: false, past: false }
      void submitMove()
    },
    [gridDayByKey, submitMove],
  )

  const handleCreateSpan = useCallback(
    (dayKey: string, startMinutes: number, durationMinutes: number) => {
      const day = gridDayByKey.get(dayKey)
      if (!day) return
      const time = `${String(Math.floor(startMinutes / 60)).padStart(2, "0")}:${String(startMinutes % 60).padStart(2, "0")}`
      onTimeSlotClick?.(day, time, durationMinutes)
    },
    [gridDayByKey, onTimeSlotClick],
  )

  const handleCellClick = useCallback(
    (dayKey: string, hour: number) => {
      const day = gridDayByKey.get(dayKey)
      if (day) onTimeSlotClick?.(day, hourSlot(hour))
    },
    [gridDayByKey, onTimeSlotClick],
  )

  const gridDrag = useAgendaGridDrag({
    // Mois has no hour cells and the phone's Semaine renders the strip instead, so there is nothing to drag
    // across in either; `loading` is in here because the cells this measures do not exist yet.
    enabled: mounted && !loading && view !== "month" && !(view === "week" && isNarrow),
    coarsePointer,
    containerRef: scrollContainerRef,
    // The rendered window and the row height are what every cell's position is made of — see the hook's note on
    // why a change cancels an in-flight drag rather than remapping it.
    geometryKey: `${view}-${gridWindow.fromHour}-${gridWindow.toHour}-${hourHeight}`,
    onCreateSpan: handleCreateSpan,
    onCellClick: handleCellClick,
    onMoveDrop: handleMoveDrop,
  })

  /** The provisional span's geometry, or null when nothing is being dragged (or the day left the grid mid-drag). */
  const createSelectionStyle = gridDrag.createSelection
    ? gridBandStyle(
        gridDrag.createSelection.dayKey,
        gridDrag.createSelection.fromMinutes,
        gridDrag.createSelection.toMinutes - gridDrag.createSelection.fromMinutes,
      )
    : null

  /**
   * Where the dragged block would land. Sized by the appointment's **own** duration, which is what makes the
   * preview honest: a move never changes how long a visit is (AC-5), so a ghost the height of the pointer's travel
   * would be advertising a resize this feature deliberately does not do.
   */
  const moveGhostStyle = gridDrag.moveDrag
    ? gridBandStyle(
        gridDrag.moveDrag.target.dayKey,
        gridDrag.moveDrag.target.minutes,
        Math.max(parseDurationToMinutes(gridDrag.moveDrag.appointment.duration), AGENDA_SNAP_MINUTES),
      )
    : null

  // Assign overlapping appointments to side-by-side columns (Google-Calendar style) so simultaneous
  // appointments share the slot width instead of stacking on top of each other. Appointments are packed
  // into the first free column (a column frees once its last appointment ends); each cluster of
  // chain-overlapping appointments gets `colCount` = the max columns it ever needed, so widths are
  // consistent within the cluster. Works for any number of overlaps. Non-overlapping appointments get
  // `colCount === 1` (full width). Order of the returned lanes is irrelevant (absolute-positioned).
  /**
   * `minutesToNextStart` is how far below this block the next one begins — `Infinity` when nothing follows.
   *
   * It rides along here because this is the one place that has already sorted the day, and the geometry needs
   * it: `MIN_APPT_HEIGHT(_PHONE)` is a floor applied to blocks that are *shorter than their neighbours are
   * apart*, so without knowing that distance the clamp paints one appointment over the next. See the constant's
   * own note — the two appointments in question never overlap **in time**, so no amount of laning separates
   * them; only not growing them does.
   */
  type AppointmentLane = {
    appointment: AppointmentDto
    colIndex: number
    colCount: number
    minutesToNextStart: number
  }
  const computeOverlapLanes = (dayAppointments: AppointmentDto[]): AppointmentLane[] => {
    const items = dayAppointments
      .map((a) => {
        const start = new Date(a.appointmentDateTime)
        const startMin = start.getHours() * 60 + start.getMinutes()
        const endMin = startMin + Math.max(parseDurationToMinutes(a.duration), 1)
        return { appointment: a, startMin, endMin }
      })
      .sort((x, y) => x.startMin - y.startMin || x.endMin - y.endMin)

    /*
     * The next *strictly later* start, per item, in one backward pass over the sorted list. Strictly later
     * matters: two appointments booked at the same minute are laned side by side, so they are not below each
     * other and must not shorten each other. Deliberately measured against the whole day rather than against
     * the item's own lane — a conservative distance can only ever keep a block at its true height, which is
     * the behaviour before this change, whereas a too-generous one paints over a neighbour.
     */
    const nextStart = new Array<number>(items.length).fill(Number.POSITIVE_INFINITY)
    for (let i = items.length - 2; i >= 0; i--) {
      nextStart[i] =
        items[i + 1].startMin > items[i].startMin ? items[i + 1].startMin : nextStart[i + 1]
    }
    const gapOf = new Map<AppointmentDto, number>()
    items.forEach((it, i) => gapOf.set(it.appointment, nextStart[i] - it.startMin))

    const lanes: AppointmentLane[] = []
    let cluster: { appointment: AppointmentDto; startMin: number; endMin: number }[] = []
    let colEnds: number[] = []
    const colOf = new Map<AppointmentDto, number>()
    let clusterMaxEnd = -1

    const flush = () => {
      const colCount = colEnds.length
      for (const it of cluster) {
        lanes.push({
          appointment: it.appointment,
          colIndex: colOf.get(it.appointment) ?? 0,
          colCount,
          minutesToNextStart: gapOf.get(it.appointment) ?? Number.POSITIVE_INFINITY,
        })
      }
      cluster = []
      colEnds = []
      colOf.clear()
      clusterMaxEnd = -1
    }

    for (const it of items) {
      // A gap with no active appointment closes the current cluster.
      if (cluster.length > 0 && it.startMin >= clusterMaxEnd) flush()
      let col = colEnds.findIndex((end) => end <= it.startMin)
      if (col === -1) {
        col = colEnds.length
        colEnds.push(it.endMin)
      } else {
        colEnds[col] = it.endMin
      }
      colOf.set(it.appointment, col)
      cluster.push(it)
      clusterMaxEnd = Math.max(clusterMaxEnd, it.endMin)
    }
    flush()
    return lanes
  }

  // Render an appointment as a single continuous block, positioned by its start minute and sized
  // proportionally to its true duration, with a minimum height for legibility (AC-4). The block is an
  // absolute child of the scroll container (like the current-time line), so it scrolls with content.
  const renderAppointmentBlock = (
    appointment: AppointmentDto,
    positionStyle: CSSProperties,
    minutesToNextStart: number,
  ) => {
    const aptStart = new Date(appointment.appointmentDateTime)
    const durationMinutes = parseDurationToMinutes(appointment.duration)
    const startMinutesOfDay = aptStart.getHours() * 60 + aptStart.getMinutes()
    // `hourGridOffsetTop`, not bare arithmetic: the sticky day header sits above the grid inside the same
    // positioned wrapper, and the current-time line already accounts for it by reading the DOM.
    //
    // ⚠️ `- gridWindow.fromHour * 60` is the whole of the grid-trimming fix. The grid no longer starts at midnight,
    // so a block positioned from midnight lands `fromHour` rows too low — a 09:00 visit painted at 17:00 on a grid
    // opening at 08:00. It is subtracted here and in the scroll fallback above, and **nowhere else**: the
    // current-time line asks the DOM for its row's `offsetTop`, so it re-bases itself for free.
    const minutesIntoGrid = startMinutesOfDay - gridWindow.fromHour * 60
    const top = hourGridOffsetTop + (minutesIntoGrid / 60) * hourHeight

    /*
     * ── The height arithmetic, which is the whole of fixes (9) and (10) ──────────────────────────────────────
     *
     *   natural   = duration/60 × hourHeight   — the honest height, what the hour ruler says
     *   floor     = 28 px on a phone, 18 px on a desk machine (see MIN_APPT_HEIGHT_PHONE)
     *   roomBelow = distance to the next appointment's START, from `computeOverlapLanes`
     *
     *   height    = max(natural, min(floor, roomBelow))
     *
     * The `min` is the load-bearing half. A 15-minute visit is 16 px at `HOUR_HEIGHT_PHONE` (12 px on a desktop)
     * and consecutive quarter-hour visits sit exactly that far apart, so a bare `max(natural, floor)` clamps the
     * 09:00 block to 28 px and paints 12 px of it over the 09:15 one. `computeOverlapLanes` cannot rescue that:
     * the two do not overlap **in time**, so they are never in one cluster and never get side-by-side lanes —
     * the defect looks like a rendering bug and is arithmetic. Clamping by the real room below means a short
     * visit grows to the floor when it is alone (the common case, and the reason for the floor) and keeps its
     * true 16 px when it has a neighbour (where growing it would lie about the schedule anyway).
     */
    const naturalHeight = (durationMinutes / 60) * hourHeight
    const heightFloor = isNarrow ? MIN_APPT_HEIGHT_PHONE : MIN_APPT_HEIGHT
    const roomBelow = Number.isFinite(minutesToNextStart)
      ? (minutesToNextStart / 60) * hourHeight
      : Number.POSITIVE_INFINITY
    const height = Math.max(naturalHeight, Math.min(heightFloor, roomBelow))

    const isVerySmall = height < 30
    const isSmall = height < 48
    /** Enough painted height for the sync badge row *under* the name and the time+act line — see its own note below. */
    const hasRoomForSync = height >= 62
    const actsSummary = appointmentActsSummary(appointment)
    const actsCount = appointmentActsCount(appointment)
    const colorStyle = appointmentAppearance(appointment)
    const tone = appointmentTone(appointment)
    const statusLabel = appointmentStatusLabel(appointment.status)

    /*
     * ── Drag-to-move, on the block ────────────────────────────────────────────────────────────────────────
     *
     * ⚠️ AC-9: a **cancelled or completed** visit is not draggable. Moving one asserts a change to something that
     * has already happened or has already been called off, and both are corrected through the edit dialog — which
     * the block still opens on click, so this removes no capability (§ 0).
     *
     * ⚠️ `onClick` stays, guarded, rather than the gesture claiming the tap: the phone branch below is a real
     * `<button>`, so Enter and Space produce a `click` and **no pointer events at all**. What a click cannot know
     * on its own is whether a drag preceded it — browsers synthesise one after a pointer release — hence the
     * guard, without which moving an appointment would also re-open its dialog on top of the grid.
     *
     * ⚠️ Every block goes `pointer-events-none` while any drag is painting, not just the dragged one. The drop
     * target is resolved with `elementFromPoint`, and an appointment block is a sibling of the hour grid rather
     * than a child of a cell — so a block left interactive is a hole in the grid that a drop cannot land on.
     */
    const canonicalStatus = normalizeStatus(appointment.status)
    const isDraggable = canonicalStatus !== "Cancelled" && canonicalStatus !== "Completed"
    const isBeingDragged = gridDrag.moveDrag?.appointment.id === appointment.id
    const isBeingSaved = movingId === appointment.id
    const onBlockPointerDown = isDraggable
      ? (event: React.PointerEvent<HTMLElement>) =>
          gridDrag.beginAppointmentGesture(event, appointment, {
            dayKey: format(aptStart, "yyyy-MM-dd"),
            minutes: startMinutesOfDay,
          })
      : undefined
    const onBlockClick = (event: React.MouseEvent) => {
      event.stopPropagation()
      if (gridDrag.didConsumeGesture()) return
      onAppointmentClick?.(appointment)
    }
    const dragClasses = cn(
      gridDrag.dragActive && "pointer-events-none",
      isBeingDragged && "opacity-40",
      // In flight, and deliberately still at its OLD position: the grid must never show a time that was not
      // saved (AC-8), so the block dims where it is and moves only once the refetch confirms it.
      isBeingSaved && "opacity-60",
    )
    // The name a screen reader (and the hover tooltip) gets. Status is in it because status is now *paint* on
    // the block — a struck-through, dashed or ringed block states something, and until this it stated it to
    // sighted users only.
    const blockLabel = `${appointment.patientName} · ${format(aptStart, "HH:mm")} · ${durationMinutes} min · ${statusLabel}${
      actsSummary ? ` · ${actsSummary}` : ""
    }`

    /*
     * The phone block is a different shape, not the desktop block scaled down.
     *
     * A 390px-wide block has room for one line, and everything the desktop puts on that line competes with the
     * patient's name for it: a duration badge (which the block's own HEIGHT already states), an acts badge, a
     * « non synchronisé » badge and an « Envoyer » button. That row is most of what made Jour feel crowded, and
     * the Google-Agenda controls in particular are an admin errand — not something a dentist reaches for while
     * looking at this afternoon. So on a phone a block says: who, and — when it is tall enough to have a second
     * line — from when, plus the acts. Both remain fully available from `md:` up, and tapping the block still
     * opens the edit dialog where the sync state is shown.
     *
     * ⚠️ A real `<button>`, unlike its desktop twin. It carries no nested control here, so it can be one — and
     * an appointment is a thing you should be able to reach with a keyboard. The desktop block hosts the
     * « Envoyer » button, so making *that* one a button would nest two buttons (invalid DOM), and `role="button"`
     * would be worse still: it makes its descendants presentational and would hide « Envoyer » from AT entirely.
     *
     * ⚠️ Deliberately **no `touch-target`**. The utility overlays a 44 px `::after`, and this element is
     * `overflow-hidden` (it has to be — that is what clips the second line), so the overlay would be clipped to
     * the block's own box and buy exactly nothing. With side-by-side lanes it would also straddle a neighbour.
     * The 28 px floor above is real painted pixels instead, which is a hit area that actually exists.
     */
    if (isNarrow) {
      // Below 40 px there are two lines to fit in ~32 px, so both tighten their leading. Left at 1.3 the pair
      // measures ~30 px inside a 32 px block and the second line is clipped — which is how a 30-minute visit
      // ended up showing only a name on a row height that was doubled to give it two.
      const tightLines = height < 40
      return (
        <button
          key={appointment.id}
          type="button"
          className={cn(
            "pointer-events-auto absolute z-20 flex cursor-pointer flex-col overflow-hidden rounded-md text-start transition-[box-shadow,transform] duration-[160ms] ease-snap active:scale-[0.99]",
            colorStyle.className,
            tightLines ? "px-1.5 py-0" : "px-1.5 py-0.5",
            // Clearance for the 3px statut strip. After the padding shorthand, so tailwind-merge lets it win on
            // the inline-end side only.
            "pe-2",
            dragClasses,
          )}
          style={{ top: `${top}px`, height: `${height}px`, ...positionStyle, ...colorStyle.style }}
          onPointerDown={onBlockPointerDown}
          onClick={onBlockClick}
          title={blockLabel}
          aria-label={blockLabel}
        >
          {/* The statut, as a full-height strip on the opposite edge from the act's rail. It is the only status
              treatment that survives a 12px block, which on a phone's 64px hour is a quarter-hour visit. The
              label already carries the status in words for a screen reader (`blockLabel`), so this is
              `aria-hidden` decoration. */}
          {renderStatusEdge(appointment)}
          <span className="flex min-w-0 items-center gap-1">
            {tone === "negative" && <UserX className="h-3 w-3 shrink-0" aria-hidden="true" />}
            <span
              className={cn(
                "min-w-0 truncate font-semibold",
                isVerySmall || tightLines ? "text-2xs leading-[1.15]" : "text-xs leading-[1.3]",
              )}
            >
              {appointment.patientName}
            </span>
          </span>
          {/* 30, not 40. A 30-minute visit — the default — is exactly 32 px at `HOUR_HEIGHT_PHONE`, so a 40 px
              gate excluded the single most common appointment in the app from ever showing its start time, and
              the taller phone row bought the standard case nothing at all. */}
          {height >= 30 && (
            <span
              className={cn(
                "truncate text-2xs opacity-75",
                tightLines ? "leading-[1.15]" : "leading-[1.3]",
              )}
            >
              {format(aptStart, "HH:mm")}
              {actsSummary ? ` · ${actsSummary}` : ""}
            </span>
          )}
        </button>
      )
    }

    return (
      <div
        key={appointment.id}
        className={cn(
          // Press feedback on the block itself: it is the primary click target of the whole screen and had
          // none. Kept at 0.99 because the block is absolutely positioned against its neighbours — anything
          // stronger reads as the appointment jumping out of its slot rather than being pressed.
          "pointer-events-auto absolute z-20 flex flex-col overflow-hidden rounded transition-[box-shadow,transform] duration-[160ms] ease-snap hover:shadow-md active:scale-[0.99]",
          // A grab cursor is the only affordance a mouse gets for the move gesture, and it is honest: a block that
          // cannot be dragged (annulé, terminé) keeps the plain pointer rather than promising one.
          isDraggable ? (isBeingDragged ? "cursor-grabbing" : "cursor-grab") : "cursor-pointer",
          colorStyle.className,
          isVerySmall ? "px-1 py-0" : isSmall ? "p-1" : "p-1.5",
          // Clearance for the 3px statut strip — after the padding shorthand so tailwind-merge lets it win on
          // the inline-end side alone. The quick-status chevron sits at that end and must not straddle it.
          "pe-2",
          dragClasses,
        )}
        style={{ top: `${top}px`, height: `${height}px`, ...positionStyle, ...colorStyle.style }}
        onPointerDown={onBlockPointerDown}
        onClick={onBlockClick}
        title={blockLabel}
        aria-label={blockLabel}
      >
        {/* The statut strip — see `renderStatusEdge`. `aria-hidden`: `blockLabel` already states the status in
            words, so this adds a channel for the eye and nothing for a screen reader to repeat. */}
        {renderStatusEdge(appointment)}
        {/*
          ── What a block says, and why the duration badge is gone ────────────────────────────────────────────
          A one-hour visit in Semaine is 48 px tall and about 120 px wide. It used to spend that width on the
          patient's name — truncated to « oumayma be… » — plus a `60m` badge, and the badge is the one token on it
          that carries **no** information: the block's own HEIGHT states the duration, measured against the hour
          ruler two centimetres to its left. The phone branch above already says exactly that in its own comment
          and drops the badge for that reason; the desktop kept it, so the desktop paid ~34 px of its scarcest
          axis to repeat what the geometry already said, and the name lost the room.

          What replaces it is what the week grid genuinely could not tell you: **the start time and the act**.
          The start was desktop-only-missing (the phone branch shows it), and the act name was reserved for Jour —
          so in Semaine « qu'est-ce que je fais à 14 h ? » had no answer without opening the appointment.

          ⚠️ The second line is `truncate`, never wrapped: a wrapped act name grows the text past a block sized by
          duration, and the overflow is clipped mid-word rather than ellipsised — which reads as a rendering fault.
        */}
        <div className="flex min-w-0 items-center gap-1.5">
          {tone === "negative" && <UserX className="h-3 w-3 shrink-0" aria-hidden="true" />}
          <span
            className={cn(
              "min-w-0 flex-1 truncate font-semibold",
              isVerySmall ? "text-2xs leading-[1.1]" : isSmall ? "text-xs leading-[1.2]" : "text-sm leading-[1.3]",
            )}
          >
            {appointment.patientName}
          </span>
          {/* The multi-act signal stays a badge: it is a *count*, so it must not be mistaken for the act's name,
              and it is the one thing a grouped séance cannot say any other way at this width. */}
          {!isVerySmall && actsCount > 1 && (
            <Badge
              variant="secondary"
              className="h-4 shrink-0 border-0 bg-white/50 px-1.5 text-2xs leading-none dark:bg-background/50"
              title={actsSummary ?? undefined}
            >
              {actsCount} actes
            </Badge>
          )}
          {/*
            AC-24: advance the visit's statut — or delete a séance booked by mistake — without opening the edit
            dialog.

            ⚠️ Gated on the block having the room, not on the pointer. A block is sized by DURATION — a 15-minute
            visit is 12 px at `HOUR_HEIGHT` — so below ~36 px a trigger of any size would either be clipped by the
            block's own `overflow-hidden` or crowd the patient's name off the only line it has. This removes no
            capability (§ 0): the block still opens the edit dialog on click, whose footer carries both actions as
            real buttons, and the popover's own options carry the 44 px coarse floor.
          */}
          {!isVerySmall && height >= 36 && (
            <span {...{ [AGENDA_NO_DRAG_ATTR]: "" }} className="contents">
              <AppointmentQuickActions
                appointment={appointment}
                onChanged={() => onChanged?.()}
                compact
                triggerClassName="-me-0.5"
              />
            </span>
          )}
        </div>
        {!isVerySmall && (
          <div
            className={cn(
              "flex min-w-0 items-baseline gap-1.5 leading-[1.25] opacity-80",
              isSmall ? "text-2xs" : "text-xs",
            )}
          >
            <span className="shrink-0 font-medium tabular-nums">{format(aptStart, "HH:mm")}</span>
            {/* Day view has the room to name every act of a séance; Semaine names the lead act, because
                « Détartrage + Obturation » in 120 px truncates to « Détarta… » — which says less than the badge
                beside the name already said. */}
            {actsSummary && (
              <span className="min-w-0 truncate" title={actsSummary}>
                {view === "day" ? actsSummary : actsSummary.split(" + ")[0]}
              </span>
            )}
          </div>
        )}
        {appointment.notes && !isVerySmall && !isSmall && (
          <div className="mt-0.5 flex-shrink-0 truncate text-xs leading-tight opacity-75">{appointment.notes}</div>
        )}
        {/*
          ⚠️ `hasRoomForSync`, not `!isVerySmall`, and this is a real trade rather than tidying. The block is
          `overflow-hidden`; the name row, the new time+act line and this badge row measure ~49 px inside a
          60-minute block's 36 px of content box, so gating this the old way would have *clipped* the
          « non synchronisé » badge on the single most common appointment length — a badge that is present in the
          DOM and invisible is worse than one that is honestly absent. 62 px ≈ a 75-minute visit, i.e. blocks with
          genuine room. Below that the state is still reachable: it is on the appointment's edit dialog, which is
          one click away, and the phone branch drops these controls at every height for the same reason (a Google
          errand is not what a dentist reads off the grid).
        */}
        {hasRoomForSync && renderSyncControls(appointment)}
      </div>
    )
  }

  const getWeekDays = () => {
    const start = startOfWeek(selectedDate, { weekStartsOn: 1 })
    return Array.from({ length: 7 }, (_, i) => addDays(start, i)) // All 7 days (Monday to Sunday)
  }

  const handlePrevious = () => {
    if (view === "day") {
      onDateChange(subDays(selectedDate, 1))
    } else if (view === "month") {
      onDateChange(subMonths(selectedDate, 1))
    } else {
      onDateChange(subWeeks(selectedDate, 1))
    }
  }

  /**
   * Horizontal swipe to change day — **Jour only**, deliberately.
   *
   * ⚠️ In Semaine the same container scrolls horizontally (AC-30 removed its `overflow-x-hidden` precisely so it
   * would). A swipe handler on that axis in that view would fight the scroll the user is performing, so the
   * gesture is scoped to the one view where the axis is free rather than dropped entirely.
   *
   * The direction test requires the horizontal travel to clearly dominate the vertical, because this container
   * also scrolls vertically through 24 hours — a diagonal thumb drag must scroll, not jump a day.
   */
  const swipeStartRef = useRef<{ x: number; y: number } | null>(null)
  const SWIPE_MIN_PX = 60
  const SWIPE_AXIS_RATIO = 1.5

  const handleTouchStart = (event: React.TouchEvent) => {
    if (view !== "day" || event.touches.length !== 1) {
      swipeStartRef.current = null
      return
    }
    swipeStartRef.current = { x: event.touches[0].clientX, y: event.touches[0].clientY }
  }

  const handleTouchEnd = (event: React.TouchEvent) => {
    const start = swipeStartRef.current
    swipeStartRef.current = null
    if (!start || view !== "day") return
    /*
     * ⚠️ A drag has already consumed this touch. `touchend` fires *after* `pointerup`, so by the time we get here
     * the gesture is finished and invisible — without this, carrying an appointment leftwards across the grid in
     * Jour would also read as « swipe to the previous day » and the agenda would jump out from under the drop.
     */
    if (gridDrag.didConsumeGesture()) return

    const touch = event.changedTouches[0]
    if (!touch) return

    const dx = touch.clientX - start.x
    const dy = touch.clientY - start.y
    if (Math.abs(dx) < SWIPE_MIN_PX || Math.abs(dx) < Math.abs(dy) * SWIPE_AXIS_RATIO) return

    // Swiping left reveals the NEXT day, matching every calendar app's direction convention.
    if (dx < 0) {
      handleNext()
    } else {
      handlePrevious()
    }
  }

  const handleNext = () => {
    if (view === "day") {
      onDateChange(addDays(selectedDate, 1))
    } else if (view === "month") {
      onDateChange(addMonths(selectedDate, 1))
    } else {
      onDateChange(addWeeks(selectedDate, 1))
    }
  }

  const handleToday = () => {
    onDateChange(new Date())
  }

  /**
   * Append the next batch of months once the phone's Mois scroll nears its end — the gesture that means "and
   * after that?" on a phone is to keep scrolling, not to find a chevron.
   *
   * Growing on scroll rather than rendering the cap up front is what keeps the read honest: the visible weeks are
   * also the fetch window (see `endDate`), so a fixed year-wide span would fetch a year of appointments to draw
   * dots on months nobody looked at.
   *
   * ⚠️ The latch is what makes that true in practice. `scroll` fires many times per flick, and every event inside
   * the threshold band satisfied the test — so one thumb flick could step the span 2 → 5 → 8 → 11 and fetch a
   * *year* of appointments in one gesture, which is exactly the read the incremental span exists to avoid. It is
   * cleared by an effect keyed on `monthsAhead`, i.e. once the new weeks have actually rendered and there is
   * genuinely more to reach. Staying latched at the cap is correct and deliberate: the span reset that follows a
   * month change clears it.
   */
  const monthGrowLatchRef = useRef(false)
  useEffect(() => {
    monthGrowLatchRef.current = false
  }, [monthsAhead])

  const handlePhoneMonthScroll = (event: React.UIEvent<HTMLDivElement>) => {
    if (monthGrowLatchRef.current) return
    const el = event.currentTarget
    if (el.scrollHeight - el.scrollTop - el.clientHeight > PHONE_MONTH_GROW_THRESHOLD_PX) return
    monthGrowLatchRef.current = true
    setMonthsAhead((current) => Math.min(current + PHONE_MONTH_AHEAD_STEP, PHONE_MONTH_AHEAD_MAX))
  }

  // A compact month-cell chip: start time + patient name, colored by the shared status/procedure rules
  // (AC-2). Clicking opens the edit dialog (AC-4); stopPropagation keeps the cell's day-navigation from
  // firing too.
  const renderMonthChip = (appointment: AppointmentDto) => {
    const colorStyle = appointmentAppearance(appointment)
    const tone = appointmentTone(appointment)
    const start = format(new Date(appointment.appointmentDateTime), "HH:mm")
    // The status belongs in the label for the same reason it belongs on the block: the chip now *paints* it.
    const chipLabel = `${start} · ${appointment.patientName} · ${appointmentStatusLabel(appointment.status)}`

    return (
      <button
        key={appointment.id}
        type="button"
        onClick={(e) => {
          e.stopPropagation()
          onAppointmentClick?.(appointment)
        }}
        className={cn(
          // `relative`: the strip is an absolutely-positioned child, and unlike the grid blocks (which are
          // absolute themselves) a month chip is in normal flow, so it has to establish the containing block.
          "relative flex w-full items-center gap-1 overflow-hidden rounded px-1 py-0.5 pe-2 text-left text-2xs leading-tight transition-[box-shadow,transform] duration-[160ms] ease-snap hover:shadow-sm active:scale-[0.97]",
          colorStyle.className,
        )}
        style={colorStyle.style}
        title={chipLabel}
        aria-label={chipLabel}
      >
        {/* Mois gets the statut strip too. A month cell is where « qui a annulé ? » is asked over a whole
            period, and the chip previously carried the act's hue with no statut on it at all below the
            strikethrough. */}
        {renderStatusEdge(appointment)}
        {tone === "negative" && <UserX className="h-2.5 w-2.5 shrink-0" aria-hidden="true" />}
        <span className="flex-shrink-0 font-semibold">{start}</span>
        <span className="min-w-0 truncate">{appointment.patientName}</span>
      </button>
    )
  }

  // Month view (AC-1..AC-5): a fixed 6×7 day-cell grid (weeks start Monday). Adjacent-month days are
  // dimmed, today is highlighted, each cell lists its appointments as chips with a "+N more" overflow,
  // and clicking a cell's empty area / "+N more" navigates to Day view for that date. No time-of-day
  // layout, current-time line, or scroll-centering (day/week only).
  /**
   * Semaine below `md:` — seven tappable days with their density, instead of the time grid (AC-28).
   *
   * The scrolling week grid built for AC-30 is honest from `lg:` up: seven 120px columns, a sticky gutter and
   * a sticky header. On a 320–390px phone the same grid is a 900px canvas you read through a 320px window,
   * which is navigation, not reading. This answers the question a phone actually asks of a week — *which day
   * do I need?* — and hands off to Jour, where the hours are legible.
   *
   * ⚠️ **Seven rows, not seven columns.** « Strip » in the plan implies a horizontal band, and that was tried
   * on paper first: seven cells across 320px is ~45px each, which fits dots and nothing else — the same
   * unreadable sliver the month chips became — and it would leave the rest of the screen blank, since the
   * time grid is exactly what it replaces. Rows use the width the phone has, so each day can carry its count
   * and its first appointment time as well as the colour dots. Logged as DEV-8.
   *
   * Accessibility follows the month cells: **dots are `aria-hidden` decoration, the count is the fact.**
   */
  const renderWeekStrip = () => {
    /*
     * ⚠️ A skeleton, because this view had **no loading branch at all**. It read `appointments` directly and was
     * invoked without consulting `loading`, so during the very first fetch the phone's Semaine rendered seven
     * rows of « Aucun rendez-vous » — not a blank waiting state but a *positive statement* that the week is
     * free, which in a clinic is the one wrong answer that matters. The other three views all had this branch;
     * this one was written without it and nothing could see the difference.
     */
    if (loading) {
      return (
        <ul className="divide-y" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 7 }).map((_, i) => (
            <li key={i} className="flex items-center gap-3 px-3 py-3">
              <span className="flex w-10 shrink-0 flex-col items-center gap-1">
                <span className="h-2 w-6 animate-pulse rounded bg-muted" />
                <span className="h-8 w-8 animate-pulse rounded-full bg-muted" />
              </span>
              <span className="flex min-w-0 flex-1 flex-col gap-1.5">
                <span className="h-2 w-10 animate-pulse rounded bg-muted" />
                <span className="h-3 w-32 animate-pulse rounded bg-muted" />
              </span>
            </li>
          ))}
        </ul>
      )
    }

    return (
    <ul
      className={cn(
        "divide-y overflow-y-auto transition-opacity duration-200 ease-snap",
        refetching && "opacity-60",
      )}
      aria-label="Semaine — choisissez un jour"
    >
      {weekDays.map((day) => {
        const dayAppointments = getAppointmentsForDay(day)
        const visible = dayAppointments.slice(0, MONTH_CELL_MAX_CHIPS)
        const overflow = dayAppointments.length - visible.length
        const first = dayAppointments[0]

        return (
          <li key={day.toISOString()}>
            <button
              type="button"
              onClick={() => onSelectDay?.(day)}
              className="flex w-full items-center gap-3 px-3 py-3 text-start transition-colors hover-hover:hover:bg-accent/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
            >
              <span className="flex w-10 shrink-0 flex-col items-center gap-0.5">
                <span className="text-2xs font-medium uppercase tracking-wider text-muted-foreground">
                  {format(day, "EEE", { locale: fr })}
                </span>
                <span
                  className={cn(
                    "inline-flex h-8 w-8 items-center justify-center rounded-full text-sm font-semibold",
                    isToday(day) ? TODAY_PILL : "text-foreground",
                  )}
                >
                  {format(day, "d")}
                </span>
              </span>

              <span className="min-w-0 flex-1">
                {dayAppointments.length === 0 ? (
                  <span className="text-sm text-muted-foreground">Aucun rendez-vous</span>
                ) : (
                  <>
                    <span className="flex flex-wrap items-center gap-1" aria-hidden="true">
                      {visible.map((appointment) => (
                        <span
                          key={appointment.id}
                          className="h-2 w-2 rounded-full"
                          style={{
                            backgroundColor:
                              parseProcedureHex(appointment.procedureColorHex) ?? "var(--muted-foreground)",
                          }}
                        />
                      ))}
                      {overflow > 0 && <span className="text-2xs text-muted-foreground">+{overflow}</span>}
                    </span>
                    <span className="mt-0.5 block truncate text-sm text-foreground">
                      {dayAppointments.length} rendez-vous
                      {first && ` · dès ${format(new Date(first.appointmentDateTime), "HH:mm")}`}
                    </span>
                  </>
                )}
              </span>

              <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" aria-hidden="true" />
            </button>
          </li>
        )
      })}
    </ul>
    )
  }

  /**
   * Mois **below `md:`** — a sticky weekday header over a continuous vertical scroll of weeks, with a heading
   * wherever a new month starts. Google Agenda's month view, and a real branch rather than responsive classes on
   * the grid above, for the same reason `renderWeekStrip` is one.
   *
   * ⚠️ The 6×7 grid it replaces was **fixed height**: 42 cells forced into `flex-1`, which on a phone is about
   * 52 × 60 px per cell for a day that may hold four appointments, and — the part that made it feel like a
   * cramped desktop rather than an app — it *ended*. There was nothing after the last row, so « et en octobre ? »
   * meant finding a chevron. Here the weeks simply continue, and each cell is sized by content instead of by a
   * division of the viewport.
   *
   * Days from adjacent months are **not dimmed**, deliberately: in a continuous scroll the boundary week is not
   * "outside" anything, it is just the week it is, and the month headings are what carry the orientation. Dimming
   * half of it would only make the same days look less real above the heading than below it.
   */
  const renderPhoneMonthView = () => (
    <div className="flex h-full min-h-0 flex-col">
      <div className="grid flex-shrink-0 grid-cols-7 border-b bg-card py-1.5">
        {PHONE_WEEKDAY_INITIALS.map((initial, i) => (
          <div
            key={`${initial}-${i}`}
            className="text-center text-2xs font-medium uppercase tracking-wider text-muted-foreground"
          >
            {initial}
          </div>
        ))}
      </div>

      {loading ? (
        <div className="flex-1 space-y-px p-px" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 7 }).map((_, row) => (
            <div key={row} className="grid grid-cols-7 gap-px">
              {Array.from({ length: 7 }).map((__, col) => (
                <div key={col} className="flex h-14 flex-col items-center justify-center gap-1.5">
                  <div className="h-7 w-7 animate-pulse rounded-full bg-muted" />
                  {(row + col) % 3 === 0 && <div className="h-1.5 w-1.5 animate-pulse rounded-full bg-muted" />}
                </div>
              ))}
            </div>
          ))}
        </div>
      ) : (
        <div
          onScroll={handlePhoneMonthScroll}
          className={cn(
            "min-h-0 flex-1 overflow-y-auto transition-opacity duration-200 ease-snap",
            refetching && "opacity-60",
          )}
        >
          {phoneMonthWeeks.map((week) => {
            // The week that opens a month carries its heading. Every rendered span begins on the week holding a
            // 1st, so the first week always has one — the scroll never starts under an unlabelled month.
            const monthStart = week.find((day) => day.getDate() === 1)

            return (
              <div key={week[0].toISOString()}>
                {monthStart && (
                  <h3 className="px-3 pb-1 pt-3 text-sm font-semibold capitalize">
                    {format(monthStart, "MMMM yyyy", { locale: fr })}
                  </h3>
                )}
                <div className="grid grid-cols-7 border-b">
                  {week.map((day) => {
                    const dayAppointments = getAppointmentsForDay(day)
                    const dots = dayAppointments.slice(0, PHONE_MONTH_MAX_DOTS)
                    const dotOverflow = dayAppointments.length - dots.length
                    const selected = isSameDay(day, selectedDate)

                    return (
                      <button
                        key={day.toISOString()}
                        type="button"
                        onClick={() => onSelectDay?.(day)}
                        aria-current={selected ? "date" : undefined}
                        className="flex min-h-[56px] flex-col items-center gap-1 border-r py-1.5 transition-colors last:border-r-0 active:bg-accent/40"
                      >
                        <span
                          className={cn(
                            "grid h-7 w-7 place-items-center rounded-full text-sm tabular-nums transition-colors",
                            isToday(day)
                              ? "bg-primary font-semibold text-primary-foreground shadow-sm"
                              : selected
                                ? "border border-primary font-semibold text-primary"
                                : "text-foreground",
                          )}
                        >
                          {format(day, "d")}
                        </span>
                        {/* Dots are decoration; the count below is the accessible fact — the same split the
                            month chips and the week strip already use.

                            ⚠️ The « +N » is not decoration though. Capping at three dots and stopping made a day
                            with eight appointments look exactly like a day with three, and *density* is the only
                            thing this view is for — the week strip already said « +N » for the same reason. */}
                        <span className="flex h-3 items-center gap-[3px]" aria-hidden="true">
                          {dots.map((appointment) => (
                            <span
                              key={appointment.id}
                              className="h-1.5 w-1.5 rounded-full"
                              style={{
                                backgroundColor:
                                  parseProcedureHex(appointment.procedureColorHex) ?? "var(--muted-foreground)",
                              }}
                            />
                          ))}
                          {dotOverflow > 0 && (
                            <span className="text-2xs leading-none text-muted-foreground">+{dotOverflow}</span>
                          )}
                        </span>
                        <span className="sr-only">
                          {format(day, "EEEE d MMMM", { locale: fr })}
                          {dayAppointments.length > 0
                            ? ` — ${dayAppointments.length} rendez-vous`
                            : " — aucun rendez-vous"}
                        </span>
                      </button>
                    )
                  })}
                </div>
              </div>
            )
          })}
          {monthsAhead >= PHONE_MONTH_AHEAD_MAX && (
            // Says where the scroll stops rather than just stopping. Twelve months is the cap because the
            // rendered weeks decide the fetch window, and « l'année prochaine » is a job for the date picker.
            <p className="px-3 py-4 text-center text-2xs text-muted-foreground">
              Utilisez la flèche du mois pour aller plus loin.
            </p>
          )}
        </div>
      )}
    </div>
  )

  /** Mois from `md:` up — the fixed 6×7 grid with named chips. The phone gets `renderPhoneMonthView`. */
  const renderMonthView = () => (
    <div className="flex h-full flex-col min-h-0">
      {/* `bg-card` for the reason the week header states: `bg-white dark:bg-background` painted the page ground
          inside a card in dark mode. */}
      <div className="grid flex-shrink-0 grid-cols-7 border-b bg-card">
        {monthGridDays.slice(0, 7).map((day) => (
          <div
            key={`dow-${day.toISOString()}`}
            className="border-r py-2 text-center text-xs font-medium uppercase tracking-wider text-muted-foreground last:border-r-0"
          >
            {format(day, "EEE", { locale: fr })}
          </div>
        ))}
      </div>

      {loading ? (
        // A skeleton shaped like the grid it stands in for, so the month does not jump when the chips arrive
        // (the same reasoning as `patients-table.tsx`). Only on the FIRST load — a month change now dims.
        <div className="grid flex-1 grid-cols-7 grid-rows-6 min-h-0" role="status" aria-label="Chargement des rendez-vous">
          {Array.from({ length: 42 }).map((_, i) => (
            <div key={i} className="min-w-0 border-b border-r p-1 last:border-r-0">
              <div className="flex justify-end">
                <div className="h-6 w-6 animate-pulse rounded-full bg-muted" />
              </div>
              {i % 3 === 0 && <div className="mt-1 h-3 animate-pulse rounded bg-muted" />}
            </div>
          ))}
        </div>
      ) : (
        <div
          className={cn(
            "grid flex-1 grid-cols-7 grid-rows-6 min-h-0 transition-opacity duration-200 ease-snap",
            refetching && "opacity-60",
          )}
        >
          {monthGridDays.map((day) => {
            const dayAppointments = getAppointmentsForDay(day)
            const inMonth = isSameMonth(day, selectedDate)
            const visible = dayAppointments.slice(0, MONTH_CELL_MAX_CHIPS)
            const overflow = dayAppointments.length - visible.length

            return (
              /*
               * ⚠️ The cell is a `<div>` with a **real `<button>` stretched across it**, not a `<button>` wrapping
               * its own content — and that is a hard constraint, not a preference. The cell renders appointment
               * chips, which are buttons: a `<button>` cell would nest buttons (invalid DOM, and React says so),
               * and `role="button"` on the div would be worse, because ARIA makes a button's descendants
               * presentational and would hide every chip from assistive tech. A stretched sibling gives the cell
               * one tab stop and a keyboard route to « ouvrir ce jour » while the chips stay separately
               * focusable. It was a bare `<div onClick>` before, i.e. reachable by mouse only, while its phone
               * twin — `renderPhoneMonthView` — was already a real button.
               */
              <div
                key={day.toISOString()}
                className={cn(
                  "relative flex min-w-0 flex-col gap-0.5 overflow-hidden border-b border-r p-1 transition-colors last:border-r-0 hover:bg-accent/30 dark:hover:bg-muted/50",
                  !inMonth && "bg-muted/40 dark:bg-muted/20",
                )}
              >
                <button
                  type="button"
                  onClick={() => onSelectDay?.(day)}
                  aria-label={`Voir le ${format(day, "EEEE d MMMM", { locale: fr })} en vue Jour${
                    dayAppointments.length > 0 ? ` — ${dayAppointments.length} rendez-vous` : " — aucun rendez-vous"
                  }`}
                  className="absolute inset-0 cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
                />
                <div className="pointer-events-none relative flex flex-shrink-0 justify-end">
                  <span
                    className={cn(
                      "inline-flex h-6 w-6 items-center justify-center rounded-full text-xs font-semibold",
                      isToday(day)
                        ? TODAY_PILL
                        : inMonth
                          ? "text-foreground"
                          : "text-muted-foreground/60",
                    )}
                  >
                    {format(day, "d")}
                  </span>
                </div>
                <div className="relative flex min-h-0 flex-col gap-0.5 overflow-hidden">
                  {visible.map((appointment) => renderMonthChip(appointment))}
                  {overflow > 0 && (
                    // Not interactive: the stretched cell button behind it already opens the day, and a second
                    // control saying the same thing would only add a tab stop per cell.
                    <span className="pointer-events-none px-1 text-2xs font-medium text-muted-foreground">
                      +{overflow} autres
                    </span>
                  )}
                </div>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )

  const weekDays = getWeekDays()

  /**
   * How many appointments the visible window holds, after the status filters — the number appended to the bar's
   * title. `appointments`, not `allAppointments`: a total that counted rows the grid is hiding would not be a
   * total *of what is on screen*, which is the only thing a heading can honestly summarise.
   */
  const visibleCount = appointments.length

  /**
   * How many non-default filters are on, for the « Filtres » badge.
   *
   * It is what makes folding the controls into a popover safe: the trigger says *how many* rather than the state
   * being silent. The page renders the removable chips too (§ 13) — this is the count, not a substitute for them.
   */
  const activeFilterCount = (showCancelled ? 1 : 0) + (showCompleted ? 1 : 0) + (doctorFilter && doctorFilter.value !== "all" ? 1 : 0)

  /**
   * The acts on screen and the colour each of them paints — **derived from the window, never a list kept here.**
   *
   * `appointmentAppearance`'s own docstring flagged this as the deferred half of the legend: the grid's hue is
   * the *act*, the status is a treatment on top, and only the second half was ever explained. A hardcoded table
   * of act colours was the obvious fix and the wrong one — `ProcedureType.ColorHex` is clinic-editable data, so
   * a second copy would be stale the first time somebody recoloured « Détartrage » and would list acts this
   * practice does not perform. Reading the window instead means the key names exactly the colours that are
   * currently on screen, in every deployment, for free.
   *
   * Keyed on the **lead** act's name because `procedureColorHex` is the lead act's colour (`Appointment`'s
   * derived snapshot) — pairing a grouped séance's full « A + B » summary with A's hue would state something
   * false about B. Blocked slots are skipped: they carry no act and paint amber from the warning tokens.
   */
  const actLegend = useMemo(() => {
    const byName = new Map<string, { name: string; hex: string }>()
    for (const appointment of appointments) {
      if (isBusySlot(appointment)) continue
      const hex = parseProcedureHex(appointment.procedureColorHex)
      const summary = appointmentActsSummary(appointment)
      if (!hex || !summary) continue
      const lead = summary.split(" + ")[0]
      if (!byName.has(lead)) byName.set(lead, { name: lead, hex })
    }
    return Array.from(byName.values()).sort((a, b) => a.name.localeCompare(b.name, "fr"))
  }, [appointments])

  /**
   * The legend, rendered once and mounted twice (the desktop « Filtres » popover and the phone disclosure).
   *
   * They were two hand-written copies before, which is how they came to disagree about which statuses existed.
   * Both axes are named — « Bord droit » for the statut strip, « Couleur du rendez-vous » for the act — because
   * a key that explains one of two colour channels is what made the grid look arbitrary.
   */
  const renderLegend = () => {
    const shownActs = actLegend.slice(0, LEGEND_MAX_ACTS)
    const hiddenActs = actLegend.length - shownActs.length

    return (
      <div className="flex flex-col gap-3">
        <div className="flex flex-col gap-1.5">
          <p className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">
            Statut — bord droit
          </p>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs">
            {LEGEND_ITEMS.map((item) => (
              <span key={item.label} className="flex items-center gap-1.5">
                {/* The swatch IS the paint — `STATUS_EDGE_PAINT`, the same value the block renders — rather than
                    a flat fill that approximates it. That is what makes the dashed « Planifié » strip legible as
                    a key rather than as a smudge. */}
                <span
                  className="h-3.5 w-[3px] shrink-0 rounded-full"
                  style={{ background: STATUS_EDGE_PAINT[item.tone] }}
                />
                <span className="text-muted-foreground">{item.label}</span>
              </span>
            ))}
          </div>
        </div>

        <div className="flex flex-col gap-1.5 border-t pt-3">
          <p className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">
            Acte — couleur du rendez-vous
          </p>
          {shownActs.length === 0 ? (
            // Not an « aucun acte » claim about the catalogue — a statement about this window, which is the only
            // thing this key can honestly describe.
            <p className="text-xs text-muted-foreground">Aucun acte coloré sur cette période.</p>
          ) : (
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs">
              {shownActs.map((act) => (
                <span key={act.name} className="flex min-w-0 items-center gap-1.5">
                  <span
                    className="h-3 w-3 shrink-0 rounded-full"
                    style={{ backgroundColor: act.hex }}
                    aria-hidden="true"
                  />
                  <span className="truncate text-muted-foreground">{act.name}</span>
                </span>
              ))}
              {hiddenActs > 0 && <span className="text-muted-foreground">+{hiddenActs} autres</span>}
            </div>
          )}
        </div>

        {effectiveHours && effectiveHours.length > 0 && (
          <div className="flex flex-col gap-1.5 border-t pt-3">
            <p className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">Grille</p>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 text-xs">
              <span className="flex items-center gap-1.5">
                <span
                  className="h-3.5 w-3.5 shrink-0 rounded border"
                  style={{ backgroundImage: CLOSED_HATCH }}
                  aria-hidden="true"
                />
                {/* Names the pause too: the same hatch now covers the mid-day closure, and a key that lists
                    only one of the two things a mark means is what makes the other read as a fault. */}
                <span className="text-muted-foreground">Hors horaires d&apos;ouverture (pause incluse)</span>
              </span>
              <span className="flex items-center gap-1.5">
                <span
                  className="h-3.5 w-3.5 shrink-0 rounded border"
                  style={{ backgroundColor: "var(--agenda-today)" }}
                  aria-hidden="true"
                />
                <span className="text-muted-foreground">Aujourd&apos;hui</span>
              </span>
            </div>
            {doctorHours !== null && (
              <p className="text-xs text-muted-foreground">
                Horaires du praticien sélectionné, et non ceux du cabinet.
              </p>
            )}
          </div>
        )}
      </div>
    )
  }

  /**
   * The praticien Select, rendered twice — visible in the bar from `xl:`, inside « Filtres » below it — from one
   * function, because two hand-written copies of an options list is how one of them stops listing a new doctor.
   */
  const renderDoctorSelect = (className: string, id?: string) =>
    doctorFilter ? (
      <Select value={doctorFilter.value} onValueChange={doctorFilter.onChange}>
        <SelectTrigger id={id} className={cn("h-9", className)}>
          <SelectValue placeholder="Praticien" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">Tous les praticiens</SelectItem>
          {doctorFilter.doctors
            .filter((doc) => doc.id)
            .map((doc) => (
              <SelectItem key={doc.id} value={doc.id!}>
                {doc.name}
              </SelectItem>
            ))}
        </SelectContent>
      </Select>
    ) : null

  /**
   * Nothing at all in the time grid's visible range — Jour's day, or all seven of Semaine's.
   *
   * Deliberately the *whole range* rather than per day: seven « aucun rendez-vous » cards down one week grid is
   * noise, and in Semaine the question the user is asking is about the week.
   */
  const emptyRange =
    view === "week"
      ? weekDays.every((day) => getAppointmentsForDay(day).length === 0)
      : getAppointmentsForDay(selectedDate).length === 0

  /**
   * The first paint, before `matchMedia` has answered — see the `mounted` note at the top.
   *
   * Deliberately **geometry-free**: no hour rows, no gutter width, no seven columns. Its whole job is to occupy
   * the card honestly for the one frame in which this component does not yet know whether it is drawing a phone
   * or a desk machine, so it must not commit to either. The real per-view skeletons take over immediately after.
   */
  const renderGeometrySkeleton = () => (
    <div className="flex h-full flex-col gap-2 p-3" role="status" aria-label="Chargement de l'agenda">
      {Array.from({ length: 10 }).map((_, i) => (
        <div key={i} className="flex items-center gap-3">
          <div className="h-3 w-9 flex-shrink-0 animate-pulse rounded bg-muted" />
          <div className="h-8 flex-1 animate-pulse rounded bg-muted/60" />
        </div>
      ))}
    </div>
  )

  return (
    <div className="flex h-full flex-col">
      {/*
        The phone header (< md:). Mounted here rather than in the page because it needs the appointment data this
        component owns — the density dots and the « Prochains RDV » summary both read it, and lifting the fetch to
        the page to feed a header would be a much larger change for no gain.
        Design + rationale: features/agenda-phone-ux/design.md.
      */}
      {onViewChange && (
        <AgendaPhoneHeader
          view={view}
          onViewChange={onViewChange}
          selectedDate={selectedDate}
          onDateChange={onDateChange}
          appointments={appointments}
          /*
           * The two status filters, on the phone at last. They lived only in the `md:flex` toolbar below and in
           * the `hidden md:flex` chip row on the page, so `lib/dashboard-links.ts`' « Taux d'absence » link —
           * `/appointments?status=NoShow,Cancelled` — landed a phone user on a *filtered* agenda with nothing on
           * screen saying why and no way to clear it. The chips and the « Filtres » disclosure now live in the
           * header, which is the phone's only toolbar.
           */
          /* The create action's phone home. It was a floating pill fixed above the bottom nav, which cost every
             scroller below 56 px of clearance padding and covered the last hour of the grid. */
          onNewAppointment={onNewAppointment}
          showCancelled={showCancelled}
          showCompleted={showCompleted}
          onShowCancelledChange={onShowCancelledChange}
          onShowCompletedChange={onShowCompletedChange}
          /* The same resolution the grid shades with, so the phone strip and the desktop grid cannot disagree
             about which days the cabinet is open. */
          isDayOpen={isDayOpenOnAgenda}
        />
      )}

      {/*
        ══ The agenda bar — ONE row, desktop/tablet only (the phone header above replaces it below `md:`) ══

        It was **four** rows before this: (1) the view switch + « Nouveau rendez-vous », (2) the Google controls +
        the praticien filter, (3) these arrows + « Aujourd'hui » + the date, (4) the legend + « Afficher : » + the
        two switches + Exporter. Two of them lived in `app/appointments/page.tsx` and two here, which is how the
        *date* ended up two rows away from the *view switch* — the two halves of "where am I in time" — with an
        administrative Google row wedged between them. Together they spent ≈ 215 px of a ~910 px window, so the
        grid got less than half the screen and showed five hours of an eleven-hour day.

        Three rules hold the single row together:

        1. **The calendar owns the whole bar.** It already owned three quarters of it and all the data behind it
           (the window for Exporter, the appointment index for the count, the view-aware date arithmetic). The
           page's four controls arrive as props (`onNewAppointment`, `doctorFilter`, `googleControls`) rather than
           being rendered a row above, because two components rendering one control strip is precisely how the
           strip became four rows.
        2. **The title absorbs the slack** (`min-w-0 flex-1 truncate`), so the row can never wrap. That is what
           makes 820 px safe — an iPad portrait with the 256 px rail expanded leaves ~532 px, and a wrapping bar
           there is a third row on the device this product is used on most.
        3. **Reference and controls are separated.** The legend is reference, so it moved *inside* « Filtres »
           beside the switches it explains; one-off administration (Google, Exporter) moved into « ⋯ ». The old
           fourth row mixed five legend swatches, a « Afficher : » label, two switches and a download button on
           one line with nothing to say which of them answered a click.
      */}
      <div className="mb-3 hidden flex-nowrap items-center gap-2 md:flex">
        <Button variant="outline" size="icon" className="h-9 w-9 shrink-0 bg-transparent" onClick={handlePrevious} aria-label="Période précédente">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <Button variant="outline" size="icon" className="h-9 w-9 shrink-0 bg-transparent" onClick={handleNext} aria-label="Période suivante">
          <ChevronRight className="h-4 w-4" />
        </Button>
        {/*
          « Aujourd'hui » keeps its label from `lg:` up and drops to the icon below it — the icon is a calendar
          glyph with an `aria-label`, and at 820 px this label is the cheapest of the four to spend. It is not the
          primary action (that is « Nouveau »), so § 13's no-unlabelled-primary rule is not what is at stake here.
        */}
        <Button
          variant="outline"
          size="sm"
          onClick={handleToday}
          className="h-9 shrink-0 gap-2 bg-transparent px-2.5 lg:px-3"
          aria-label="Aller à aujourd'hui"
        >
          <Calendar className="h-4 w-4" />
          <span className="hidden lg:inline">Aujourd&apos;hui</span>
        </Button>

        {/* `min-w-0 flex-1 truncate` — rule 2 above. « mercredi 12 novembre 2026 » is wider than the space a
            tablet has, and an un-truncated title is what forced the old toolbar onto a fifth row (AC-31). */}
        <div className="ml-1 min-w-0 flex-1 truncate text-base font-semibold lg:text-lg">
          {view === "week"
            ? `${format(weekDays[0], "d MMM", { locale: fr })} – ${format(weekDays[6], "d MMM yyyy", { locale: fr })}`
            : view === "month"
              ? format(selectedDate, "MMMM yyyy", { locale: fr })
              : format(selectedDate, "EEEE d MMMM yyyy", { locale: fr })}
          {/* The window's own total, appended to the title rather than given an element: it is a fact *about*
              this heading, it costs no width of its own, and it is the first thing to truncate away when the
              viewport gets tight — which is the correct priority for a summary. */}
          {visibleCount > 0 && (
            <span className="ml-2 hidden text-sm font-normal text-muted-foreground tabular-nums lg:inline">
              · {visibleCount} RDV
            </span>
          )}
        </div>

        <div
          role="group"
          aria-label="Vue de l'agenda"
          /*
            ⚠️ **The track needed a border and the pill needed the right white.** The active segment was
            `bg-background`, and in this palette `--background` is the tinted *page ground* (`oklch 0.977`) while
            `--card` — the surface this bar actually sits on — is pure white. So the "selected" pill was painted
            **darker than the surface behind it** and sat 1.3 % away from its own `bg-muted` track (`0.964`):
            three flat words with no control around them, which is exactly how it read. The pill is `bg-card`
            now (it lifts), the track carries a real border (it reads as a control at rest), and the primary ring
            plus primary ink is what survives at a glance on a busy bar.
          */
          className="inline-flex h-9 shrink-0 items-center rounded-lg border border-border bg-muted p-[3px] text-muted-foreground"
        >
          {AGENDA_VIEW_OPTIONS.map(({ value, label, short }) => {
            const selected = view === value
            return (
              <button
                key={value}
                type="button"
                aria-pressed={selected}
                aria-label={label}
                onClick={() => onViewChange?.(value)}
                className={cn(
                  "inline-flex h-[calc(100%-1px)] items-center justify-center rounded-md border border-transparent px-2 py-1 text-sm font-medium whitespace-nowrap transition-[color,background-color,box-shadow] duration-150 ease-snap lg:px-2.5",
                  "focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] focus-visible:outline-1",
                  // `bg-card`, not `bg-background` — see the track's note. `dark:bg-input/70` because in dark the
                  // card is *darker* than the muted track, so "raised" has to come from the lighter input token
                  // there; the ring and the primary ink carry the state in both themes either way.
                  selected
                    ? "bg-card font-semibold text-primary shadow-sm ring-1 ring-primary/30 dark:bg-input/70"
                    : "hover:text-foreground",
                )}
              >
                {/* Full words where there is room, initials on a tablet — the control is three toggles whose
                    meaning is carried by `aria-label` either way. */}
                <span className="hidden lg:inline">{label}</span>
                <span className="lg:hidden">{short}</span>
              </button>
            )
          })}
        </div>

        {/* The praticien filter stays a visible Select from `xl:` up, and below that it lives inside « Filtres »
            (it is one). Both instances read the same props and the same `renderDoctorSelect`, so they cannot
            drift; neither holds state of its own. */}
        {doctorFilter && <div className="hidden shrink-0 xl:block">{renderDoctorSelect("w-[170px]")}</div>}

        {/*
          « Filtres » — the two status toggles, the praticien below `xl:`, and the legend that explains the paint.

          A popover rather than a permanent row because all of it is occasional: the grid's default (hide
          cancelled, hide completed, all praticiens) is what the desk wants ~all day. The active-filter chips that
          make a non-default state visible are rendered by the page, at every width, which is what keeps § 13's
          "an active filter is visible as a removable chip" true while the controls themselves are folded away.
        */}
        <Popover>
          <PopoverTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              className={cn(
                "h-9 shrink-0 gap-2 bg-transparent px-2.5 lg:px-3",
                activeFilterCount > 0 && "border-primary text-primary",
              )}
              aria-label="Filtres de l'agenda"
            >
              <Filter className="h-4 w-4" />
              <span className="hidden lg:inline">Filtres</span>
              {activeFilterCount > 0 && (
                <span className="grid h-4 min-w-4 place-items-center rounded-full bg-primary px-1 text-2xs font-semibold text-primary-foreground tabular-nums">
                  {activeFilterCount}
                </span>
              )}
            </Button>
          </PopoverTrigger>
          {/* `w-[min(20rem,calc(100vw-2rem))]` — § 10: a bare `w-80` is the whole viewport at 320 px with no
              gutter. This one only ever opens at `md:` and up, but the rule is cheap to keep and the popover is
              one copy-paste away from a narrower home. */}
          <PopoverContent align="end" className="w-[min(20rem,calc(100vw-2rem))] p-4">
            <div className="flex flex-col gap-4">
              <div className="flex flex-col gap-3">
                <p className="text-sm font-semibold">Afficher</p>
                <div className="flex items-center gap-2">
                  <Switch
                    id="show-completed"
                    checked={showCompleted}
                    onCheckedChange={(checked) => onShowCompletedChange?.(checked)}
                  />
                  <Label htmlFor="show-completed" className="cursor-pointer text-sm">
                    Rendez-vous terminés
                  </Label>
                </div>
                <div className="flex items-center gap-2">
                  <Switch
                    id="show-cancelled"
                    checked={showCancelled}
                    onCheckedChange={(checked) => onShowCancelledChange?.(checked)}
                  />
                  <Label htmlFor="show-cancelled" className="cursor-pointer text-sm">
                    Rendez-vous annulés
                  </Label>
                </div>
              </div>

              {doctorFilter && (
                <div className="flex flex-col gap-2 xl:hidden">
                  <Label htmlFor="agenda-doctor-filter" className="text-sm font-semibold">
                    Praticien
                  </Label>
                  {renderDoctorSelect("w-full", "agenda-doctor-filter")}
                </div>
              )}

              {/*
                The legend, beside the switches it belongs with — and now naming **both** colour axes.

                The note that used to sit here said the act colours were a deferred second pass and that writing
                a half-true legend into a nicer home would be the same defect in a nicer place. It was right, and
                `renderLegend` is that second pass: the statut swatches are the real edge paint and the act
                swatches are derived from the window (`actLegend`), so neither can describe the grid wrongly.
              */}
              <div className="flex flex-col gap-2 border-t pt-3">
                <p className="text-sm font-semibold">Légende</p>
                {renderLegend()}
              </div>
            </div>
          </PopoverContent>
        </Popover>

        {/*
          ══ Google Agenda — a visible control whose LABEL IS ITS STATE ══════════════════════════════════════

          It lived inside « ⋯ » with the export, which put the whole feature two clicks deep behind an unlabelled
          glyph and left the connection state stated **nowhere on the screen** — so « est-ce que mon agenda est
          synchronisé ? » had no answer short of opening the menu, and a practice that had never connected had no
          way to discover the feature existed at all.

          One slot, two states, and the state decides both the label and what the click does:

            • not connected → a primary-tinted outline « Connecter Google » that connects **directly**. A
              popover in front of a one-action state is a click that buys nothing.
            • connected     → « Google Agenda » with a `CalendarCheck` in `--success`, opening the two
              secondary actions. Importer and Déconnecter stay behind that press on purpose: the note that used
              to sit here is still right — a destructive-looking « Déconnecter » at navigation weight, on a
              screen a dentist opens forty times a day, for a connection made once in the life of the practice.

          The label is `xl:` and the button is icon-only below it, `aria-label`led at every width. That is the
          same budget « Aujourd'hui » spends: at 820 px the bar is `flex-nowrap` and the title absorbs the slack,
          so a new *labelled* control here is what would have forced a second row on the device this product is
          used on most.

          Admins only — the prop is `undefined` for everyone else, because all three endpoints are `AdminOnly`
          and offering the action to a secretary buys a 403 and a generic « Échec ».
        */}
        {googleControls &&
          (!googleControls.authorized ? (
            <Button
              variant="outline"
              size="sm"
              onClick={googleControls.onConnect}
              disabled={!internetReachable}
              title={internetReachable ? "Connecter Google Agenda" : "Connexion internet requise"}
              aria-label={
                internetReachable
                  ? "Connecter Google Agenda"
                  : "Connecter Google Agenda — connexion internet requise"
              }
              className="h-9 shrink-0 gap-2 border-primary/45 bg-primary/5 px-2.5 text-primary hover:bg-primary/10 hover:text-primary xl:px-3"
            >
              <CalendarPlus className="h-4 w-4" />
              <span className="hidden xl:inline">Connecter Google</span>
            </Button>
          ) : (
            <Popover>
              <PopoverTrigger asChild>
                <Button
                  variant="outline"
                  size="sm"
                  className="h-9 shrink-0 gap-2 bg-transparent px-2.5 xl:px-3"
                  aria-label="Google Agenda — connecté. Déconnecter."
                >
                  <CalendarCheck className="h-4 w-4 text-success" />
                  <span className="hidden xl:inline">Google Agenda</span>
                </Button>
              </PopoverTrigger>
              <PopoverContent align="end" className="w-[min(17rem,calc(100vw-2rem))] p-2">
                <div className="flex flex-col gap-1">
                  <p className="px-2 pb-1 text-2xs font-semibold uppercase tracking-wider text-muted-foreground">
                    Google Agenda · connecté
                  </p>
                  {/* Nothing pulls: the events this clinic books here are pushed to Google as they are saved,
                      and « Importer depuis Google » was retired. So the only action left is disconnecting. */}
                  <Button
                    variant="ghost"
                    size="sm"
                    className="w-full justify-start gap-2 text-destructive hover:text-destructive"
                    onClick={googleControls.onDisconnect}
                  >
                    <Unlink className="h-4 w-4" />
                    Déconnecter Google
                  </Button>
                  {!internetReachable && (
                    <p className="px-2 pt-1 text-2xs text-warning-ink">Connexion internet requise</p>
                  )}
                </div>
              </PopoverContent>
            </Popover>
          ))}

        {/*
          « ⋯ » — the one-off errands. Exporter, and nothing else now that Google has a slot of its own: two
          homes for one feature is how a control ends up stated differently in each of them.
        */}
        <Popover>
          <PopoverTrigger asChild>
            <Button variant="outline" size="icon" className="h-9 w-9 shrink-0 bg-transparent" aria-label="Autres actions de l'agenda">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-[min(17rem,calc(100vw-2rem))] p-2">
            <div className="flex flex-col gap-1">
              {/*
                L5 — « Exporter » lives on the calendar, not on `app/appointments/page.tsx`, because the **window**
                does: `startDate`/`endDate` above are derived from `view` + `selectedDate` + (on a phone's Mois)
                `monthsAhead`, all of which are internal to this component. A copy of that derivation at page level
                would be a second authority on « which days are on screen » and would disagree the moment the phone
                lazily loaded another month. The two bounds go through the same `startOfDay`/`endOfDay`/`toISOString`
                transform `useAppointments` applies, so the file covers exactly the days the grid drew.

                ⚠️ It carries the window and the praticien, and **not** « Terminés » / « Annulés ». Those two toggles
                *reveal* rather than narrow — the grid hides completed and cancelled visits by default — so honouring
                them would mean the ordinary export of a past week omitted almost every appointment in it. `Statut` is
                a column in the CSV, so nothing is hidden: the file is the whole window and the spreadsheet can filter
                it. Stated here because it is the one place in L5 where the file is deliberately a superset.
              */}
              <ExportButton
                path="/appointments/export"
                label="rendez-vous"
                className="w-full justify-start"
                params={{
                  startDate: startOfDay(startDate).toISOString(),
                  endDate: endOfDay(endDate).toISOString(),
                  doctorId,
                }}
              />
            </div>
          </PopoverContent>
        </Popover>

        {onNewAppointment && (
          <Button onClick={onNewAppointment} size="sm" className="h-9 shrink-0 gap-2">
            <Plus className="h-4 w-4" />
            {/* Labelled at every width this row renders at — an icon-only primary action on the busiest screen
                in the app is the unlabelled-ghost-icon problem P3 spent a part removing. */}
            Nouveau
          </Button>
        )}
      </div>

      {/*
        The fetch failed. This is deliberately a banner ABOVE the grid rather than a replacement for it: the
        rows already loaded are still the best information available, and an agenda that goes blank on a
        transient error is worse than a stale one that says so. Before this existed, `error` was destructured
        away and a failed request rendered as an empty day — « rien cet après-midi » when the server simply
        never answered.
      */}
      {error && (
        <div
          role="status"
          className="mb-3 flex flex-wrap items-center justify-between gap-3 rounded-lg border border-warning/30 bg-warning-wash px-3 py-2 text-sm text-warning-ink"
        >
          <span>
            Les rendez-vous n&apos;ont pas pu être chargés
            {appointments.length > 0 ? " — l'agenda ci-dessous peut être incomplet." : "."} {error}
          </span>
          <Button variant="outline" size="sm" onClick={() => void refetch()} disabled={refetching}>
            {refetching ? "Chargement…" : "Réessayer"}
          </Button>
        </div>
      )}

      {/*
        The legend on a phone, and it is a **sibling of the toolbar rather than a child of it** — which is the
        entire fix.

        ⚠️ This `<details className="md:hidden">` used to live inside the toolbar `<div className="… hidden … md:flex">`
        above. The ancestor is `display:none` below `md:` and the child hides itself from `md:` up, so the two
        conditions never overlapped and the phone legend **rendered at no width, in no view, ever**. It is the
        exact defect a `md:hidden` inside a `hidden md:flex` always is, and it is invisible in review because both
        halves read as correct on their own.

        The legend is REFERENCE, not a control, and five items of it is two rows on a phone — so below `md:` it
        folds into a disclosure rather than being deleted. Hiding it outright would have been simpler and wrong:
        the grey « hors horaires » shading has no other explanation anywhere.
      */}
      <details className="mb-2 px-3 md:hidden">
        <summary className="touch-target cursor-pointer text-sm text-muted-foreground">Légende</summary>
        <div className="mt-2">{renderLegend()}</div>
      </details>

      {/*
        ⚠️ **`py-0` at every width, not `md:py-6`.** That padding was 24 px of dead white *inside* the card, above
        the sticky day header and below the footer strip — two bands of nothing framing a grid that already draws
        its own header, its own gridlines and its own footer border. A calendar's chrome is its ruler, and a ruler
        has to start at the edge of the instrument.
      */}
      {/*
        ⚠️ **`border-border`, and the colour is the whole point — `border` alone draws nothing.** The `Card`
        primitive sets `border-transparent` (it carries elevation with `shadow-md` instead, deliberately), and a
        Tailwind `border` sets only the *width*, so this card had a 1 px transparent rule: a white surface on a
        near-white ground with no edge at all. It is also the one card in the app that downgrades the shadow
        (`shadow-sm`), because a full-height working surface is not a floating object — so it had opted out of
        both of the two things the system uses to express a surface. The stroke is the right half to restore
        rather than the shadow: the grid's own hairlines run to the card's edge and need an edge to terminate
        against, or the ruler bleeds off the page.

        ⚠️ **The edge is drawn at EVERY width now, and the phone was the case that needed it most.** It used to
        be `md:`-only, under the heading « edge-to-edge on a phone » — but the card is **not** edge-to-edge at any
        width: `AppShell` gutters `<main>` with `p-4`, so below `md:` this was a square, border-less, shadow-less
        white slab inset 16 px into a near-white page. Both of the two things that say « this is a surface » were
        off, on the one screen where the grid's hairlines run all the way to the edge — so the ruler simply
        stopped in the middle of the page. Going *actually* edge-to-edge (bleeding the card back through the
        gutter with `-mx-4`) is the other coherent answer and would buy 32 px of width; drawing the edge is the
        smaller one, and it is what « lacks definition » asks for.
      */}
      <Card className="min-h-0 flex-1 overflow-hidden rounded-xl border border-border py-0 shadow-sm">
        {!mounted ? (
          renderGeometrySkeleton()
        ) : view === "month" ? (
          isNarrow ? renderPhoneMonthView() : renderMonthView()
        ) : view === "week" && isNarrow ? (
          /* ⚠️ A real branch, not `md:hidden` on the grid. A hidden (`display:none`) scroll container reports
             `offsetTop: 0` for every row, so the opening-hour positioning and the current-time line would both
             compute against a zero-height layout and be wrong the moment the viewport crossed back to `md:`. Not
             rendering it means there is nothing to mis-measure, and the scroll effect re-runs on `isNarrow`. */
          renderWeekStrip()
        ) : (
        <div className="relative flex h-full flex-col min-h-0">
          {/*
            AC-30 — **one element scrolls both axes**, and everything else follows from that.

            The week grid is the only wide content in the app that used to be clipped rather than scrollable
            (`overflow-x-hidden`), which is what AC-P3.14 forbids. Making it scroll is not a class change,
            for two reasons:

            1. **The day header must be INSIDE this scroller.** It used to be a sibling above it, so scrolling
               sideways would have left the dates sitting over the wrong columns. It is `sticky top-0` here
               instead, which is the same result vertically and stays honest horizontally.
            2. **A `sticky left-0` gutter only sticks to a scrollport that scrolls horizontally.** So the
               obvious alternative — a horizontal scroller outside, a vertical one inside — cannot give a
               sticky time column at all. Hence `overflow-auto` on this single element.

            ⚠️ The inner wrapper below is load-bearing, not tidiness: the appointment overlay is positioned
            with percentages (`(100% - 60px) / 7`), and a percentage resolves against the containing block's
            **padding box** — the visible width, not the scrollable content width. With the overlay as a direct
            child of this scroller, the moment the grid got wider than the viewport every block would land in
            the wrong column, silently. The wrapper is `w-max`, so `100%` means *the grid*, and the `calc()`
            strings — and with them the `HOUR_HEIGHT` invariant — need no change at all.
          */}
          <div
            ref={scrollContainerRef}
            onTouchStart={handleTouchStart}
            onTouchEnd={handleTouchEnd}
            className={cn(
              "relative min-h-0 flex-1 overflow-auto transition-opacity duration-200 ease-snap",
              refetching && "opacity-60",
              /*
               * ⚠️ **Unconditional, and it has to be.** A mouse drag across the grid otherwise anchors a native
               * text selection and paints a blue smear over the hour labels, the blocks and — the selection runs
               * on past the scroller — the footer beneath them, which is what a drag across two hours actually
               * looked like before this.
               *
               * Gating it on the drag being armed does **not** work: the browser establishes the selection anchor
               * on `mousedown`, i.e. before any movement has told us this is a drag rather than a click, so by the
               * time the class lands the smear already exists. That was measured, not assumed.
               *
               * The grid is a control surface rather than prose — every value on it is reachable as real text in
               * the edit dialog and on the patient's page — so nothing is lost by never selecting inside it.
               */
              "select-none",
            )}
          >
          {/* `lg:w-full`, matching `WEEK_COLS` — the two are one contract and moving only one of them is what
              puts every block in the wrong column. It carries no padding of its own: anything added here has to
              leave both the padding box's width (the `100%` the bands resolve against) and the `top: 0` origin
              the absolutely-positioned blocks measure from untouched. */}
          <div
            className={cn(
              "relative",
              view === "week" ? "w-max min-w-full lg:w-full" : "w-full",
            )}
          >
          {/*
            Not rendered on a phone: below `md:` this branch is only ever Jour, and the phone header directly
            above already names the day — « mer. 12 novembre » in its title and the same date circled in its week
            strip. A third statement of it costs ~46 px of the only screen the grid has to itself. Google Agenda's
            phone day view has no in-grid date header for exactly this reason.
          */}
          {!isNarrow && (
          <div
            className={cn(
              // z-50: above the sticky gutter (z-30) and the current-time overlay (z-40), both of which
              // scroll under it.
              //
              // ⚠️ `bg-card`, not `bg-white dark:bg-background`. This header sits *inside* the agenda `Card`, so
              // in dark mode it was painting the **page ground** (`0.245`) over the card (`0.305`) — a darker
              // strip across the top of a lighter surface, which reads as a seam. One token, correct in both
              // themes, and it is the third of the pass's three "rendering gaps".
              "sticky top-0 z-50 grid border-b bg-card",
              view === "week" ? WEEK_COLS : dayGridCols,
            )}
          >
            {/* The corner cell is sticky on BOTH axes, so it stays over the time gutter as it scrolls. */}
            <div className="sticky left-0 z-10 border-r bg-muted/60" />
            {view === "week" ? (
              /*
               * A **button**, so Semaine's day header opens that day the way a Mois cell does.
               *
               * ⚠️ The circle below already carried `hover:bg-gray-100` while the cell was a plain `<div>` — an
               * affordance with nothing behind it, which is worse than no affordance: it invites the click Mois
               * has trained the user to expect and then swallows it. Only the header navigates; the hour cells
               * below still open the create dialog, which is why this is not a click handler on the column.
               */
              weekDays.map((day) => {
                /*
                 * The day's load, in the header.
                 *
                 * « Quel jour est chargé ? » was previously answered by counting blocks with the eye — and the
                 * short ones are 12–18 px filets, so the count was genuinely hard to take. The number comes from
                 * the `appointmentsByDay` index the grid already builds, so it costs one map lookup per column and
                 * no request.
                 *
                 * ⚠️ A closed day says « fermé » rather than « 0 RDV »: zero on an open Thursday is an invitation
                 * to book, zero on a Sunday the clinic never opens is a different fact, and the grid shades the
                 * two identically at a glance.
                 */
                const dayCount = getAppointmentsForDay(day).length
                const dayIsOpen = isDayOpenOnAgenda(day)
                return (
                  <button
                    key={day.toISOString()}
                    type="button"
                    onClick={() => onSelectDay?.(day)}
                    aria-label={`Voir le ${format(day, "EEEE d MMMM", { locale: fr })} en vue Jour — ${
                      dayIsOpen ? `${dayCount} rendez-vous` : "cabinet fermé"
                    }`}
                    className={cn(
                      "min-w-0 border-r py-2 text-center transition-colors last:border-r-0 hover:bg-accent/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring dark:hover:bg-muted/50",
                      // The header half of the today band the columns below carry, through the SAME token
                      // (`--agenda-today`) rather than a second pair of hand-tuned opacities — so the marked
                      // column reads as one continuous band from its date down through its last hour, and the
                      // two halves cannot drift apart the next time either is nudged.
                      isToday(day) && "bg-[var(--agenda-today)]",
                    )}
                  >
                    <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                      {format(day, "EEE", { locale: fr })}
                    </div>
                    <div
                      className={cn(
                        "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold transition-colors",
                        isToday(day) ? TODAY_PILL : "text-foreground",
                      )}
                    >
                      {format(day, "d")}
                    </div>
                    <div
                      className={cn(
                        "mt-0.5 text-2xs tabular-nums",
                        isToday(day) ? "font-semibold text-primary" : "text-muted-foreground",
                      )}
                    >
                      {!dayIsOpen ? "fermé" : dayCount === 0 ? "—" : `${dayCount} RDV`}
                    </div>
                  </button>
                )
              })
            ) : (
              <div className="py-2 text-center">
                <div className="mb-1 text-xs font-medium uppercase tracking-wider text-muted-foreground">
                  {format(selectedDate, "EEEE", { locale: fr })}
                </div>
                <div
                  className={cn(
                    "mx-auto inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-semibold",
                    isToday(selectedDate) ? TODAY_PILL : "text-foreground",
                  )}
                >
                  {format(selectedDate, "d")}
                </div>
              </div>
            )}
          </div>
          )}

          {loading ? (
            // Skeleton rows at the real hour height AND the real row count, so the grid does not resize when the
            // appointments land — 12 fixed rows overflowed a stretched scrollport and then snapped back.
            <div role="status" aria-label="Chargement des rendez-vous">
              {Array.from({ length: visibleHours.length }).map((_, i) => (
                <div
                  key={i}
                  className={cn("grid border-b", view === "week" ? WEEK_COLS : dayGridCols)}
                  style={{ height: hourHeight }}
                >
                  <div
                    className={cn(
                      "flex items-center justify-end px-2",
                      !isNarrow && "border-r bg-muted/60",
                    )}
                  >
                    <div className="h-3 w-8 animate-pulse rounded bg-muted-foreground/20" />
                  </div>
                  {Array.from({ length: view === "week" ? 7 : 1 }).map((__, c) => (
                    <div key={c} className="border-r p-1 last:border-r-0">
                      {(i + c) % 4 === 0 && <div className="h-full animate-pulse rounded bg-muted" />}
                    </div>
                  ))}
                </div>
              ))}
            </div>
          ) : (
            <>
              {/* Current time indicator line - overlay */}
              {isCurrentTimeVisible && currentTimePosition !== null && (
                <>
                  <div
                    className="absolute z-40 pointer-events-none"
                    style={{
                      /*
                       * Full width, from the gutter to the right edge of the grid — the original geometry, restored
                       * on request after the density pass had scoped it to today's column.
                       *
                       * The scoped version is a defensible read (15:47 is a fact about today, not about Sunday, and
                       * Google Agenda draws it that way) but it is not what this product's users had, and the line
                       * is the one mark on the screen that must not change under them. If it is ever revisited, the
                       * band is `calc(60px + ((100% - 60px) / 7) * todayColumnIndex)` wide `calc((100% - 60px) / 7)`
                       * — the same arithmetic contract as `weekBandLeftExpr`, since the week time grid only renders
                       * from `md:` up where `gutterPx` is that 60.
                       */
                      left: `${gutterPx}px`,
                      right: '0',
                      top: `${currentTimePosition}px`,
                      height: '2px',
                      marginTop: '-1px',
                    }}
                  >
                    {/* Saturated on a phone: it is the one mark that says « maintenant » on a screen showing a
                        single day, and the desktop's pale 300 disappears under a coloured block behind it. */}
                    <div className={cn("h-full shadow-sm", isNarrow ? "bg-destructive" : "bg-destructive/60")} />
                  </div>
                  {/* Current time dot on time column */}
                  <div
                    className="absolute z-40 pointer-events-none"
                    style={{
                      left: `${gutterPx - 14}px`,
                      top: `${currentTimePosition - 4}px`,
                    }}
                  >
                    <div className="w-2 h-2 rounded-full bg-destructive shadow-sm" />
                  </div>
                  {/*
                    The clock reading, in the gutter. Below `md:` only: on a phone the hour labels scroll far
                    enough out of view that the line alone says "now" without saying *when* now is. On desktop the
                    surrounding hour labels are always visible, so the same label would only add clutter.
                    Opaque background because it sits on top of the gutter's own hour text.
                  */}
                  <div
                    className="absolute z-40 pointer-events-none md:hidden"
                    style={{ left: '2px', top: `${currentTimePosition - 7}px` }}
                  >
                    <span className="rounded bg-card px-0.5 text-2xs font-bold tabular-nums text-destructive">
                      {format(currentTime, "HH:mm")}
                    </span>
                  </div>
                </>
              )}
              {/* Hour grid: time labels + empty clickable cells (gridlines + click-to-create). */}
              <div ref={hourGridRef} className={cn("grid", view === "week" ? WEEK_COLS : dayGridCols)}>
                {visibleHours.map((time) => {
                  const hour = Number.parseInt(time.split(":")[0])
                  // The label column has no single day in week view, so it reflects the focused date; each day
                  // cell below shades against its OWN window (Sunday no longer shades like Monday).
                  const labelWindow = openWindowFor(selectedDate, effectiveHours)
                  const isWorkingHours = isHourOpen(hour, labelWindow)

                  return (
                    <div key={time} className="contents">
                      {/* Time label. `leading-none` is load-bearing: without it this div inherits the global
                          line-height (1.5) and can outgrow the row. The appointment overlay positions blocks
                          against the fixed `hourHeight`, so any row taller than it makes appointments drift
                          upward (e.g. a 17:00 block landing near 14:00). Keep rows exactly `hourHeight` tall —
                          which is why the day cells below set `minHeight` from the value itself rather than
                          repeating the number in a Tailwind class, as they used to.

                          ⚠️ On a phone the gutter is a **bare column of times**, not a shaded panel: no fill, no
                          right border, no closed-hours tint. A filled 48px band down the edge of a 390px screen
                          reads as a second column of content competing with the appointments — it is the same
                          chrome-instead-of-data trade as the card frame. The hour lines and the day cells still
                          carry the grid; the shading of closed hours stays on the cells, where it means
                          something. */}
                      <div
                        className={cn(
                          // `sticky left-0` keeps the hour visible while the week scrolls sideways (AC-30) —
                          // a scrolled column with no time beside it is unreadable. z-30 puts it above the
                          // appointment blocks (z-20) and below the current-time dot (z-40), which is drawn
                          // just inside this very column.
                          "sticky left-0 z-30 px-2 py-2 text-right leading-none",
                          isNarrow
                            ? "bg-card"
                            : cn(
                                "border-b border-r bg-muted/60",
                                !isWorkingHours && "bg-muted/50",
                              ),
                        )}
                      >
                        <span
                          className={cn(
                            "font-medium",
                            isNarrow
                              ? "text-2xs text-muted-foreground"
                              : cn(
                                  "text-xs",
                                  isWorkingHours
                                    ? "text-foreground"
                                    : "text-muted-foreground",
                                ),
                          )}
                        >
                          {time}
                        </span>
                      </div>

                      {/* Day columns (empty — appointments render in the overlay below).
                          ⚠️ These stay plain `<div onClick>`, deliberately, and it is the one place in this file
                          where "make it a button" is the wrong answer: there are 24 of them in Jour and **168**
                          in Semaine, so giving each a `tabIndex` would put 168 tab stops between the toolbar and
                          the appointments — a keyboard user would have to traverse the empty grid to reach the
                          only things on it. The keyboard route to creating an appointment is « Nouveau
                          rendez-vous » (the toolbar button, and the phone header's own), which opens the
                          same dialog with the same date; the cell click is a pointer shortcut on top of it. */}
                      {view === "week"
                        ? weekDays.map((day) => {
                            // Each day shades against its OWN window — Sunday no longer shades like Monday.
                            const dayWindow = openWindowFor(day, effectiveHours)
                            const dayOpen = isHourOpen(hour, dayWindow)
                            return (
                              <div
                                key={`${day.toISOString()}-${time}`}
                                className={cn(
                                  "min-w-0 cursor-pointer border-b border-r transition-colors last:border-r-0 hover:bg-accent/30 dark:hover:bg-muted/50",
                                  /*
                                   * Today's COLUMN, not just its date pill.
                                   *
                                   * Seven identical columns give the eye nothing to land on, and the one thing
                                   * every user of a week grid is looking for first is today. The pill in the
                                   * header was the only mark, 8 px tall at the top of a grid that scrolls away
                                   * from it — so on a scrolled agenda there was no indication at all.
                                   *
                                   * ⚠️ The band and the closed-hours shading used to be **two `background-color`
                                   * utilities on the same element**, so which of them won on a closed hour of
                                   * today was settled by stylesheet order rather than by intent — and
                                   * `opacity-70` faded the cell's own gridlines with the fill, which is what made
                                   * a closed column read as a rendering fault. « Closed » is a hatch *image* now
                                   * (`hourCellBackground`), so the two compose: the colour says which column, the
                                   * hatch says which hours, and neither can delete the other.
                                   */
                                  isToday(day) && "bg-[var(--agenda-today)]",
                                )}
                                style={{ minHeight: hourHeight, backgroundImage: hourCellBackground(dayOpen) }}
                                /* The click is no longer here: the gesture hook decides press-versus-drag on
                                   release and calls back with the hour for a plain click, so the two cannot both
                                   fire on one gesture. */
                                onPointerDown={(event) =>
                                  gridDrag.beginCellGesture(event, format(day, "yyyy-MM-dd"), hour)
                                }
                                {...cellDataProps(day, hour)}
                              />
                            )
                          })
                        : (
                            <div
                              className={cn(
                                "cursor-pointer border-b transition-colors hover:bg-accent/30 dark:hover:bg-muted/50",
                                !isNarrow && "border-r",
                                // Jour paints the band too: the column IS the day, so when that day is today the
                                // grid should say so without the reader checking the header.
                                isToday(selectedDate) && "bg-[var(--agenda-today)]",
                              )}
                              style={{ minHeight: hourHeight, backgroundImage: hourCellBackground(isWorkingHours) }}
                              onPointerDown={(event) =>
                                gridDrag.beginCellGesture(event, format(selectedDate, "yyyy-MM-dd"), hour)
                              }
                              {...cellDataProps(selectedDate, hour)}
                            />
                          )}
                    </div>
                  )
                })}
              </div>

              {/* Appointment overlay (AC-4): each appointment rendered exactly once, positioned by its
                  start minute and sized proportionally to its duration. Absolute children of the `w-max`
                  wrapper — NOT of the scroll container — so their percentage widths mean the grid rather
                  than the viewport (see the AC-30 note above); the empty cells keep gridlines + clicks. */}
              {view === "week"
                ? weekDays.map((day, dayIndex) =>
                    computeOverlapLanes(getAppointmentsForDay(day)).map(
                      ({ appointment, colIndex, colCount, minutesToNextStart }) =>
                        renderAppointmentBlock(
                          appointment,
                          laneStyle(weekBandLeftExpr(dayIndex), weekBandWidthExpr, colIndex, colCount),
                          minutesToNextStart,
                        ),
                    ),
                  )
                : computeOverlapLanes(getAppointmentsForDay(selectedDate)).map(
                    ({ appointment, colIndex, colCount, minutesToNextStart }) =>
                      renderAppointmentBlock(
                        appointment,
                        laneStyle(dayBandLeftExpr, dayBandWidthExpr, colIndex, colCount),
                        minutesToNextStart,
                      ),
                  )}

              {/*
                The span being painted, and the block being carried.

                ⚠️ **`aria-hidden`, and that is not an oversight.** Both are transient states of a pointer gesture
                nobody can perform with a keyboard or a screen reader, and they change on every pointer move — a
                live region here would announce a new time several times a second. The accessible route to both
                capabilities is unchanged and is where it has always been: « Nouveau rendez-vous » for a booking,
                the edit dialog for a move (AC-11).

                ⚠️ `pointer-events-none` on both, for the same reason every block goes transparent while a drag is
                on: the drop target is resolved with `elementFromPoint`, and a preview under the cursor would hide
                the very cell it is previewing.
              */}
              {gridDrag.createSelection && createSelectionStyle && (
                <div
                  aria-hidden="true"
                  className="pointer-events-none absolute z-30 overflow-hidden rounded-md border-2 border-dashed border-primary bg-primary/10 px-1.5 py-0.5"
                  style={createSelectionStyle}
                >
                  <span className="truncate text-2xs font-semibold tabular-nums text-primary">
                    {clockOfMinutes(gridDrag.createSelection.fromMinutes)}
                    {gridDrag.createSelection.toMinutes > gridDrag.createSelection.fromMinutes &&
                      ` – ${clockOfMinutes(gridDrag.createSelection.toMinutes)} · ${
                        gridDrag.createSelection.toMinutes - gridDrag.createSelection.fromMinutes
                      } min`}
                  </span>
                </div>
              )}

              {gridDrag.moveDrag && moveGhostStyle && (
                <div
                  aria-hidden="true"
                  className="pointer-events-none absolute z-30 flex flex-col overflow-hidden rounded border-2 border-primary bg-primary/15 px-1.5 py-0.5 shadow-lg"
                  style={moveGhostStyle}
                >
                  <span className="truncate text-2xs font-semibold text-primary">
                    {gridDrag.moveDrag.appointment.patientName}
                  </span>
                  <span className="truncate text-2xs tabular-nums text-primary/80">
                    {clockOfMinutes(gridDrag.moveDrag.target.minutes)}
                  </span>
                </div>
              )}
            </>
          )}
          </div>
          </div>

          {/*
            The grid's footer: the escape hatch out of the trimmed window, and the sentence that explains why the
            grid starts where it starts.

            ⚠️ **The disclosure is what makes the trimming honest.** Hiding hours the clinic does not open is only
            acceptable if the user can still reach them — § 0 of the device contract — and `gridWindow` already
            guarantees nothing *booked* is hidden. What remains is booking INTO a closed hour: an emergency at
            06:30 on a day with no appointment yet. That is what this button is for, and it says which hours it is
            currently showing so the state is never silent.

            Rendered at every width (a phone needs the 06:30 slot more than a desk machine, not less), and it is a
            real `min-h-11` control on a coarse pointer rather than a 24 px link.

            ⚠️ **It is also where « aucun rendez-vous » now lives**, which is why the strip renders whenever the
            range is empty and not only when the window is trimmed. That sentence used to be an `inset-0` overlay
            card: it read as a modal, it sat on top of the very hours the user was trying to click, and — with the
            grid now also answering to a drag across those hours — it covered the whole gesture. A quiet line under
            the grid states the same fact and takes nothing away, and « Nouveau rendez-vous » keeps its two
            existing homes (the bar's own button, and the phone header's), so the overlay's duplicate went
            with the overlay.

            ⚠️ Suppressed while `loading` **or** `error` is set. The banner above already says the fetch failed;
            « aucun rendez-vous » printed beside it is exactly the false statement that banner exists to prevent,
            and it would be the more emphatic of the two.
          */}
          {(() => {
            const showWindowDisclosure = isTrimmed || showFullDay
            const showEmptySentence = !loading && !error && emptyRange
            if (!showWindowDisclosure && !showEmptySentence) return null

            return (
              <div className="flex flex-shrink-0 flex-wrap items-center gap-x-3 gap-y-1 border-t px-3 py-1.5">
                {showWindowDisclosure && (
                  <>
                    <button
                      type="button"
                      onClick={() => setShowFullDay((full) => !full)}
                      aria-expanded={showFullDay}
                      className="touch-target inline-flex items-center gap-1.5 rounded-md text-xs font-medium text-muted-foreground transition-colors hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                    >
                      <ChevronDown className={cn("h-3.5 w-3.5 transition-transform", showFullDay && "rotate-180")} aria-hidden="true" />
                      {showFullDay ? "Réduire aux horaires d'ouverture" : "Afficher les 24 heures"}
                    </button>
                    <span className="text-2xs text-muted-foreground">
                      {showFullDay
                        ? "00:00 – 24:00"
                        : `Affichage ${hourSlot(gridWindow.fromHour)} – ${hourSlot(gridWindow.toHour === 24 ? 0 : gridWindow.toHour)}`}
                    </span>
                  </>
                )}
                {showEmptySentence && (
                  <p className="text-2xs text-muted-foreground">
                    <span className="font-medium text-foreground">
                      {view === "week" ? "Aucun rendez-vous cette semaine" : "Aucun rendez-vous ce jour-là"}
                    </span>{" "}
                    {/* Two spellings of the same hint rather than one that is wrong on half the devices: this app
                        is used on a phone in a corridor and on a desk machine at reception. */}
                    <span className="md:hidden">— touchez une heure, ou faites glisser sur plusieurs</span>
                    <span className="hidden md:inline">— cliquez sur une heure, ou faites glisser sur plusieurs</span>
                  </p>
                )}
              </div>
            )
          })()}
        </div>
        )}
      </Card>

      {/*
        The three refusals a drop can meet, in one dialog.

        ⚠️ **The two advisory codes quote the server verbatim.** `slot_taken` and `outside_working_hours` are
        refusals the backend words itself — « Dr X : le cabinet est fermé le samedi. » — and confirming re-sends the
        same request carrying the acknowledgement, which is what records the exception on the appointment rather
        than silently allowing it. Rewording them here would be a second copy of a rule the server owns, and
        branching on the French would break the day somebody fixed a typo.

        ⚠️ **Past-time is ours, because no backend refuses it.** Both booking dialogs already ask this question
        client-side; this is a third caller of the same question, not a new rule — which is also why it is checked
        *before* the request rather than being read out of a response that will never carry it.

        Cancelling leaves the appointment exactly where it was: nothing was ever optimistically moved, so the block
        is already at its saved time and there is no snap-back to perform (AC-6).
      */}
      <AlertDialog
        open={movePrompt !== null}
        onOpenChange={(open) => {
          if (open) return
          setMovePrompt(null)
          pendingMoveRef.current = null
        }}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>
              {movePrompt?.kind === "overlap"
                ? "Créneau déjà occupé"
                : movePrompt?.kind === "hours"
                  ? "En dehors des horaires d'ouverture"
                  : "Heure dans le passé"}
            </AlertDialogTitle>
            <AlertDialogDescription>{movePrompt?.message}</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Annuler</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                const kind = movePrompt?.kind
                setMovePrompt(null)
                if (!kind) return
                // Merged into the grants already given, never replacing them: a drop can trip all three, and each
                // confirmation re-sends the whole request.
                if (kind === "overlap") moveGrantsRef.current.overlap = true
                else if (kind === "hours") moveGrantsRef.current.hours = true
                else moveGrantsRef.current.past = true
                void submitMove()
              }}
            >
              Déplacer quand même
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
