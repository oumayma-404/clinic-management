"use client"

import { useState } from "react"
import { toast } from "sonner"
import { CalendarClock, Copy, Phone, Cake, ArrowRight } from "lucide-react"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { patientsApi } from "@/lib/api/patients"
import { formatDateFr } from "@/lib/format"
import { getErrorMessage } from "@/lib/errors"
import { cn } from "@/lib/utils"
import type { PatientDto } from "@/lib/api/types"

/**
 * « Doublon possible » — the calendar import's duplicate question, as a <b>quiet chip on the row</b> that opens a
 * side-by-side comparison of the two fiches.
 *
 * <p>⚠️ <b>The question is NOT asked in the row itself, and that was the first attempt.</b> A grey sentence
 * (« S'agit-il de Leila Gharbi (né(e) le 2 mars 1985), 22334455 ? ») above two answers put three controls in one
 * cell, made the row twice as tall as its neighbours, and pushed « Compléter les infos patient » — the action every
 * row has — out of line. Worse, it asked the reader to decide whether two people are the same from <i>one wrapped
 * line of small print</i>, when what the decision needs is both records shown next to each other.</p>
 *
 * <p>⚠️ So: one chip, one dialog, and <b>the dialog is the confirmation</b> — there is no second « êtes-vous
 * sûr ? » on top of it. A comparison the reader has just studied, with the consequence written under the two
 * columns, is a better guard than a modal that repeats two names.</p>
 *
 * <p>⚠️ Renders <b>nothing</b> without a suggestion, which is the ordinary case — most imported patients resemble
 * nobody. Callers mount it unconditionally.</p>
 */
export function DuplicateSuggestionPrompt({
  patient,
  onResolved,
  className,
}: {
  patient: PatientDto
  /** Called after a merge or a refusal has committed — the caller reloads, or navigates away. */
  onResolved?: (outcome: "merged" | "rejected", survivingPatientId?: string) => void
  className?: string
}) {
  const suggestion = patient.suggestedDuplicate
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)

  if (!suggestion) {
    return null
  }

  const patientName = `${patient.firstName} ${patient.lastName}`.trim()

  const merge = async () => {
    setBusy(true)
    try {
      const result = await patientsApi.mergeIntoSuggestedDuplicate(patient.id)
      setOpen(false)
      toast.success(
        result.appointmentsMoved > 0
          ? `Fiches fusionnées — ${result.appointmentsMoved} rendez-vous rattaché${result.appointmentsMoved > 1 ? "s" : ""} à ${result.survivingPatientName}.`
          : `Fiches fusionnées sur ${result.survivingPatientName}.`,
      )
      // ⚠️ Its own toast, not folded into the sentence above: nothing failed, and which séance stands is the
      // practice's decision. The server reports the overlap and never refuses on it.
      if (result.overlapsExisting) {
        toast.warning(`${result.survivingPatientName} a maintenant deux rendez-vous qui se chevauchent.`)
      }
      onResolved?.("merged", result.survivingPatientId)
    } catch (err) {
      // The refusal names its own blocker (« 1 fiche de soins y sont rattachés »), so it is shown verbatim, and
      // the dialog stays open with the comparison on screen.
      toast.error(getErrorMessage(err, "La fusion des fiches a échoué."))
    } finally {
      setBusy(false)
    }
  }

  const reject = async () => {
    setBusy(true)
    try {
      await patientsApi.rejectSuggestedDuplicate(patient.id)
      setOpen(false)
      toast.success("Compris — deux patients différents. La fiche reste à compléter.")
      onResolved?.("rejected")
    } catch (err) {
      toast.error(getErrorMessage(err, "La proposition n'a pas pu être retirée."))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {/* A chip, not a button-shaped control: it is a *remark* about the row, and the row already has one primary
          action. `coarse:min-h-11` rather than `.touch-target` — it sits inline with text and an overlay would
          overhang the line above it. */}
      <button
        type="button"
        onClick={() => setOpen(true)}
        className={cn(
          "inline-flex w-fit items-center gap-1.5 rounded-full border border-amber-300 bg-amber-50 px-2.5 py-1",
          "text-2xs font-medium text-amber-900 transition-colors hover:bg-amber-100",
          "dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200 dark:hover:bg-amber-900/60",
          "coarse:min-h-11 coarse:px-3 coarse:text-xs",
          className,
        )}
      >
        <Copy className="h-3 w-3 shrink-0" aria-hidden />
        Doublon possible
      </button>

      <Dialog open={open} onOpenChange={(next) => !busy && setOpen(next)}>
        <DialogContent className="md:max-w-2xl">
          <DialogHeader>
            <DialogTitle>Est-ce le même patient ?</DialogTitle>
            <DialogDescription>
              Ces deux fiches portent le même nom écrit différemment. Comparez-les avant de répondre.
            </DialogDescription>
          </DialogHeader>

          {/*
            ⚠️ Side by side from `sm:`, stacked below it, and the arrow between them says which way the merge goes.
            This is the whole reason the question left the row: « Imen » and « Iman » cannot be told apart from a
            name, only from the birth date and the number beside it — so both records are shown in full, and a
            field neither fiche has is stated as « non renseigné » rather than omitted, since *that* is often the
            reason the imported one is a duplicate at all.
          */}
          <div className="grid gap-3 sm:grid-cols-[1fr_auto_1fr] sm:items-center">
            <FicheCard
              heading="Fiche importée"
              tone="amber"
              name={patientName}
              dateOfBirth={patient.dateOfBirth}
              phone={patient.phoneNumber}
              footnote={
                patient.calendarImportPendingReviewSince
                  ? `Créée depuis Google Agenda le ${formatDateFr(patient.calendarImportPendingReviewSince)}`
                  : "Créée depuis Google Agenda"
              }
            />
            <div className="flex items-center justify-center text-muted-foreground" aria-hidden>
              <ArrowRight className="h-4 w-4 rotate-90 sm:rotate-0" />
            </div>
            <FicheCard
              heading="Patient existant"
              tone="default"
              name={suggestion.fullName}
              dateOfBirth={suggestion.dateOfBirth}
              phone={suggestion.phone}
              footnote={suggestion.phoneMatches ? "Même numéro de téléphone que la fiche importée" : undefined}
              footnoteStrong={suggestion.phoneMatches}
            />
          </div>

          {/* The consequence, under the comparison and above the answers — this dialog IS the confirmation. */}
          <p className="text-xs text-muted-foreground">
            En répondant «&nbsp;oui&nbsp;», la fiche importée est supprimée et son rendez-vous rattaché à{" "}
            <span className="font-medium text-foreground">{suggestion.fullName}</span>.{" "}
            <span className="font-medium text-foreground">Cette action est irréversible.</span>
          </p>

          {/* ⚠️ `flex-col-reverse` below `sm:` is `DialogFooter`'s own behaviour, so the affirmative sits at the
              top of the stack on a phone — nearest the thumb, and it is the answer with consequences. */}
          <DialogFooter className="gap-2">
            <Button variant="outline" className="coarse:h-11" disabled={busy} onClick={() => void reject()}>
              Non, deux patients différents
            </Button>
            <Button className="coarse:h-11" disabled={busy} onClick={() => void merge()}>
              {busy ? "Fusion…" : "Oui, fusionner les fiches"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  )
}

/** One of the two fiches under comparison. Both columns render identically so the eye can diff them. */
function FicheCard({
  heading,
  tone,
  name,
  dateOfBirth,
  phone,
  footnote,
  footnoteStrong,
}: {
  heading: string
  tone: "amber" | "default"
  name: string
  dateOfBirth?: string | null
  phone?: string | null
  footnote?: string
  footnoteStrong?: boolean
}) {
  const amber = tone === "amber"
  return (
    <div
      className={cn(
        "min-w-0 rounded-md border p-3",
        amber ? "border-amber-200 bg-amber-50/60 dark:border-amber-900 dark:bg-amber-950/40" : "bg-card",
      )}
    >
      <p
        className={cn(
          "text-2xs font-medium uppercase tracking-wide",
          amber ? "text-amber-800 dark:text-amber-300" : "text-muted-foreground",
        )}
      >
        {heading}
      </p>
      <p className="mt-1 break-words text-sm font-semibold">{name}</p>
      <dl className="mt-2 space-y-1 text-xs text-muted-foreground">
        <Row icon={Cake} label="Naissance" value={dateOfBirth ? formatDateFr(dateOfBirth) : null} />
        <Row icon={Phone} label="Téléphone" value={phone ?? null} />
      </dl>
      {footnote && (
        <p
          className={cn(
            "mt-2 flex items-start gap-1.5 text-2xs",
            footnoteStrong ? "font-medium text-foreground" : "text-muted-foreground",
          )}
        >
          <CalendarClock className="mt-px h-3 w-3 shrink-0" aria-hidden />
          {footnote}
        </p>
      )}
    </div>
  )
}

/** ⚠️ « non renseigné », never an omitted row: an absent birth date is the point of comparison, not a blank. */
function Row({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof Phone
  label: string
  value: string | null
}) {
  return (
    <div className="flex items-baseline gap-1.5">
      <Icon className="h-3 w-3 shrink-0 translate-y-px" aria-hidden />
      <dt className="sr-only">{label}</dt>
      <dd className={cn("min-w-0 break-words", value ? "text-foreground" : "italic")}>{value ?? "non renseigné"}</dd>
    </div>
  )
}
