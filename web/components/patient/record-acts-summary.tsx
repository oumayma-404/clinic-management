"use client"

import Link from "next/link"
import type { ReactNode } from "react"
import { FlaskConical, Pill } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"
import type { DentalRecordDto } from "@/lib/api/types"

interface RecordActsSummaryProps {
  record: DentalRecordDto
  /** Card lists right-align their values; the table reads from the start. */
  align?: "start" | "end"
  /**
   * Drop the act's name when there is only one — for a surface that already names it elsewhere (the card list
   * puts it in the card's own title). With several acts the name is what the teeth are attached to, so it is
   * always kept.
   */
  hideSingleName?: boolean
  className?: string
  /**
   * Opens the séance's ordonnance. Omitted on a surface with no route to it (the read-only summary modal), and
   * the prescription line then renders as plain text rather than as a dead control.
   */
  onOpenPrescription?: (documentId: string) => void
  /**
   * Drop the prescription and examens lines entirely — for a surface that states them as its own labelled
   * fields.
   *
   * ⚠️ **The patient page's card tree did exactly that and did not pass this**, so every fiche carrying an
   * ordonnance printed its médicaments twice: once unlabelled inside « Actes » (as plain text, since the card
   * tree passes no `onOpenPrescription`) and again under « Prescription » directly beneath. The card's own
   * comment already claimed the field was there « rather than » the line; only half of that had been done.
   */
  hidePrescription?: boolean
}

/**
 * A saved fiche's acts, **each with its own teeth**.
 *
 * <p>⚠️ It exists because the séance was read back as two independent columns: a « Type d'acte » holding the
 * server's comma-joined summary (« Radiographie panoramique, Extraction simple ») and a « Dents » holding the
 * flat UNION of every act's teeth (27, 36, 37, 13, 43). Both were true and neither said which tooth belonged to
 * which act — so a two-act séance could not be read back at all, and a reader's most likely conclusion is that
 * both acts were done on all five teeth.</p>
 *
 * <p>The single-act case is left exactly as it was — a name and its teeth, with no per-act scaffolding — because
 * that is the overwhelming majority of fiches and the ambiguity does not exist there.</p>
 */
/**
 * What a séance of a multi-séance treatment actually WAS — the step, with its rank.
 *
 * <p>⚠️ <b>Without it a patient's history printed the ACT once per séance.</b> Three fiches of one implant read
 * « Implant dentaire · Implant dentaire · Implant dentaire », which says the patient had three implants — and
 * nothing on any of the three rows said they were one treatment. The step label and its rank were on record
 * (<c>TreatmentPlanItemStep</c>) and simply never read back.</p>
 *
 * <p>Rendered under the act's name rather than replacing it: « Implant dentaire » is what the patient and the
 * devis both call the work, and « Pose de l'implant » alone would lose it.</p>
 *
 * <p>⚠️ <b>It is the way TO that treatment, not a note about it.</b> The line was inert text, so the one row
 * that knows which treatment the séance belongs to sent the reader to « Plan de traitement » to find it again
 * among all of them — and a Draft (« Suivre ce traitement ») has no number to search by at all. Shaped after
 * {@link PatientNameLink}: underlined <b>at rest</b> so it is discoverable without a hover, and
 * `coarse:min-h-11` rather than `.touch-target`, whose absolute overlay would overhang the row above in these
 * dense tables.</p>
 */
function SeanceIdentity({ record }: { record: DentalRecordDto }) {
  if (!record.treatmentPlanId) return null
  const rank =
    record.treatmentStepNumber && record.treatmentStepTotal
      ? `étape ${record.treatmentStepNumber} / ${record.treatmentStepTotal}`
      : null
  // Names the treatment when it HAS a name: a followed treatment is un-numbered (`Accept` is the only writer
  // of `Number`), and « Ouvrir le traitement undefined » is what a template would have printed.
  const label = `Ouvrir le traitement${record.treatmentPlanNumber ? ` ${record.treatmentPlanNumber}` : ""}`
  const linked = (children: ReactNode) => (
    <Link
      href={`/treatment-plans/${record.treatmentPlanId}`}
      // Several of these lists make the whole row or card clickable; without it the row handler wins the race
      // and the link looks right while doing nothing.
      onClick={(e) => e.stopPropagation()}
      aria-label={label}
      title={label}
      // ⚠️ `w-fit` is a fix, not tidiness: the single-act tree is a `flex flex-col`, so a flex item
      // STRETCHES — measured at 1440 px, a 119 px label sat in a 299 px anchor, i.e. 180 px of invisible
      // clickable area to its right, and a click on the empty half of the cell navigated off the patient
      // file with nothing having said it would. A link is as wide as its own label.
      className="inline-flex w-fit max-w-full items-center rounded-sm text-2xs text-muted-foreground underline decoration-muted-foreground/50 underline-offset-2 transition-colors hover:text-primary hover:decoration-primary coarse:min-h-11"
    >
      {children}
    </Link>
  )
  // An act booked whole has no step, so it says only which treatment it belongs to — which is still the fact
  // the row was missing.
  if (!record.treatmentStepLabel && !rank) {
    return linked(
      <span>
        séance du traitement{record.treatmentPlanNumber ? ` ${record.treatmentPlanNumber}` : ""}
      </span>,
    )
  }
  return linked(
    <span>
      {record.treatmentStepLabel}
      {rank ? <span className="opacity-80"> · {rank}</span> : null}
    </span>,
  )
}

/**
 * What the séance prescribed, on the history row — one 11 px line under the séance identity.
 *
 * <p><b>It NAMES what was prescribed</b> (« Prescrit : Augmentin Comprimé 1 g, Ibuprofène 400 mg ») rather than
 * merely marking that something was, which is what makes it worth 16 px: « quel antibiotique lui ai-je
 * donné ? » is answered without opening anything. The full ordonnance is one tap away.</p>
 *
 * <p>⚠️ <b>The word « Prescrit » is VISIBLE, not only in the accessible name.</b> A pill glyph alone is not a
 * label, and this product has already shipped the mirror of that mistake — a count whose only qualifier lived
 * in an `sr-only` span, leaving the sighted reader with a bare figure to guess at (N31).</p>
 *
 * <p>⚠️ The labels come from the SERVER (`prescriptionSummary`). The printed sentence has one owner and a table
 * cell has no business reimplementing it.</p>
 */
function PrescriptionLine({
  record,
  onOpen,
  kind = "prescription",
}: {
  record: DentalRecordDto
  onOpen?: (documentId: string) => void
  /**
   * Which of the séance's two sheets this line names. ⚠️ **One component, two instances — never one line
   * merging both**: a médicament and an examen are separate documents going to separate places, so a row
   * reading « Prescrit : Augmentin, Panoramique » would say the patient has one paper to hand over when they
   * have two, and clicking it could only ever open one of them.
   */
  kind?: "prescription" | "examens"
}) {
  const isExamens = kind === "examens"
  const labels = (isExamens ? record.examensSummary : record.prescriptionSummary) ?? []
  const documentId = isExamens ? record.examensDocumentId : record.prescriptionDocumentId
  if (labels.length === 0 || !documentId) return null

  const noun = isExamens ? "la demande d'examens" : "l'ordonnance"
  const text = `${isExamens ? "Examens" : "Prescrit"} : ${labels.join(", ")}`
  const Icon = isExamens ? FlaskConical : Pill

  const body = (
    <>
      <Icon className="h-3 w-3 shrink-0" aria-hidden="true" />
      {/* Truncates to one line: the whole list is on the ordonnance, and a history row that grows with a
          six-drug prescription stops being a row. */}
      <span className="min-w-0 truncate">{text}</span>
    </>
  )

  if (!onOpen) {
    return (
      <span className="flex max-w-full items-center gap-1.5 text-2xs text-muted-foreground" title={text}>
        {body}
      </span>
    )
  }

  return (
    <button
      type="button"
      onClick={() => onOpen(documentId)}
      // Grows its own box rather than taking `.touch-target`: it sits directly under the séance-identity line
      // inside a table cell, so an overlay would overhang the row above it.
      className="flex min-h-6 max-w-full items-center gap-1.5 text-start text-2xs text-primary hover:underline coarse:min-h-11"
      aria-label={`Ouvrir ${noun} — ${labels.join(", ")}`}
    >
      {body}
    </button>
  )
}

export function RecordActsSummary({
  record,
  align = "start",
  hideSingleName,
  className,
  onOpenPrescription,
  hidePrescription,
}: RecordActsSummaryProps) {
  const acts = record.acts ?? []
  const justify = align === "end" ? "justify-end" : "justify-start"

  const teeth = (numbers: number[]) =>
    numbers.length > 0 ? (
      <span className={cn("inline-flex flex-wrap gap-1", justify)}>
        {numbers.map((n) => (
          <Badge key={n} variant="secondary" className="text-xs tabular-nums">
            {n}
          </Badge>
        ))}
      </span>
    ) : (
      <span className="text-xs italic text-muted-foreground">acte général</span>
    )

  // No per-act detail on the wire (an older row, or a partial read): say what is known rather than nothing.
  if (acts.length <= 1) {
    const name = acts.length === 1 ? acts[0].procedureName : record.procedureType
    const numbers = acts.length === 1 ? (acts[0].toothNumbers ?? []) : record.toothNumbers
    return (
      <div className={cn("flex flex-col gap-1", align === "end" && "items-end", className)}>
        {!hideSingleName && <span className="text-sm">{name}</span>}
        <SeanceIdentity record={record} />
        {teeth(numbers)}
        {!hidePrescription && (
          <>
            <PrescriptionLine record={record} onOpen={onOpenPrescription} />
            <PrescriptionLine record={record} onOpen={onOpenPrescription} kind="examens" />
          </>
        )}
      </div>
    )
  }

  return (
    <ul className={cn("flex flex-col gap-1.5", className)}>
      {/* On a mixed séance the step names the visit, not any one act, so it leads the list rather than
          repeating on each row. */}
      {record.treatmentPlanId && (
        <li className={cn("flex", justify)}>
          <SeanceIdentity record={record} />
        </li>
      )}
      {acts.map((act, i) => (
        <li
          key={`${act.procedureName}-${i}`}
          className={cn("flex flex-wrap items-center gap-x-2 gap-y-1", justify)}
        >
          <span className="text-sm">{act.procedureName}</span>
          {teeth(act.toothNumbers ?? [])}
        </li>
      ))}
      {/* Last, and once for the whole séance — an ordonnance is written for the visit, not per act. */}
      {!hidePrescription && (record.prescriptionSummary?.length ?? 0) > 0 && (
        <li className={cn("flex", justify)}>
          <PrescriptionLine record={record} onOpen={onOpenPrescription} />
        </li>
      )}
      {!hidePrescription && (record.examensSummary?.length ?? 0) > 0 && (
        <li className={cn("flex", justify)}>
          <PrescriptionLine record={record} onOpen={onOpenPrescription} kind="examens" />
        </li>
      )}
    </ul>
  )
}
