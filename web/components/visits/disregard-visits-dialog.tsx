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
 * « Retirer de la liste » — take séances off « À clôturer » without claiming anything clinical about them.
 *
 * <p><b>Why the worklist needed a third exit.</b> Its only others are « Venu », « Absent » and an annulation —
 * three statements about what happened to a patient. A row that should never have been on the list answers none
 * of them, so clearing it meant asserting one that was false: a cabinet whose Google import filled the list with
 * a week of past events cancelled them to tidy up, and watched its « taux d'absence » climb to a figure it knew
 * was wrong. This asserts nothing, and the server keeps it out of the dashboard's figures as well as the list.</p>
 *
 * <p>⚠️ <b>Not « Rien à facturer ».</b> That mark answers the <i>third</i> question — this séance raises no
 * document — and leaves the first two standing. This one says the row does not belong here at all. A visit can
 * legitimately carry either.</p>
 *
 * <p><b>One motif for the whole selection.</b> A mandatory motif per row is unusable across the hundred-odd rows
 * this exists for; no motif at all would make the bulk door the one everybody reaches for precisely because it
 * asks nothing. The server refuses a blank one too, so the disabled button is a courtesy rather than the guard.</p>
 */

interface DisregardVisitsDialogProps {
  /** The séances to set aside; `null` closes the dialog. One row, or every row on screen. */
  visits: VisitToCloseDto[] | null
  onOpenChange: (open: boolean) => void
  onDone: () => void
}

export function DisregardVisitsDialog({ visits, onOpenChange, onDone }: DisregardVisitsDialogProps) {
  const [reason, setReason] = useState("")
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A fresh selection is a fresh question: carrying the previous motif over is how a colleague's reasoning ends
  // up attached to séances it was never about.
  useEffect(() => {
    if (visits) {
      setReason("")
      setError(null)
    }
  }, [visits])

  const count = visits?.length ?? 0
  const single = count === 1 ? visits?.[0] : undefined

  const submit = async () => {
    if (!visits || visits.length === 0 || reason.trim().length === 0) return

    setSubmitting(true)
    setError(null)

    try {
      const result = await appointmentsApi.disregardVisits(
        visits.map((v) => v.appointmentId),
        true,
        reason.trim(),
      )

      toast.success(
        result.changed === 1
          ? "Séance retirée de la liste."
          : `${result.changed.toLocaleString("fr-TN")} séances retirées de la liste.`,
      )
      onDone()
    } catch (err) {
      // § 13 — the dialog stays open with the motif still typed in it.
      setError(getErrorMessage(err))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Dialog open={visits !== null} onOpenChange={onOpenChange}>
      <DialogContent className="md:max-w-lg">
        <DialogHeader>
          <DialogTitle>
            {count > 1 ? `Retirer ${count.toLocaleString("fr-TN")} séances de la liste` : "Retirer de la liste"}
          </DialogTitle>
          <DialogDescription>
            {single
              ? `Séance de ${single.patientName} du ${formatDateTime(single.appointmentDateTime)}.`
              : count > 1
                ? "Ces séances quitteront « À clôturer »."
                : ""}
          </DialogDescription>
        </DialogHeader>

        {error && <FormErrorBanner message={error} />}

        {/*
          Said plainly, because it is the difference between this control and the two beside it — and because a
          practice reaching for it is usually here to fix a statistic that is already wrong.
        */}
        <p className="rounded-md bg-muted/50 p-3 text-sm text-muted-foreground">
          Rien n’est supprimé et rien n’est affirmé sur ces séances : elles quittent simplement la liste et ne
          sont plus comptées dans vos chiffres — ni comme honorées, ni comme absences. Vous pourrez les
          réafficher à tout moment.
        </p>

        <div className="space-y-2">
          <Label htmlFor="disregard-reason">Motif</Label>
          <Textarea
            id="disregard-reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Ex. importées par erreur depuis Google Agenda, créneau jamais occupé…"
            rows={3}
            maxLength={500}
          />
          <p className="text-xs text-muted-foreground">
            {count > 1
              ? "Le même motif est enregistré sur chaque séance retirée."
              : "Le motif reste visible sur la séance : il explique plus tard pourquoi elle a été retirée."}
          </p>
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={submitting}>
            Annuler
          </Button>
          <Button onClick={submit} disabled={submitting || reason.trim().length === 0}>
            {submitting && <Loader2 aria-hidden="true" className="me-1.5 size-4 animate-spin" />}
            Retirer de la liste
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
