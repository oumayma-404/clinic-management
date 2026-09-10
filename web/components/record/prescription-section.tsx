"use client"

import { Eye, Pill, Plus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { LoadFailureNotice } from "@/components/ui/load-failure"
import {
  PRESCRIPTION_KINDS,
  emptyPrescriptionLine,
  isExamenLine,
  shortPrescriptionLabel,
  type PrescriptionKind,
  type PrescriptionLine,
} from "@/lib/documents"
import type { MedicationDto, PatientDto } from "@/lib/api/types"
import { PatientAlertPanel } from "@/components/patient/patient-alert-panel"
import { RecordSection } from "./record-section"
import { PrescriptionLineRow } from "./prescription-line-row"

/**
 * « Prescription » — the fiche de soins' second folding section, directly under « Notes de séance ».
 *
 * <p><b>It is a `RecordSection` and that choice is the whole density answer.</b> That primitive's contract is
 * that the header carries a live summary of its own contents, so folding makes a value read-only rather than
 * hidden — and folded it costs 34 px, which is what the great majority of séances (nothing prescribed) pay.
 * « Notes de séance » was the only one in this modal; this is the second, and no third idiom was invented.</p>
 *
 * <p>⚠️ <b>The summary NAMES what is prescribed, never counts it.</b> « Augmentin 1 g · panoramique » rather
 * than « 3 prescriptions »: a count tells a reader they must unfold to learn anything, which is exactly the
 * state `RecordSection` exists to avoid.</p>
 *
 * <p>⚠️ <b>Two add buttons, not a mode selector.</b> The gesture already says what it creates. This product has
 * removed a control of that shape once before — see `bridge-identity-and-tooth-gesture`' « the gesture stopped
 * being a mode » — and a segmented « Médicament / Examen » above an empty form is the same mistake: it makes
 * the user answer a question before they have anything to answer it about.</p>
 *
 * <p>⚠️ <b>The section summary is built here, in the browser, and that is not a violation of « what a patient
 * owes is SERVED ».</b> It labels lines the user has typed and not yet saved, which no server can see; the
 * séance <i>history</i> row reads the served `prescriptionSummary` instead. Same words, two different inputs.</p>
 */
interface PrescriptionSectionProps {
  lines: PrescriptionLine[]
  /** Which line is being typed — its index, or null when every line is at rest. */
  armedIndex: number | null
  open: boolean
  onToggle: () => void
  onLinesChange: (lines: PrescriptionLine[]) => void
  onArmedIndexChange: (index: number | null) => void
  catalog: MedicationDto[]
  catalogFailed: boolean
  onRetryCatalog: () => void
  /**
   * The ordonnance this fiche already issued. Only used to say so — the section never offers to delete it,
   * because that paper may already be in the patient's hand and deletion is role-gated on the document itself.
   */
  existingDocumentId?: string | null
  /**
   * The demande d'examens this fiche already issued, if any. A séance owns up to <b>two</b> documents: a
   * médicament and an examen may not share a sheet, so this is a second id and not an alternative to the
   * first. See `DocumentTypes.Examens`.
   */
  existingExamensDocumentId?: string | null
  /**
   * Show the sheet that is about to be emitted, rendered by the server from what is typed. One control per
   * kind present, because there are two documents.
   */
  onPreview?: (kind: PrescriptionKind) => void
  /**
   * The ordonnance exists and could not be read. Rendered as its own state, never as an empty list: « rien de
   * prescrit » beside a real prescription is the reassuring half of a wrong pair, and here it is one save away
   * from being written over the truth.
   */
  readFailed?: boolean
  disabled?: boolean
  /**
   * The patient, for the « À vérifier avant de prescrire » panel — allergies, maladies, médicaments.
   *
   * <p>Optional and read-only: a caller with no patient in hand renders exactly as before, and nothing here
   * writes to it. Allergies are corrected in the patient's file, and an editable copy on a fourth surface is
   * a fourth way to disagree.</p>
   */
  patient?: PatientDto | null
}

/** What the folded header says. Named, never counted — see the type remark. */
export function prescriptionSummary(lines: PrescriptionLine[]): string {
  const labels = lines.map(shortPrescriptionLabel).filter(Boolean)
  return labels.length === 0 ? "aucune prescription" : labels.join(" · ")
}

export function PrescriptionSection({
  lines,
  armedIndex,
  open,
  onToggle,
  onLinesChange,
  onArmedIndexChange,
  catalog,
  catalogFailed,
  onRetryCatalog,
  existingDocumentId,
  existingExamensDocumentId,
  onPreview,
  readFailed,
  disabled,
  patient,
}: PrescriptionSectionProps) {
  const add = (kind: PrescriptionKind) => {
    const next = [...lines, emptyPrescriptionLine(kind)]
    onLinesChange(next)
    // The new line is the one being typed. Arming it here rather than in the parent keeps « exactly one line
    // is open » a property of this section instead of a rule two components have to agree on.
    onArmedIndexChange(next.length - 1)
  }

  const update = (index: number, line: PrescriptionLine) => {
    onLinesChange(lines.map((existing, i) => (i === index ? line : existing)))
  }

  const remove = (index: number) => {
    onLinesChange(lines.filter((_, i) => i !== index))
    if (armedIndex === null) return
    // Keep the armed line pointing at the same line, not at the same index — removing the row above would
    // otherwise silently open its neighbour.
    if (armedIndex === index) onArmedIndexChange(null)
    else if (armedIndex > index) onArmedIndexChange(armedIndex - 1)
  }

  const hasMedication = lines.some((line) => !isExamenLine(line.kind))
  const hasExamen = lines.some((line) => isExamenLine(line.kind))
  const emptied =
    lines.length === 0 && Boolean(existingDocumentId || existingExamensDocumentId) && !readFailed

  return (
    <RecordSection
      title="Prescription"
      summary={
        readFailed ? "l'ordonnance n'a pas pu être lue" : prescriptionSummary(lines)
      }
      open={open}
      onToggle={onToggle}
      highlight={readFailed}
      icon={<Pill className="h-3.5 w-3.5 shrink-0 text-chart-1" aria-hidden="true" />}
    >
      {/*
        ⚠️ **The allergy, the maladies and the current médicaments, beside the box they change.**
        They are already stated at the top of this dialog — and that block is off screen by the time anyone is
        typing « Augmentin », because the fiche scrolls and « Prescription » is the last section in it. This
        panel's own doc has said since it was written that an ordonnance is exactly where it belongs; the
        document editor renders it for that reason and the fiche's own prescription did not.

        ⚠️ It is the SHARED panel with a `purpose`, never a fourth hand-written copy of the three lines — that
        is the defect shape this component was extracted to end. Tabac is the one fact it drops there; see the
        prop's remark. It renders above the failure branch too: not being able to read the existing ordonnance
        is no reason to withhold the allergy.
      */}
      {patient && (
        <PatientAlertPanel patient={patient} purpose="prescribing" className="mb-2" />
      )}
      {readFailed ? (
        /*
         * Nothing else renders — no add buttons — and that is the safe shape rather than a
         * degraded one. With no way to add a line the section sends an empty list, which by contract leaves
         * the existing ordonnance exactly as it is; offering to type into a document we could not read would
         * let one save replace a prescription nobody has seen.
         */
        <LoadFailureNotice
          message="L'ordonnance de cette séance n'a pas pu être lue. Ce n'est pas une séance sans prescription — la lecture a échoué. Rouvrez la fiche pour la modifier ; enregistrer maintenant la laisse telle quelle."
        />
      ) : lines.length === 0 ? (
        <div className="flex flex-col items-center gap-1 px-3 py-2.5 text-center">
          <span className="text-xs font-medium">Rien de prescrit à cette séance</span>
          <span className="max-w-md text-2xs text-muted-foreground">
            Un médicament se choisit dans le catalogue — ou s&apos;écrit librement. Un examen (bilan, radio)
            s&apos;écrit comme vous le diriez au patient.
          </span>
        </div>
      ) : (
        <div className="grid min-w-0 gap-2">
          {lines.map((line, index) => (
            <PrescriptionLineRow
              key={index}
              line={line}
              armed={armedIndex === index}
              onArm={() => onArmedIndexChange(index)}
              onChange={(next) => update(index, next)}
              onRemove={() => remove(index)}
              catalog={catalog}
              catalogFailed={catalogFailed}
              onRetryCatalog={onRetryCatalog}
              disabled={disabled}
            />
          ))}
        </div>
      )}

      {!readFailed && (
      /*
       * ⚠️ `flex-wrap` + a real `basis`, and `shrink` spelled out — § 10.1's trap, measured here at 320 px.
       * `Button` is `whitespace-nowrap shrink-0`, and `flex-1` does NOT remove that: they are different
       * tailwind-merge groups, so the element ends up `flex: 1 1 0%` **and** `flex-shrink: 0`. The two labels
       * then cannot shrink and cannot wrap, so their combined min-content (253 px) sized the section's grid
       * track — and because `RecordSection`'s body is a grid, that pushed **every** sibling to 253 px in a
       * 231 px box: the rows and the closing sentence all ran past the dialog's edge.
       * One un-shrinkable pair of buttons, the whole section over the boundary.
       *
       * `basis-32` is what decides where they stack: 2 × 128 + 8 > 231 at 320 px, so they take a row each
       * there, and fit side by side from 390 px up where there is room for both.
       */
      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          onClick={() => add(PRESCRIPTION_KINDS.medicament)}
          className="h-10 min-w-0 shrink grow basis-32 border-dashed text-muted-foreground"
        >
          <Plus className="me-1.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          Médicament
        </Button>
        <Button
          type="button"
          variant="outline"
          disabled={disabled}
          onClick={() => add(PRESCRIPTION_KINDS.examen)}
          className="h-10 min-w-0 shrink grow basis-32 border-dashed text-muted-foreground"
        >
          <Plus className="me-1.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
          Examen
        </Button>
      </div>
      )}


      {/*
        « Aperçu » — the sheet as it will be printed, composed by the SERVER from what is typed (see
        PreviewFicheOrdonnanceQuery). One control per kind present, because a médicament and an examen are two
        documents; nothing is shown when the section is empty, since there is no sheet to look at.

        ⚠️ It works before the first save, which is the whole point: « montre-moi l'ordonnance avant de la
        donner » is asked at the chair. What it deliberately does NOT offer is Imprimer — the dialog says why.
        ⚠️ `min-w-0 shrink grow basis-40` + `flex-wrap` for § 10.1's reason, same as the add row above: a
        `whitespace-nowrap shrink-0` Button in this grid sizes the track for every sibling.
      */}
      {!readFailed && onPreview && (hasMedication || hasExamen) && (
        <div className="flex flex-wrap gap-2">
          {hasMedication && (
            <Button
              type="button"
              variant="ghost"
              disabled={disabled}
              onClick={() => onPreview(PRESCRIPTION_KINDS.medicament)}
              className="h-9 min-w-0 shrink grow basis-40 justify-start px-2 text-xs text-muted-foreground"
            >
              <Eye className="me-1.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <span className="truncate">Aperçu de l&apos;ordonnance</span>
            </Button>
          )}
          {hasExamen && (
            <Button
              type="button"
              variant="ghost"
              disabled={disabled}
              onClick={() => onPreview(PRESCRIPTION_KINDS.examen)}
              className="h-9 min-w-0 shrink grow basis-40 justify-start px-2 text-xs text-muted-foreground"
            >
              <Eye className="me-1.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <span className="truncate">Aperçu de la demande d&apos;examens</span>
            </Button>
          )}
        </div>
      )}

      {readFailed ? null : emptied ? (
        // The fiche never deletes its ordonnances — see FicheOrdonnanceEmitter. Saying so is the whole reason
        // this branch exists: silently keeping a document the user has just emptied would be the surprise.
        // ⚠️ Plural-aware, because the rule holds per document: a séance can have issued one of each.
        <p className="text-2xs text-muted-foreground" role="status">
          {existingDocumentId && existingExamensDocumentId
            ? "L'ordonnance et la demande d'examens déjà émises pour cette séance restent au dossier. Pour les supprimer, ouvrez-les depuis l'onglet « Documents » du patient."
            : existingExamensDocumentId
              ? "La demande d'examens déjà émise pour cette séance reste au dossier. Pour la supprimer, ouvrez-la depuis l'onglet « Documents » du patient."
              : "L'ordonnance déjà émise pour cette séance reste au dossier. Pour la supprimer, ouvrez-la depuis l'onglet « Documents » du patient."}
        </p>
      ) : (
        lines.length > 0 && (
          /*
           * ⚠️ It names the DOCUMENTS, plural when there are two. « Une ordonnance est émise » would be a
           * half-truth on a séance that also requests a panoramique: two separate papers leave this save, and
           * the patient hands one to the pharmacie and the other to the laboratoire. Saying so here is also
           * the only place a dentist learns why we split them.
           */
          <p className="text-2xs text-muted-foreground">
            {hasMedication && hasExamen
              ? `À l'enregistrement, deux documents ${
                  existingDocumentId && existingExamensDocumentId ? "sont mis à jour" : "sont émis"
                } : une ordonnance pour les médicaments et une demande d'examens — un médicament et un examen ne peuvent pas figurer sur la même ordonnance. Imprimables et envoyables depuis l'onglet « Documents » du patient.`
              : hasExamen
                ? `À l'enregistrement, une demande d'examens ${
                    existingExamensDocumentId ? "est mise à jour" : "est émise au nom du praticien de la séance"
                  } — imprimable et envoyable depuis l'onglet « Documents » du patient.`
                : `À l'enregistrement, l'ordonnance de cette séance ${
                    existingDocumentId ? "est mise à jour" : "est émise au nom du praticien de la séance"
                  } — imprimable et envoyable depuis l'onglet « Documents » du patient.`}
          </p>
        )
      )}
    </RecordSection>
  )
}
