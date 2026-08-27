"use client"

import Link from "next/link"
import { Ban, ClipboardCheck } from "lucide-react"
import { actSolidStyle } from "@/lib/dashboard/act-colour"
import {
  formatClock,
  formatDuration,
  type DaySlot,
  type DaySummary,
} from "@/lib/dashboard/day-summary"
import { appointmentActsSummary, isBusySlot } from "@/components/appointment-labels"
import { cn } from "@/lib/utils"

interface NowNextCardsProps {
  summary: DaySummary
  /** Minutes from local midnight, so « dans 1 h 50 » is computed against the same instant the summary was. */
  nowMinutes: number
}

/**
 * Zone 2 of the day board — who is in the chair, and who is next.
 *
 * <p>The single most actionable thing on the screen, and the reason the day zones exist at all. Both cards are
 * light surfaces with a 4 px top edge in the act's own colour; the live one additionally takes the accent wash.
 * Nothing here is a dark panel: a filled surface is designed to carry <i>one</i> number, and these carry a name,
 * an act, a time and a delay.</p>
 *
 * <p>⚠️ <b>The internals are stacked at every width, deliberately.</b> A name-then-time row is what clipped
 * « Yassine Chaabane » at 320 px, and a patient's name is an identity — it wraps
 * (`[overflow-wrap:anywhere]`), it never receives an ellipsis. The time moves to a foot row behind a hairline,
 * where it has the width to be large.</p>
 *
 * <p>⚠️ <b>Both cards show a delay, not only a clock time.</b> « dans 1 h 50 » is what a dentist reads; « 13:30 »
 * alone makes them do the subtraction. The clock stays because it is what they will say out loud to the patient.</p>
 *
 * <p>⚠️ <b>A « créneau occupé » is not a visit, and must not borrow one word of this card.</b> It carries no
 * patient, so « Au fauteuil », the accent wash and the pulsing dot each assert something false — somebody is
 * being treated right now. The blocked branch says so plainly, in the amber the agenda already paints such a
 * slot, and puts the practitioner's own note where the act name would be: that note is the only place the
 * reason for the block lives.</p>
 *
 * <p>⚠️ <b>There is a third state, and it exists because the first one used to lie.</b> A séance whose slot ended
 * long ago and which nobody has answered for is <i>not</i> in the chair — it is « Séance à clôturer », in amber,
 * pointing at `/a-cloturer`. Dropping the card instead would have been simpler and worse: the à-traiter chip counts
 * those séances but cannot name one, and the patient's name is the whole reason this zone is the most useful thing
 * on the screen.</p>
 */
export function NowNextCards({ summary, nowMinutes }: NowNextCardsProps) {
  const { current, next, needsClosure } = summary
  if (!current && !next && !needsClosure) return null

  return (
    <div className="grid gap-3 sm:grid-cols-2 sm:gap-4">
      {current && <SlotCard slot={current} nowMinutes={nowMinutes} kind="chair" />}
      {/* `needsClosure` is already null while anything holds the chair, so these two are never both present. */}
      {needsClosure && <SlotCard slot={needsClosure} nowMinutes={nowMinutes} kind="closure" />}
      {next && <SlotCard slot={next} nowMinutes={nowMinutes} kind="next" />}
    </div>
  )
}

/** Which question this card answers: who is being treated, what is owed an answer, or who is coming. */
type SlotCardKind = "chair" | "closure" | "next"

function SlotCard({
  slot,
  nowMinutes,
  kind,
}: {
  slot: DaySlot
  nowMinutes: number
  kind: SlotCardKind
}) {
  const { appointment } = slot
  const live = kind === "chair"
  const closure = kind === "closure"
  // A « créneau occupé » is `Completed` by the elapse pass and is filtered out of `needsClosure`, so only the
  // chair and next cards can ever carry one.
  const busy = isBusySlot(appointment)
  const acts = appointmentActsSummary(appointment)
  const duration = slot.endMinutes - slot.startMinutes
  // Measured from the slot's END for a closure card: « terminée depuis 2 h 59 » is the fact that makes it overdue,
  // where « depuis » its start would be the length of a séance nobody is having.
  const delta = closure
    ? nowMinutes - slot.endMinutes
    : live
      ? nowMinutes - slot.startMinutes
      : slot.startMinutes - nowMinutes
  // The block's own note is the reason it exists (« réunion », « congé ») and lives nowhere else.
  const reason = appointment.notes?.trim() || undefined
  const accented = live && !busy

  return (
    <Link
      href={closure ? "/a-cloturer" : `/appointments?appointmentId=${appointment.id}`}
      className={cn(
        "group relative block overflow-hidden rounded-xl border p-4 shadow-sm transition-shadow sm:p-5",
        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
        "hover-hover:hover:shadow-md",
        closure
          ? "border-warning/30 bg-warning-wash"
          : accented
            ? "border-primary/25 bg-accent"
            : "border-border bg-card",
      )}
      aria-label={
        closure
          ? `Séance à clôturer : ${appointment.patientName}, terminée depuis ${formatDuration(Math.max(0, Math.round(delta)))}. Ouvrir les séances à clôturer.`
          : busy
            ? `Créneau bloqué à ${formatClock(slot.startMinutes)}${reason ? ` : ${reason}` : ""}. Ouvrir l'agenda.`
            : `${live ? "Au fauteuil" : "Prochain rendez-vous"} : ${appointment.patientName} à ${formatClock(slot.startMinutes)}. Ouvrir la fiche.`
      }
    >
      {/* The act's colour, at full strength — a 4px edge is exactly the small mark that a pastel would erase.
          A blocked slot has no act, so it takes the amber the agenda paints it in. */}
      <span
        aria-hidden="true"
        className="absolute inset-x-0 top-0 h-1"
        style={busy || closure ? { background: "var(--warning)" } : actSolidStyle(slot.colorHex)}
      />

      <p
        className={cn(
          "flex items-center gap-2 font-mono text-2xs font-medium uppercase tracking-[0.12em]",
          closure ? "text-warning-ink" : accented ? "text-primary" : "text-muted-foreground",
        )}
      >
        {closure ? (
          <ClipboardCheck aria-hidden="true" className="size-3 shrink-0" />
        ) : busy ? (
          <Ban aria-hidden="true" className="size-3 shrink-0" />
        ) : (
          live && (
            // Only a live card pulses. On a séance that ended hours ago a pulsing « live » dot is the same
            // false claim the unbounded chair rule was making.
            <span
              aria-hidden="true"
              className="size-1.5 shrink-0 rounded-full bg-success ring-3 ring-success/25 motion-safe:animate-pulse"
            />
          )
        )}
        {closure
          ? "Séance à clôturer"
          : busy && live
            ? "Créneau bloqué"
            : live
              ? "Au fauteuil"
              : "Ensuite"}
      </p>

      {/* Wraps, never truncates. At 320px this column is ~230px — about 30 characters — and a clipped name is
          not a weaker label, it is a different person. */}
      <p className="mt-2 text-lg font-semibold leading-snug tracking-tight text-foreground [overflow-wrap:anywhere]">
        {busy ? "Indisponible" : appointment.patientName}
      </p>

      <p className="mt-1.5 flex items-center gap-2 text-sm text-muted-foreground">
        {!busy && (
          <span
            aria-hidden="true"
            className="size-2.5 shrink-0 rounded-[3px]"
            style={actSolidStyle(slot.colorHex)}
          />
        )}
        <span className="min-w-0 [overflow-wrap:anywhere]">
          {busy ? (reason ?? "Créneau bloqué") : (acts ?? "Rendez-vous")} · {formatDuration(duration)}
        </span>
      </p>

      <p
        className={cn(
          "mt-3 flex items-baseline justify-between gap-3 border-t pt-3",
          closure ? "border-warning/25" : accented ? "border-primary/20" : "border-border",
        )}
      >
        <span className="font-mono text-xl font-semibold tabular-nums tracking-tight text-foreground">
          {formatClock(slot.startMinutes)}
        </span>
        <span className={cn("text-end text-xs", closure ? "text-warning-ink" : "text-muted-foreground")}>
          {relativeLabel(delta, kind)}
        </span>
      </p>
    </Link>
  )
}

/**
 * « dans 1 h 50 » / « depuis 25 min » / « terminée depuis 3 h ».
 *
 * <p>A visit that should have started is « en retard de … » rather than « dans −5 min »: the clock does not run
 * backwards, and a negative delay is exactly the case a practice needs named.</p>
 */
function relativeLabel(deltaMinutes: number, kind: SlotCardKind): string {
  const rounded = Math.round(deltaMinutes)
  if (kind === "closure") {
    // Measured from the slot's end, and it is only ever rendered past the grace — so this is never « à l'instant ».
    return `terminée depuis ${formatDuration(Math.max(0, rounded))}`
  }
  if (kind === "chair") {
    if (rounded < 1) return "commence maintenant"
    return `depuis ${formatDuration(rounded)}`
  }
  if (rounded <= 0) return `en retard de ${formatDuration(Math.abs(rounded))}`
  if (rounded < 1) return "dans un instant"
  return `dans ${formatDuration(rounded)}`
}
