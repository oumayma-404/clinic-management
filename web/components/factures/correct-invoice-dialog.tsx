"use client"

import { useState } from "react"
import { toast } from "sonner"
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
import { formatDT } from "@/lib/format"
import { ApiError } from "@/lib/api/client"

/**
 * What the correction is about to do, stated before it happens.
 *
 * <p>The three rows are not decoration: retiring a numbered document and raising another is exactly the kind of
 * thing a dentist should see spelled out before pressing, and the old behaviour — a refusal naming « établissez
 * un avoir » with no way to do it — is what this replaces.</p>
 */
/**
 * What the journal records when a correction is made.
 *
 * <p>The aggregate refuses to void a payment or cancel a note without a reason — that guard is what keeps a
 * numbered document from being retired anonymously — but the dentist is not made to type one. Being asked to
 * justify a typo, in a box, before the product will let you fix it, is friction on the person already having
 * a bad minute. The trail still names the act: who, when, and that it was a correction.</p>
 */
export const DEFAULT_CORRECTION_REASON = "Correction — note remplacée depuis l'application."

export interface CorrectionPreview {
  /** The note being replaced, for the heading. Null on a fiche whose note number is not to hand. */
  invoiceNumber?: string | null
  /** What that note billed and collected. */
  previousTotal: number
  /**
   * What the corrected séance comes to — `null` when the correction has not been written yet.
   *
   * ⚠️ Null is a real case, not a missing value. The fiche path knows the new total (the dentist has already
   * retyped it, which is what got the save refused); /factures opens an empty draft copy, so the only figure it
   * could pass is the old one — and passing that rendered « annulée 190 DT / nouvelle 190 DT », a preview of
   * nothing that read as a bug.
   */
  nextTotal?: number | null
}

interface CorrectInvoiceDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  preview: CorrectionPreview
  /** Why the correction is refused outright — shown instead of the confirm when the patient is owed money. */
  blockedReason?: string | null
  /** Runs the correction. Throws to keep the dialog open with the error shown. */
  onConfirm: () => Promise<void>
}

/**
 * « Corriger la note » — the way out of a refused edit on a billed fiche.
 *
 * <p><b>Correcting is not an avoir.</b> An avoir records money going back to the patient; a mis-keyed amount
 * gave nothing back, so an avoir there states a refund that never happened. Correcting marks the payments as
 * never received, cancels the wrong note and raises the right one — which is what actually occurred. The number
 * is spent and marked cancelled, so the sequence stays gapless.</p>
 *
 * <p>An `AlertDialog`, not a `Dialog`: this is a decision with a consequence, and the primitive that traps focus
 * on the confirm is the one that belongs. It also means the surrounding record modal cannot be clicked behind
 * it.</p>
 */
export function CorrectInvoiceDialog({
  open,
  onOpenChange,
  preview,
  blockedReason,
  onConfirm,
}: CorrectInvoiceDialogProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const nextTotal = preview.nextTotal ?? null
  const difference = nextTotal === null ? 0 : Math.round((preview.previousTotal - nextTotal) * 1000) / 1000
  const label = preview.invoiceNumber ? `n° ${preview.invoiceNumber}` : "de cette séance"

  const handleConfirm = async () => {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
      onOpenChange(false)
    } catch (err) {
      // Kept in the dialog rather than flashed as a toast: the dentist is mid-decision and the next press
      // depends on reading it.
      setError(err instanceof ApiError ? err.message : "La correction a échoué.")
    } finally {
      setBusy(false)
    }
  }

  return (
    <AlertDialog
      open={open}
      onOpenChange={(next) => {
        if (busy) return
        if (!next) setError(null)
        onOpenChange(next)
      }}
    >
      <AlertDialogContent className="max-h-[90dvh] overflow-y-auto">
        <AlertDialogHeader>
          <AlertDialogTitle>Corriger la note {label}</AlertDialogTitle>
          <AlertDialogDescription>
            {blockedReason
              ? blockedReason
              : nextTotal === null
                ? `La note ${label} est recopiée en brouillon modifiable. Rien ne bouge pour l'instant : elle garde son numéro et son paiement jusqu'à ce que la correction soit émise.`
                : "Rien n'est remboursé : l'argent est resté au cabinet. La note fautive est annulée et remplacée par une note corrigée, qui reprend le paiement à sa date d'origine."}
          </AlertDialogDescription>
        </AlertDialogHeader>

        {!blockedReason && nextTotal !== null && (
          <div className="flex flex-col gap-2 text-sm">
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-destructive/50 bg-destructive/5 px-3 py-2">
              <span className="min-w-0 flex-1">Note {label} annulée</span>
              <span className="shrink-0 font-medium tabular-nums">{formatDT(preview.previousTotal)}</span>
            </div>
            <div className="flex flex-wrap items-center gap-2 rounded-md border border-primary/50 bg-primary/5 px-3 py-2">
              <span className="min-w-0 flex-1">Nouvelle note, séance corrigée</span>
              <span className="shrink-0 font-medium tabular-nums">{formatDT(nextTotal)}</span>
            </div>
            {/* ⚠️ States what the correction DOES, then names the other door — only the person at the chair knows
                whether the old figure was really handed over, and this action does not perform a refund. */}
            {difference > 0 && (
              <p className="text-xs text-warning-ink">
                L&apos;encaissement enregistré passe de {formatDT(preview.previousTotal)} à{" "}
                {formatDT(nextTotal)} : la différence est traitée comme jamais reçue. Si le patient a réellement
                versé {formatDT(preview.previousTotal)} et récupère les {formatDT(difference)}, c&apos;est un
                remboursement — établissez un avoir.
              </p>
            )}
            {difference < 0 && (
              <p className="text-xs text-muted-foreground">
                Il reste {formatDT(-difference)} à encaisser sur la nouvelle note.
              </p>
            )}
          </div>
        )}

        {error && (
          <p role="alert" className="text-xs font-medium text-destructive">
            {error}
          </p>
        )}

        <AlertDialogFooter>
          <AlertDialogCancel disabled={busy}>Annuler</AlertDialogCancel>
          {!blockedReason && (
            <AlertDialogAction
              onClick={(e) => {
                // The primitive closes on click by default; the correction has to survive its own failure.
                e.preventDefault()
                void handleConfirm()
              }}
              disabled={busy}
            >
              {busy ? "Correction…" : "Corriger"}
            </AlertDialogAction>
          )}
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}

/** Toast helper shared by the two entry points, so the wording of a success is written once. */
export function toastCorrected(newNumber?: string | null) {
  toast.success(
    newNumber ? `Séance corrigée — nouvelle note n° ${newNumber}.` : "Séance corrigée.",
    { description: "La note précédente est annulée et reste consultable." },
  )
}
