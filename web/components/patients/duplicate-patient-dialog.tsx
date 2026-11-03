"use client"

import { BookingPromptFacts } from "@/components/appointment-recap"
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

/**
 * « Ce patient existe peut-être déjà » for a walk-in typed into another form (guided-first-days D) — the booking
 * dialog and the devis form. The server's own sentence names who was matched and why, so it is shown verbatim.
 *
 * <p>⚠️ The destructive-looking option is « Créer quand même », not the cancel: a duplicate cannot be merged or
 * deleted afterwards, so it is the irreversible choice and is styled as one. The cancel is named after what it
 * does — back to the patient list, the typed name cleared — because the ordinary answer is « this is the patient you
 * already have ». Nothing was created, so there is nothing to keep.</p>
 */
export function DuplicatePatientDialog({
  message,
  patientName,
  busy = false,
  onPickExisting,
  onCreateAnyway,
  onDismiss,
}: {
  /** The server's refusal, or null when closed. */
  message: string | null
  patientName: string | null
  busy?: boolean
  onPickExisting: () => void
  onCreateAnyway: () => void
  onDismiss: () => void
}) {
  return (
    <AlertDialog open={message !== null} onOpenChange={(o) => { if (!o) onDismiss() }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>
            {patientName ? `Créer quand même un dossier pour ${patientName} ?` : "Créer quand même ce patient ?"}
          </AlertDialogTitle>
          <AlertDialogDescription asChild>
            <BookingPromptFacts message={message} />
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={busy} onClick={onPickExisting}>
            Choisir un patient existant
          </AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onCreateAnyway} disabled={busy}>
            Créer quand même
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  )
}
