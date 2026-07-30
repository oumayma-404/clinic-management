"use client"

import type { ReactNode } from "react"
import { AlertTriangle, StickyNote, Pencil } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { formatDate } from "@/lib/format"
import type { PatientDto, DentalRecordDto } from "@/lib/api/types"

interface PatientNotesStripProps {
  patient: PatientDto
  /** Already loaded by the page; only fiches carrying notes are listed. */
  records: DentalRecordDto[]
  onEdit: () => void
}

/**
 * Shared ceiling for **both** panel bodies, in pixels.
 *
 * One constant, used twice, is what makes the row balanced: two halves that cap at different heights are not halves.
 * Beyond it each body scrolls in place, so the row's height is fixed regardless of how much either side holds — which
 * is the property the odontogram below depends on. ~120 px ≈ six lines of the small type used here.
 */
const PANEL_BODY_MAX_PX = 120

/** A patient-level warning is chip-shaped only if it is short — see {@link splitPatientWarnings}. */
const MAX_CHIP_LENGTH = 48

/**
 * Split the patient's free-text important notes into chips, or decide they are prose and leave them alone.
 *
 * The field is one textarea and practitioners type a keyword list into it — « - sida ⏎ - comportement agressif ».
 * Rendered as a paragraph that costs one line per keyword; as chips the same two facts fit on one line.
 *
 * The bail-out is the important half. A dentist who writes a sentence (« prémédication obligatoire, voir le
 * courrier du cardiologue ») must not have it stuffed into a pill, so chips are used only when the field really is
 * a short list — several pieces, each brief — and anything else falls through to a paragraph.
 */
function splitPatientWarnings(importantNotes: string): string[] | null {
  const pieces = importantNotes
    .split("\n")
    // Strip the bullet the user typed themselves; the chip's own shape is the bullet now.
    .map((line) => line.replace(/^\s*[-–—•*]\s*/, "").trim())
    .filter(Boolean)

  const chipShaped = pieces.length > 1 && pieces.every((p) => p.length <= MAX_CHIP_LENGTH)
  return chipShaped ? pieces : null
}

/** One note from one séance, carrying what identifies the visit it came from. */
interface SessionNote {
  key: string
  date: string
  /** The séance's derived act summary — « Traitement de canal (dévitalisation) ». */
  procedure: string
  text: string
}

/** Flatten `records` into one dated, newest-first list, reading either the important or the ordinary notes. */
function sessionNotesOf(records: DentalRecordDto[], kind: "importantNotes" | "notes"): SessionNote[] {
  return records
    .filter((r) => (r[kind]?.length ?? 0) > 0)
    .slice()
    .sort((a, b) => new Date(b.interventionDate).getTime() - new Date(a.interventionDate).getTime())
    .flatMap((record) =>
      (record[kind] ?? []).map((text, i) => ({
        key: `${record.id}-${i}`,
        date: formatDate(record.interventionDate),
        procedure: record.procedureType,
        text,
      })),
    )
}

/**
 * One half of the row. Both halves are the same component so they cannot drift apart in height, padding or type —
 * which is the only way "half and half" actually reads as balanced.
 */
function NotesPanel({
  icon,
  title,
  count,
  action,
  tone,
  emptyLabel,
  children,
}: {
  icon: ReactNode
  title: string
  count: number
  action?: ReactNode
  tone: "alert" | "plain"
  emptyLabel: string
  children: ReactNode
}) {
  const isAlert = tone === "alert"

  return (
    // `h-full` + the grid's default stretch keeps the two halves exactly equal even when one holds far less.
    <section
      className={cn(
        "flex h-full min-w-0 flex-col rounded-lg border",
        isAlert
          ? "border-amber-300 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/40"
          : "border-border bg-card",
      )}
    >
      {/* `min-h-9` floors the header height. It used to be load-bearing because only ONE half carried an action
          button, so without it that header grew taller and started its body lower than the other's. Both halves
          carry « Modifier » now, so the headers match on their own — the floor is kept as the guard against the
          remaining asymmetry: the count badge appears only when a half actually has items. */}
      <div className="flex min-h-9 items-center gap-2 px-3 pt-2">
        <span
          className={cn(
            "[&_svg]:h-4 [&_svg]:w-4",
            isAlert ? "text-amber-700 dark:text-amber-400" : "text-muted-foreground",
          )}
        >
          {icon}
        </span>
        <h2
          className={cn(
            "text-[11px] font-semibold uppercase tracking-wide",
            isAlert ? "text-amber-800 dark:text-amber-300" : "text-muted-foreground",
          )}
        >
          {title}
        </h2>
        {count > 0 && (
          <Badge
            variant={isAlert ? "outline" : "secondary"}
            className={cn(
              "h-5 px-1.5 text-xs font-normal tabular-nums",
              isAlert && "border-amber-300 bg-amber-100 text-amber-900 dark:border-amber-700 dark:bg-amber-900/50 dark:text-amber-100",
            )}
          >
            {count}
          </Badge>
        )}
        {/* Pushed right, and a sibling of nothing clickable — the header itself is not a button here. */}
        <div className="ml-auto">{action}</div>
      </div>

      {/* Bounded and scrolling rather than collapsible: at half width there is room to simply show the content, and
          a fixed ceiling keeps the row's height constant however long either side gets. */}
      <div
        className="min-h-0 flex-1 overflow-y-auto px-3 pb-2.5 pt-1.5"
        style={{ maxHeight: PANEL_BODY_MAX_PX }}
      >
        {count > 0 ? (
          children
        ) : (
          <p className={cn("text-xs", isAlert ? "text-amber-800/80 dark:text-amber-300/80" : "text-muted-foreground")}>
            {emptyLabel}
          </p>
        )}
      </div>
    </section>
  )
}

/**
 * The patient's notes under their name — **one row, two balanced halves**: alerts on the left, ordinary notes on the
 * right.
 *
 * Each half carries both natures of its kind: the patient's own standing facts *and* what each séance recorded. So
 * « Alertes » holds the patient's important notes plus every séance note marked important — an alert you have to
 * open is not an alert — and « Notes » holds the ordinary patient note plus every séance's ordinary notes.
 *
 * Within a half the two natures are separated by **form, not colour**:
 *
 * - a **chip** (alerts) or a plain paragraph (notes) is a standing fact about the patient, undated because it is
 *   simply true;
 * - a **dated line** is an observation from one visit, which is why it names both its date *and its act*:
 *   « difficult patient » from yesterday and from two years ago are not the same fact, and the act is what lets the
 *   reader place the visit it came from.
 *
 * ⚠️ Both bodies share one `PANEL_BODY_MAX_PX` and scroll past it. That replaced a measured two-line clamp with a
 * « +N autres alertes » toggle, which existed only because the band was full-width and always expanded; at half
 * width there is room to just show the content, and a fixed ceiling bounds the row without needing to measure what
 * it cut. Files used to occupy this right half — they are a button in the action row now, which is what freed it.
 */
export function PatientNotesStrip({ patient, records, onEdit }: PatientNotesStripProps) {
  const importantNotes = patient.importantNotes?.trim() || ""
  const notes = patient.notes?.trim() || ""
  const warningChips = splitPatientWarnings(importantNotes)

  const sessionAlerts = sessionNotesOf(records, "importantNotes")
  const sessionNotes = sessionNotesOf(records, "notes")

  // The patient's own block counts as one item when it is prose, or one per chip when it is a keyword list — so the
  // badge matches what the reader can actually count on screen.
  const alertCount = (warningChips ? warningChips.length : importantNotes ? 1 : 0) + sessionAlerts.length
  const noteCount = (notes ? 1 : 0) + sessionNotes.length

  const datedLine = (item: SessionNote, alert: boolean) => (
    <p
      key={item.key}
      className={cn(
        "text-sm leading-snug",
        alert ? "text-amber-950 dark:text-amber-50" : "text-foreground",
      )}
    >
      <span
        className={cn(
          "mr-1.5 whitespace-nowrap text-[11px] font-medium tabular-nums",
          alert ? "text-amber-800/90 dark:text-amber-300/90" : "text-muted-foreground",
        )}
      >
        {item.date}
      </span>
      {item.procedure && (
        <span
          className={cn(
            "mr-1.5 text-[11px]",
            alert ? "text-amber-800/75 dark:text-amber-300/70" : "text-muted-foreground/80",
          )}
        >
          {item.procedure}
        </span>
      )}
      <span className={alert ? "font-medium" : undefined}>{item.text}</span>
    </p>
  )

  /**
   * The « Modifier » control, now on **both** halves.
   *
   * One callback serves the two because both kinds are written in the same place: `edit-patient-dialog`'s
   * « Notes du patient » section holds `importantNotes` and `notes` together. Previously only the Notes half
   * carried it, which made the amber half look read-only — the alerts are just as editable, and the one people
   * most often need to correct.
   *
   * The label is duplicated visually, so each button gets a distinct `aria-label`: two controls both announcing
   * « Modifier » with nothing to tell them apart is worse than no label at all.
   */
  const editAction = (tone: "alert" | "plain") => (
    <Button
      variant="ghost"
      size="sm"
      onClick={onEdit}
      aria-label={tone === "alert" ? "Modifier les alertes du patient" : "Modifier les notes du patient"}
      className={cn(
        "h-7 gap-1.5 px-2 text-xs",
        // Tone-matched, like the icon / title / badge above it: a grey button on the amber panel reads as
        // belonging to a different card.
        tone === "alert"
          ? "text-amber-800 hover:bg-amber-100 hover:text-amber-900 dark:text-amber-300 dark:hover:bg-amber-900/50"
          : "text-muted-foreground",
      )}
    >
      <Pencil className="h-3.5 w-3.5" />
      Modifier
    </Button>
  )

  return (
    <div className="grid gap-3 md:grid-cols-2">
      <NotesPanel
        icon={<AlertTriangle aria-hidden="true" />}
        title="Alertes"
        count={alertCount}
        tone="alert"
        emptyLabel="Aucune alerte pour ce patient."
        action={editAction("alert")}
      >
        <div className="flex flex-col gap-1">
          {importantNotes &&
            (warningChips ? (
              <div className="flex flex-wrap gap-1.5">
                {warningChips.map((chip, i) => (
                  <span
                    key={i}
                    className="inline-flex items-center rounded-full border border-amber-200 bg-amber-100 px-2 py-0.5 text-xs font-semibold text-amber-950 dark:border-amber-800 dark:bg-amber-900/50 dark:text-amber-50"
                  >
                    {chip}
                  </span>
                ))}
              </div>
            ) : (
              <p className="whitespace-pre-wrap text-sm font-medium leading-snug text-amber-950 dark:text-amber-50">
                {importantNotes}
              </p>
            ))}

          {sessionAlerts.map((alert) => datedLine(alert, true))}
        </div>
      </NotesPanel>

      <NotesPanel
        icon={<StickyNote aria-hidden="true" />}
        title="Notes"
        count={noteCount}
        tone="plain"
        emptyLabel="Aucune note pour ce patient."
        action={editAction("plain")}
      >
        <div className="flex flex-col gap-1">
          {notes && <p className="whitespace-pre-wrap text-sm leading-snug text-foreground">{notes}</p>}
          {sessionNotes.map((note) => datedLine(note, false))}
        </div>
      </NotesPanel>
    </div>
  )
}
