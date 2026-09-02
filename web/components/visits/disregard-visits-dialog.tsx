"use client"

import { useEffect, useState } from "react"
import { Loader2 } from "lucide-react"
import { toast } from "sonner"
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog"
import { Button } from "@/components/ui/button"
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
 * <p>⚠️ <b>It asks for nothing, and that is a reversal of how it first shipped.</b> It demanded a motif, on
 * « Rien à facturer »'s reasoning — and the parallel does not hold: that one is a claim about money the cabinet
 * may be asked to justify later, whereas this asserts nothing, so there is nothing to justify. Over the
 * hundred-odd rows this exists for, a mandatory sentence priced the honest exit above the dishonest one — and the
 * dishonest one is the annulation that started the whole problem. So the dialog <b>confirms</b> rather than
 * collects: it is still worth showing, because « rien n'est supprimé » is the part staff do not assume.</p>
 */

interface DisregardVisitsDialogProps {
  /** The séances to set aside; `null` closes the dialog. One row, or every row on screen. */
  visits: VisitToCloseDto[] | null
  onOpenChange: (open: boolean) => void
  onDone: () => void
}

export function DisregardVisitsDialog({ visits, onOpenChange, onDone }: DisregardVisitsDialogProps) {
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A fresh selection is a fresh question: a previous attempt's error must not greet the next one.
  useEffect(() => {
    if (visits) {
      setError(null)
    }
  }, [visits])

  const count = visits?.length ?? 0
  const single = count === 1 ? visits?.[0] : undefined

  const submit = async () => {
    if (!visits || visits.length === 0) return

    setSubmitting(true)
    setError(null)

    try {
      const result = await appointmentsApi.disregardVisits(
        visits.map((v) => v.appointmentId),
        true,
      )

      /*
       * ⚠️ `refused` must be reported, or the count lies by omission. The server refuses a séance carrying a fiche
       * de soins or a note d'honoraires vivante, and answers with the rows it did NOT touch rather than failing the
       * whole selection — so « 34 séances retirées » over a selection of 40 is true and still leaves six rows on
       * screen with nothing said about why they stayed. That reads as the button half-working.
       */
      const refused = result.refused.length
      const moved = result.changed === 1
        ? "Séance retirée de la liste."
        : `${result.changed.toLocaleString("fr-TN")} séances retirées de la liste.`

      if (refused > 0) {
        toast.warning(result.changed === 0 ? "Aucune séance retirée." : moved, {
          description: refused === 1
            ? "1 séance a été conservée : elle a une fiche de soins ou une note d’honoraires."
            : `${refused.toLocaleString("fr-TN")} séances ont été conservées : elles ont une fiche de soins `
              + "ou une note d’honoraires.",
        })
      } else {
        toast.success(moved)
      }

      onDone()
    } catch (err) {
      // § 13 — the dialog stays open so the action can be retried without rebuilding the selection.
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

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={submitting}>
            Annuler
          </Button>
          <Button onClick={submit} disabled={submitting}>
            {submitting && <Loader2 aria-hidden="true" className="me-1.5 size-4 animate-spin" />}
            Retirer de la liste
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
