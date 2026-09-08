"use client"

import { useId, useState } from "react"
import { ChevronRight, ClipboardPlus } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { formatDateTime } from "@/lib/format"
import { appointmentActsSummary, normalizeStatus } from "@/components/appointment-labels"
import type { AppointmentDto, DentalRecordDto } from "@/lib/api/types"

interface PatientUndocumentedVisitsProps {
  /** The patient's appointments, already loaded by the page. */
  appointments: AppointmentDto[]
  /** The patient's fiches, already loaded by the page — their `appointmentId` is what marks a visit documented. */
  records: DentalRecordDto[]
  /** Opens the fiche editor for that visit (threads the id so saving also closes its post-visit prompt). */
  onRecord: (appointmentId: string) => void
}

/** Rows visible before the list starts scrolling, in the default state, from `sm:` up. */
const VISIBLE_ROWS = 3

/**
 * The same cap below `sm:`.
 *
 * This section sits between the top of the patient page and the odontogramme, and on a phone it was the largest
 * thing there: seven pending visits rendered **182 px and eight controls** — seven « Enregistrer la fiche » plus
 * « Tout afficher » — of the **698 px and 18 buttons** standing above a chart the page exists for. One row is
 * still the whole point of the section (« you owe a fiche, and here is the most recent one »), the count badge in
 * the header states the debt in full whatever is drawn, and « Tout afficher » opens the lot.
 *
 * ⚠️ The cap is CSS (`max-h`), never `useMediaQuery`: that hook returns `false` until after mount by contract, so
 * a JS switch would paint three rows and then snap to one on every phone load.
 */
const NARROW_VISIBLE_ROWS = 1

/**
 * Height of one row: a `h-7` button (28 px) plus `py-1` (8 px).
 *
 * A row-count cap is honest here, unlike in the notes band where it was not: every row is a single line, because the
 * act summary carries `truncate`. So `VISIBLE_ROWS × ROW_PX` really is what three rows occupy, and the « tout
 * afficher » control can key off `length > VISIBLE_ROWS` rather than having to measure what got clipped.
 */
const ROW_PX = 36

const LIST_MAX_PX = VISIBLE_ROWS * ROW_PX
const NARROW_LIST_MAX_PX = NARROW_VISIBLE_ROWS * ROW_PX

/*
 * The caps are applied as the literal Tailwind utilities `max-h-[36px] sm:max-h-[108px]` (see the note at the
 * scroll box). These two lines are what make a change to `ROW_PX` or to either row count a **build failure**
 * rather than a chart that silently clips at the wrong height: a literal class cannot be derived, so the only
 * available guard is to state the derivation and check it.
 */
if (NARROW_LIST_MAX_PX !== 36 || LIST_MAX_PX !== 108) {
  throw new Error(
    `Les hauteurs de la liste ont changé (${NARROW_LIST_MAX_PX}/${LIST_MAX_PX}) — mettez à jour les classes max-h littérales.`,
  )
}

/** A visit in one of these states is not waiting for a fiche — nothing happened, or it was called off. */
const NOT_EXPECTED = new Set(["Cancelled", "NoShow"])

/**
 * « À compléter » — past visits with no fiche de soins yet.
 *
 * The point is that a fiche is written *after* the patient has left, which is exactly when it gets forgotten: nothing
 * on this page used to say a séance had gone undocumented, so the omission was invisible until someone went looking
 * through the agenda. The in-app post-visit prompt fires once, for one user, and is dismissible — this is the
 * standing, patient-scoped view of the same debt.
 *
 * **Three states, two controls, each with exactly one job:**
 *
 * 1. *default* — open, capped at {@link VISIBLE_ROWS} rows, the rest reachable by scrolling;
 * 2. *collapsed* — title, count and nothing else, via the header chevron;
 * 3. *expanded* — the whole list at once, via the footer link, which only appears when there is more than fits.
 *
 * One control cycling three states would be a guessing game, so the chevron only ever answers « show this section or
 * not » and the footer link only ever answers « how much of it ». The count badge stays visible in every state, which
 * is what makes collapsing safe: a collapsed section that still says « 5 » is deferring, not hiding.
 *
 * ⚠️ **Documented means a fiche points at the appointment** — `DentalRecordDto.appointmentId` — not that the
 * appointment is `Completed`. Status cannot answer this: creating a *medical document* marks a visit completed, and so
 * does the edit dialog's « Terminer », either of which would hide a visit that still has no fiche. That is the whole
 * failure this section exists to catch, so keying on status would have made it blind to its own purpose. (The link is
 * newly stored — the column existed unpopulated for weeks; see `ReconcileDentalRecordAppointmentLink`.)
 *
 * ⚠️ A past appointment left `Scheduled` is listed too, deliberately. Either the visit happened and needs its fiche,
 * or it did not and needs marking `NoShow`/`Cancelled` — both are the practitioner's attention, which is what the
 * section claims to be about. Renders **nothing at all** when the list is empty, so it costs no space in the steady
 * state.
 */
export function PatientUndocumentedVisits({
  appointments,
  records,
  onRecord,
}: PatientUndocumentedVisitsProps) {
  const [collapsed, setCollapsed] = useState(false)
  const [showAll, setShowAll] = useState(false)
  const panelId = useId()

  const documented = new Set(records.map((r) => r.appointmentId).filter(Boolean) as string[])
  const now = Date.now()

  const pending = appointments
    .filter((a) => {
      if (documented.has(a.id)) return false
      if (NOT_EXPECTED.has(normalizeStatus(a.status))) return false
      return new Date(a.appointmentDateTime).getTime() < now
    })
    // Most recent first: the visit that just happened is the one most likely still in the practitioner's head.
    .sort(
      (a, b) => new Date(b.appointmentDateTime).getTime() - new Date(a.appointmentDateTime).getTime(),
    )

  if (pending.length === 0) return null

  const hasMoreThanFits = pending.length > VISIBLE_ROWS
  /*
   * ⚠️ The footer's condition is per-WIDTH, because the cap now is. `pending.length > VISIBLE_ROWS` alone would
   * have withheld « Tout afficher » from a phone showing 1 of 2 or 1 of 3 rows — a list clipped with no control
   * to open it, which is § 0's « no capability removed by a layout decision » exactly. So it renders whenever
   * more than the *narrow* cap is pending and is hidden from `sm:` up in the band where the desktop cap already
   * shows everything: `pending.length` is 2 or 3, i.e. more than `NARROW_VISIBLE_ROWS` and no more than
   * `VISIBLE_ROWS`.
   */
  const hasMoreThanNarrowFits = pending.length > NARROW_VISIBLE_ROWS

  return (
    <section className="rounded-lg border border-primary/30 bg-primary/5">
      {/* The whole header row is the collapse trigger. `min-h-9` floors it so collapsing does not change the
          section's header height, only what hangs below it. */}
      <button
        type="button"
        onClick={() => setCollapsed((v) => !v)}
        aria-expanded={!collapsed}
        aria-controls={panelId}
        // `touch-target` raises the collapse trigger to the 44px floor on a finger without repainting the row
        // (AC-10) — `min-h-9` is 36px, and this header is the only control that reveals the section again.
        className="touch-target flex min-h-9 w-full items-center gap-2 rounded-lg px-3 pt-2 pb-1 text-left transition-colors duration-150 ease-out hover:bg-primary/10 active:bg-primary/15 motion-reduce:transition-none"
      >
        <ChevronRight
          aria-hidden="true"
          className={cn(
            "h-4 w-4 shrink-0 text-primary transition-transform duration-200 ease-[cubic-bezier(0.23,1,0.32,1)] motion-reduce:transition-none",
            !collapsed && "rotate-90",
          )}
        />
        <ClipboardPlus aria-hidden="true" className="h-4 w-4 shrink-0 text-primary" />
        <h2 className="text-2xs font-semibold uppercase tracking-wide text-primary">
          À compléter — séances sans fiche
        </h2>
        {/* Visible in every state — this is what makes the collapsed state a deferral rather than a hiding place. */}
        <Badge variant="secondary" className="h-5 px-1.5 text-xs font-normal tabular-nums">
          {pending.length}
        </Badge>
      </button>

      {/* Height animates via `grid-template-rows: 0fr → 1fr` — the only way to transition to *content* height in CSS
          without measuring in JS, which is why the single `overflow-hidden` child below is required. */}
      <div
        id={panelId}
        className={cn(
          "grid transition-[grid-template-rows,opacity] duration-200 ease-[cubic-bezier(0.23,1,0.32,1)] motion-reduce:transition-none",
          collapsed ? "grid-rows-[0fr] opacity-0" : "grid-rows-[1fr] opacity-100",
        )}
      >
        <div className="overflow-hidden">
          <div
            className={cn(
              "px-3",
              showAll ? "overflow-visible" : "overflow-y-auto",
              /*
               * ⚠️ **Written out, never interpolated.** Tailwind v4 finds classes by scanning source text for
               * literal names, so `max-h-[${NARROW_LIST_MAX_PX}px]` generates **no CSS at all** — the cap would
               * simply not exist, silently, with the class sitting in the DOM looking correct. The two values
               * are `NARROW_VISIBLE_ROWS × ROW_PX` = 36 and `VISIBLE_ROWS × ROW_PX` = 108; the assertion just
               * below is what keeps them honest if a row's height or a cap ever changes.
               */
              !showAll && "max-h-[36px] sm:max-h-[108px]",
            )}
          >
            <ul className="divide-y divide-primary/15">
              {pending.map((appointment) => (
                <li key={appointment.id} className="flex items-center gap-2 py-1">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm text-foreground">
                      <span className="mr-1.5 whitespace-nowrap text-xs tabular-nums text-muted-foreground">
                        {formatDateTime(appointment.appointmentDateTime)}
                      </span>
                      {/* The acts booked for the visit, so the row says what the fiche is *for*. */}
                      {appointmentActsSummary(appointment) || "Séance"}
                    </p>
                  </div>
                  <Button
                    size="sm"
                    variant="outline"
                    className="h-7 shrink-0 px-2 text-xs"
                    onClick={() => onRecord(appointment.id)}
                  >
                    Enregistrer la fiche
                  </Button>
                </li>
              ))}
            </ul>
          </div>

          {/* Only when there is genuinely more than fits — otherwise the control would promise something it cannot
              deliver. A sibling of the scroll box, never inside it, so revealing it cannot resize what it describes. */}
          {hasMoreThanNarrowFits ? (
            // A bare underlined `<button>` was a ~16px-tall target. `variant="link"` keeps exactly the look
            // (primary ink, underline, no press-scale) and inherits the 44px `touch-target` floor from
            // `buttonVariants`; the negative margins keep the row's original spacing.
            <Button
              type="button"
              variant="link"
              size="sm"
              onClick={() => setShowAll((v) => !v)}
              aria-expanded={showAll}
              className={cn(
                "mb-2 mt-1 h-auto py-1 text-xs font-semibold",
                !hasMoreThanFits && "sm:hidden",
              )}
            >
              {showAll ? "Réduire la liste" : `Tout afficher (${pending.length})`}
            </Button>
          ) : (
            <div className="pb-2" />
          )}
          {/* The bottom breathing room the footer would have provided, in the band where it is hidden. */}
          {hasMoreThanNarrowFits && !hasMoreThanFits && <div className="hidden pb-2 sm:block" />}
        </div>
      </div>
    </section>
  )
}
