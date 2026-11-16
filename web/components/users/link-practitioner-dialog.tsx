"use client"

import { useEffect, useState } from "react"
import { toast } from "sonner"
import { Button } from "@/components/ui/button"
import {
  Dialog,
  DialogBody,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog"
import { FormErrorBanner } from "@/components/ui/form-error-banner"
import { usersApi, type ClinicUserDto } from "@/lib/api/users"
import type { DoctorDto } from "@/lib/api/clinics"
import { getErrorMessage } from "@/lib/errors"
import { DOCTOR_SPECIALTIES } from "@/lib/specialties"
import {
  PractitionerChoice,
  defaultPractitionerChoice,
  practitionerChoiceReady,
  practitionerPayload,
  type PractitionerFields,
} from "@/components/users/practitioner-choice"

interface LinkPractitionerDialogProps {
  /** The account to give a practitioner record; null closes the dialog. */
  user: ClinicUserDto | null
  linkable: readonly DoctorDto[]
  onClose: () => void
  onLinked: () => void
}

/**
 * « Lier à un praticien » (cabinet-de-groupe US-3): an admin or doctor account with no practitioner record gets one —
 * the roster's existing record for this person, or a new one. Without one their ordonnances print no cachet and their
 * work is credited to nobody.
 */
export function LinkPractitionerDialog({ user, linkable, onClose, onLinked }: LinkPractitionerDialogProps) {
  const [choice, setChoice] = useState<string>("")
  const [fields, setFields] = useState<PractitionerFields>({
    firstName: "",
    lastName: "",
    specialty: DOCTOR_SPECIALTIES[0],
  })
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // A new opening starts from this account's own likely record (same e-mail), and an empty form otherwise.
  useEffect(() => {
    if (!user) return
    setChoice(defaultPractitionerChoice(linkable, user.email ?? ""))
    setFields({ firstName: "", lastName: "", specialty: DOCTOR_SPECIALTIES[0] })
    setError(null)
    // Only on a new account: the roster refreshing behind an open dialog must not reset what was chosen.
  }, [user?.id])

  const submit = async () => {
    if (!user) return
    setSaving(true)
    setError(null)
    try {
      await usersApi.linkPractitioner(user.id, practitionerPayload(choice, fields))
      toast.success("Fiche praticien liée.")
      onLinked()
      onClose()
    } catch (err) {
      setError(getErrorMessage(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Dialog open={user !== null} onOpenChange={(open) => !open && !saving && onClose()}>
      <DialogContent className="md:max-w-md">
        <DialogHeader>
          <DialogTitle>Lier à un praticien</DialogTitle>
          <DialogDescription>{user?.fullName || user?.email}</DialogDescription>
        </DialogHeader>
        <DialogBody className="space-y-4">
          <FormErrorBanner message={error} />
          <PractitionerChoice
            idPrefix="link-practitioner"
            linkable={linkable}
            choice={choice}
            onChoiceChange={setChoice}
            fields={fields}
            onFieldsChange={setFields}
            disabled={saving}
          />
        </DialogBody>
        <DialogFooter>
          <Button variant="outline" className="coarse:h-11" onClick={onClose} disabled={saving}>
            Annuler
          </Button>
          <Button
            className="coarse:h-11"
            onClick={submit}
            disabled={saving || !choice || !practitionerChoiceReady(choice, fields)}
          >
            {saving ? "Enregistrement…" : "Lier"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
