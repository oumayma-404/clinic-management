"use client"

import { useEffect, useState } from "react"
import { Loader2 } from "lucide-react"
import { toast } from "sonner"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
import { Label } from "@/components/ui/label"
import { Textarea } from "@/components/ui/textarea"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { appointmentsApi } from "@/lib/api/appointments"
import { getErrorMessage } from "@/lib/errors"
import { formatDateTime } from "@/lib/format"
import type { VisitToCloseDto } from "@/lib/api/types"

/**
 * « Rien à facturer » — the escape hatch of « À clôturer », and the one place a motif is asked for.
 *
 * <p><b>Why a dialog and not a one-tap toggle.</b> The mark closes the money question for good, so the row leaves
 * the worklist and nothing will ask again. Three legitimate cases are already derived server-side — a fiche worth
 * nothing, a séance carried by a devis, an existing note — so anyone reaching this control is asserting something
 * none of those cover. « Pourquoi cette séance n'a produit aucun document ? » has to stay answerable months later,
 * and a blank motif answers nothing; the server refuses one too, so this is a courtesy rather than the guard.</p>
 *
 * <p>Free text on purpose: a closed list would be a second thing to maintain, and the first clinic needing a motif
 * that is not on it would pick the nearest wrong one.</p>
 */

interface NothingToBillDialogProps {
  /** The visit to mark; `null` closes the dialog. */
  visit: VisitToCloseDto | null
  onOpenChange: (open: boolean) => void
  onSuccess: () => void
}

export function NothingToBillDialog({ visit, onOpenChange, onSuccess }: NothingToBillDialogProps) {
  const [reason, setReason] = useState("")
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A fresh visit is a fresh question: carrying the previous motif over is how a colleague's reasoning ends up
  // attached to a séance it was never about.
  useEffect(() => {
    if (visit) {
      setReason("")
      setError(null)
    }
  }, [visit])

  const submit = async () => {
    if (!visit || reason.trim().length === 0) return

    setSubmitting(true)
    setError(null)
    try {
      await appointmentsApi.setNothingToBill(visit.appointmentId, true, reason.trim())
      toast.success("Séance clôturée sans facturation.")
      onSuccess()
    } catch (err) {
      // § 13 — the dialog stays open with the motif still typed in it.
      setError(getErrorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open={visit !== null} onOpenChange={onOpenChange}>
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>Rien à facturer</DialogTitle>
          <DialogDescription>
            {visit
              ? `Séance de ${visit.patientName} du ${formatDateTime(visit.appointmentDateTime)}. Elle ne produira aucune note d’honoraires et quittera la liste à clôturer.`
              : ""}
          </DialogDescription>
        </DialogHeader>

        {error && <FormErrorBanner message={error} />}

        <div className="space-y-2">
          <Label htmlFor="nothing-to-bill-reason">Motif</Label>
          <Textarea
            id="nothing-to-bill-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Ex. contrôle offert, acte repris sous garantie…"
            rows={3}
            maxLength={500}
          />
          <p className="text-xs text-muted-foreground">
            Le motif reste visible sur la séance : il explique plus tard pourquoi elle n’a produit aucun document.
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={submitting}>
            Annuler
          </Button>
          <Button onClick={submit} disabled={submitting || reason.trim().length === 0}>
            {submitting && <Loader2 aria-hidden="true" className="me-1.5 size-4 animate-spin" />}
            Enregistrer
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
