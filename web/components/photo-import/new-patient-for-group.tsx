"use client"

import { useEffect, useId, useRef, useState } from "react"
import { toast } from "sonner"
import { Loader2, UserPlus } from "lucide-react"
import { Button } from "@/components/ui/button"
import { DuplicatePatientDialog } from "@/components/patients/duplicate-patient-dialog"
import { NewPatientFields } from "@/components/patients/new-patient-fields"
import { showErrorToast } from "@/lib/errors"
import { useInlineNewPatient } from "@/lib/hooks/use-inline-new-patient"
import type { PatientRef } from "@/lib/photo-import/session"

/**
 * « Nouveau patient » under a group of photos (`features/camera-photo-import`) — the walk-in nobody registered: no
 * visit, no fiche, no record, and the photos are on the card. Created here and chosen for the group in one press, so
 * the review is not left half-way for another screen.
 *
 * The rules are the booking dialog's own (`useInlineNewPatient`): a first and a last name, a number only if it can be
 * dialled, « Ce patient existe peut-être déjà » asked rather than refused, and never the same patient twice.
 */
export function NewPatientForGroup({
  typed,
  onCreated,
  onCancel,
  onPickExisting,
}: {
  /** What was typed in the search — split into Prénom and Nom, both editable. */
  typed: string
  onCreated: (patient: PatientRef) => void
  onCancel: () => void
  /** « Choisir un patient existant » from the duplicate question: back to the search. */
  onPickExisting: () => void
}) {
  const patient = useInlineNewPatient()
  const prefix = useId().replace(/:/g, "")
  const [busy, setBusy] = useState(false)
  const [invalid, setInvalid] = useState<string | null>(null)
  const [duplicate, setDuplicate] = useState<string | null>(null)
  const seeded = useRef(false)

  // « Sonia Mejri » → Prénom « Sonia », Nom « Mejri »: the order a receptionist types a name in.
  useEffect(() => {
    if (seeded.current) return
    seeded.current = true
    const [first = "", ...rest] = typed.trim().split(/\s+/)
    patient.setFirstName(rest.length > 0 ? first : "")
    patient.setLastName(rest.length > 0 ? rest.join(" ") : first)
  }, [typed, patient])

  const create = async (allowDuplicate: boolean) => {
    const problem = patient.validate()
    setInvalid(problem)
    if (problem || busy) return
    setBusy(true)
    const name = [patient.firstName.trim(), patient.lastName.trim()].join(" ")
    const outcome = await patient.ensureCreated(allowDuplicate)
    setBusy(false)
    if (outcome.kind === "created") {
      setDuplicate(null)
      toast.success(`Patient créé : ${name}`)
      onCreated({ patientId: outcome.id, patientName: name })
    } else if (outcome.kind === "duplicate") {
      setDuplicate(outcome.message)
    } else {
      showErrorToast(outcome.error)
    }
  }

  return (
    // A form, so Enter in a field (or the phone keyboard's « OK ») creates — the button sits under the keyboard there.
    <form
      className="space-y-3 rounded-md border border-border bg-muted/30 p-3"
      onSubmit={(event) => {
        event.preventDefault()
        void create(false)
      }}
    >
      <p className="flex items-center gap-2 text-sm font-medium">
        <UserPlus className="size-4 shrink-0 text-primary" aria-hidden="true" />
        Nouveau patient
      </p>
      <NewPatientFields patient={patient} idPrefix={`${prefix}-`} savedWhat="le choix du groupe" disabled={busy} />
      {invalid && (
        <p role="alert" className="text-sm text-destructive">
          {invalid}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="submit" className="gap-1.5 coarse:min-h-11" disabled={busy}>
          {busy && <Loader2 className="size-4 motion-safe:animate-spin" aria-hidden="true" />}
          Créer et choisir
        </Button>
        <Button type="button" variant="ghost" className="coarse:min-h-11" disabled={busy} onClick={onCancel}>
          Annuler
        </Button>
      </div>

      <DuplicatePatientDialog
        message={duplicate}
        patientName={patient.typedName}
        busy={busy}
        onPickExisting={() => {
          setDuplicate(null)
          onPickExisting()
        }}
        onCreateAnyway={() => void create(true)}
        onDismiss={() => setDuplicate(null)}
      />
    </form>
  )
}
