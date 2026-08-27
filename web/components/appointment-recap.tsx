"use client"

import { useMemo, type ReactNode } from "react"
import { format } from "date-fns"
import { fr } from "date-fns/locale"
import { cn } from "@/lib/utils"

/**
 * The advisory clash the booking dialogs compute, in the one shape this panel renders it.
 *
 * <p>`samePractitioner` is not a severity flag we invent here — it is the hook's own distinction between « ce
 * praticien est déjà pris » (destructive ink, the server will ask for a confirmation) and « quelqu'un d'autre
 * l'est » (amber, purely informative).</p>
 */
export interface AppointmentRecapWarning {
  message: string
  samePractitioner: boolean
}

/**
 * Everything this panel states, and nothing it does not.
 *
 * ⚠️ **Every field is derived from what the form already holds.** The panel performs no fetch of its own and
 * asserts nothing the user cannot see one column to the left — which is what makes it safe to collapse it to a
 * strip below `lg:` (and, on a phone, to a strip carrying less). A panel holding a fact available nowhere else
 * would be a capability removed by a layout decision.
 */
export interface AppointmentRecapModel {
  /**
   * What is being booked.
   *
   * ⚠️ **Separate from `patientName` on purpose.** Reading a null name as « créneau occupé » made the panel say
   * so on every freshly-opened form, i.e. it asserted a block while the control above it said « Patient » — two
   * opposite facts with one rendering. A visit with no patient *yet* and a slot that will never have one are
   * different states and get different words.
   */
  kind: "patient" | "busy"
  /** Who the visit is for; `null` while nobody has been chosen, and always `null` for a « créneau occupé ». */
  patientName: string | null
  /** The lead act's colour — the same hue the agenda will paint the block with. */
  colorHex?: string | null
  date?: Date
  startHour: string
  startMinute: string
  durationMinutes: number
  /** The séance's acts, in the dentist's order. Empty is a real state (a visit booked with no act). */
  actNames: string[]
  doctorName?: string | null
  warning?: AppointmentRecapWarning | null
}

const NEUTRAL_HEX = "#6C757D"

function pad(n: number): string {
  return String(n).padStart(2, "0")
}

/**
 * « 10:00 → 11:10 ».
 *
 * <p>Wraps past midnight rather than printing a 25th hour: a visit booked at 23:30 for an hour genuinely ends at
 * 00:30, and the agenda draws it that way.</p>
 */
export function formatTimeSpan(startHour: string, startMinute: string, durationMinutes: number): string {
  const start = Number.parseInt(startHour) * 60 + Number.parseInt(startMinute)
  const startLabel = `${pad(Math.floor(start / 60) % 24)}:${pad(start % 60)}`
  if (!Number.isFinite(durationMinutes) || durationMinutes <= 0) return startLabel
  const end = start + durationMinutes
  return `${startLabel} → ${pad(Math.floor(end / 60) % 24)}:${pad(end % 60)}`
}

/** « 1 h 10 » / « 45 min » — the length said in words, beside the span that already implies it. */
export function formatDurationFr(minutes: number): string {
  if (!Number.isFinite(minutes) || minutes <= 0) return "—"
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h > 0 && m > 0) return `${h} h ${pad(m)}`
  if (h > 0) return `${h} h`
  return `${m} min`
}

function useRecapText(model: AppointmentRecapModel) {
  return useMemo(() => {
    const name = model.patientName?.trim() || null
    /**
     * The three states kept apart, in words rather than by an absence: a real name, a block, and a form nobody
     * has filled in yet. The last one is `null` here so the callers can render it muted.
     */
    const who = model.kind === "busy" ? "Créneau occupé" : name
    const dayLabel = model.date ? format(model.date, "EEEE d MMMM", { locale: fr }) : null
    const shortDayLabel = model.date ? format(model.date, "EEE d MMM", { locale: fr }) : null
    const span = formatTimeSpan(model.startHour, model.startMinute, model.durationMinutes)
    return { who, dayLabel, shortDayLabel, span }
  }, [model.kind, model.patientName, model.date, model.startHour, model.startMinute, model.durationMinutes])
}

/**
 * The clash, in whichever of the two tones the hook asked for. Shared by both variants so a warning cannot read
 * one way in the rail and another in the strip.
 */
function RecapWarning({ warning, compact = false }: { warning: AppointmentRecapWarning; compact?: boolean }) {
  return (
    <div
      className={cn(
        "rounded-md border px-3 py-2 text-xs leading-snug",
        warning.samePractitioner
          ? "border-destructive/40 bg-destructive-wash text-destructive"
          : "border-warning/40 bg-warning-wash text-warning-ink",
      )}
    >
      <p>⚠ {warning.message}</p>
      {warning.samePractitioner && !compact && (
        <p className="mt-1 text-2xs">Vous pouvez continuer : une confirmation vous sera demandée.</p>
      )}
    </div>
  )
}

/** The agenda block as it will be painted — the panel's anchor, and the only place the act colour appears. */
function RecapBlock({ model, dayLabel, span, who }: {
  model: AppointmentRecapModel
  dayLabel: string | null
  span: string
  who: string | null
}) {
  return (
    <div
      className="rounded-md border border-l-4 bg-background p-3"
      style={{ borderLeftColor: model.colorHex || NEUTRAL_HEX }}
    >
      <p className="text-sm font-semibold leading-tight">
        {who ?? <span className="font-normal text-muted-foreground">Patient à choisir</span>}
      </p>
      <p className="mt-1 text-xs text-muted-foreground tabular-nums">
        {dayLabel ? `${dayLabel} · ${span}` : span}
      </p>
      {model.actNames.length > 0 && (
        <p className="mt-1 text-xs text-muted-foreground">{model.actNames.join(" · ")}</p>
      )}
    </div>
  )
}

function RecapRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-3 text-xs">
      <span className="text-muted-foreground">{label}</span>
      <span className="text-end font-medium">{value}</span>
    </div>
  )
}

interface AppointmentRecapProps {
  model: AppointmentRecapModel
  /**
   * `rail` is the sticky right-hand pane at `lg:` and above; `bar` is the strip it becomes below that, pinned
   * between the scrolling form and the footer. Two renderings of one model, never two sources of truth.
   */
  variant: "rail" | "bar"
  className?: string
  /**
   * Extra read-only sections for the rail — the edit dialog's statut and facturation.
   *
   * ⚠️ **Read-only on purpose.** Anything actionable belongs in the form column, which is the half that survives
   * the collapse below `lg:`; an action living only here would disappear on every tablet and phone.
   */
  children?: ReactNode
}

/**
 * « Le rendez-vous tel qu'il sera » — the live recapitulation beside the booking form.
 *
 * <p>It exists for two defects at once. The dialogs had **no summary of any kind**, so on an agenda several people
 * fill there was no moment at which « Amine Trabelsi · vendredi 14 août · 10:00 → 11:10 · 2 actes » could be
 * checked before committing. And the overlap warning was rendered inside the form behind a **reserved
 * `min-h-[2.5rem]` gap** that stayed empty most of the time — a permanent ~60 px hole under the duration presets
 * that read as a rendering fault. Giving the clash a permanent home is what lets that reservation go.</p>
 *
 * <p>Shared by both booking dialogs, so « what a rendez-vous is » is described once.</p>
 */
export function AppointmentRecap({ model, variant, className, children }: AppointmentRecapProps) {
  const { who, dayLabel, shortDayLabel, span } = useRecapText(model)

  if (variant === "bar") {
    return (
      <div
        className={cn(
          "flex shrink-0 flex-col gap-2 border-t bg-muted/40 px-6 py-2.5",
          className,
        )}
      >
        <div className="flex items-center gap-2.5">
          <span
            className="h-8 w-1 shrink-0 rounded-full"
            style={{ backgroundColor: model.colorHex || NEUTRAL_HEX }}
            aria-hidden="true"
          />
          <div className="min-w-0 flex-1">
            <p className="truncate text-xs font-semibold leading-tight">
              {who ?? <span className="font-normal text-muted-foreground">Patient à choisir</span>}
            </p>
            <p className="truncate text-2xs text-muted-foreground tabular-nums">
              {shortDayLabel ? `${shortDayLabel} · ${span}` : span}
              {model.actNames.length > 0 && ` · ${model.actNames.length} acte${model.actNames.length > 1 ? "s" : ""}`}
            </p>
          </div>
        </div>
        {model.warning && <RecapWarning warning={model.warning} compact />}
      </div>
    )
  }

  return (
    <aside
      className={cn(
        "flex flex-col gap-3 overflow-y-auto border-s bg-muted/30 p-4",
        className,
      )}
      aria-label="Récapitulatif du rendez-vous"
    >
      <p className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">Récapitulatif</p>
      <RecapBlock model={model} dayLabel={dayLabel} span={span} who={who} />
      {model.warning && <RecapWarning warning={model.warning} />}
      <div className="flex flex-col gap-1.5">
        <RecapRow label="Durée" value={formatDurationFr(model.durationMinutes)} />
        <RecapRow
          label="Actes"
          value={model.actNames.length > 0 ? model.actNames.length : <span className="text-muted-foreground">Aucun</span>}
        />
        <RecapRow
          label="Praticien"
          value={model.doctorName || <span className="text-muted-foreground">Aucun</span>}
        />
      </div>
      {children}
    </aside>
  )
}

/** A titled read-only block inside the rail — the edit dialog's statut and facturation sections. */
export function AppointmentRecapSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="flex flex-col gap-1.5 border-t pt-3">
      <p className="text-2xs font-semibold uppercase tracking-wider text-muted-foreground">{title}</p>
      {children}
    </div>
  )
}
